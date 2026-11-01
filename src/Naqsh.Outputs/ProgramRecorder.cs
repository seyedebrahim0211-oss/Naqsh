using FFmpeg.AutoGen;
using Naqsh.Core.Collections;
using Naqsh.Core.Formats;
using Naqsh.Core.Timing;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Media;
using Naqsh.Engine.Outputs;
using Serilog;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Naqsh.Outputs;

/// <summary>
/// Records Program to Matroska while recording (a crash leaves a playable file) and remuxes to a
/// standard MP4 on stop. GPU → CPU readback uses a ring of staging textures read two frames late,
/// so the render thread never waits for the GPU; encoding runs on its own thread.
/// </summary>
public sealed class ProgramRecorder : IRecorder
{
    private const int StagingCount = 3;
    private const int BufferCount = 8;

    private readonly ILogger _log;
    private readonly VideoFormat _format;
    private readonly ID3D11Texture2D[] _staging = new ID3D11Texture2D[StagingCount];
    private readonly long[] _stagingFrame = new long[StagingCount];
    private readonly bool[] _stagingPending = new bool[StagingCount];
    private readonly SpscRing<CapturedFrame> _filled = new(BufferCount);
    private readonly SpscRing<byte[]> _free = new(BufferCount);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly string _mkvPath;
    private readonly int _width;
    private readonly int _height;

    private unsafe AVFormatContext* _oc;
    private unsafe AVCodecContext* _codec;
    private unsafe AVStream* _stream;
    private unsafe AVFrame* _frame;
    private unsafe AVPacket* _packet;
    private unsafe SwsContext* _sws;

    private readonly unsafe byte*[] _srcData = new byte*[4];
    private readonly int[] _srcStride = new int[4];
    private readonly unsafe byte*[] _dstData = new byte*[4];
    private readonly int[] _dstStride = new int[4];

    private long _submitted;
    private long _firstFrame = -1;
    private long _dropped;
    private long _written;
    private volatile bool _stopping;
    private volatile string? _failure;
    private bool _closed;

    private ProgramRecorder(GpuDevice gpu, VideoFormat format, string folder, ILogger log)
    {
        _log = log;
        _format = format;
        _width = format.Width;
        _height = format.Height;
        Directory.CreateDirectory(folder);
        var baseName = EncoderCatalog.RecordingBaseName(DateTime.Now);
        _mkvPath = Path.Combine(folder, baseName + ".mkv");
        FilePath = _mkvPath;
        StartedUtc = DateTimeOffset.UtcNow;

        string encoderName;
        try
        {
            OpenEncoder(out encoderName);
            OpenOutput();
        }
        catch
        {
            _failure = "Recording";
            CloseOutput();
            if (File.Exists(_mkvPath))
            {
                File.Delete(_mkvPath);
            }

            throw;
        }

        EncoderName = encoderName;

        var desc = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)_width,
            (uint)_height,
            1,
            1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            1,
            0,
            ResourceOptionFlags.None);
        for (var i = 0; i < StagingCount; i++)
        {
            _staging[i] = gpu.Device.CreateTexture2D(desc);
        }

        for (var i = 0; i < BufferCount; i++)
        {
            _free.TryEnqueue(new byte[_width * _height * 4]);
        }

        _thread = new Thread(EncodeThread) { IsBackground = false, Name = "Naqsh recorder", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public string FilePath { get; private set; }

    public string EncoderName { get; }

    public DateTimeOffset StartedUtc { get; }

    public long DroppedFrames => Interlocked.Read(ref _dropped);

    public string? FailureCode => _failure;

    public static ProgramRecorder Start(GpuDevice gpu, VideoFormat format, string folder, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(log);
        if (!FfmpegRuntime.EnsureLoaded(log))
        {
            throw new RecorderException("FfmpegMissing", "FFmpeg is not available.");
        }

        return new ProgramRecorder(gpu, format, folder, log);
    }

    // ------------------------------------------------------------ render thread

    public void SubmitFrame(ID3D11DeviceContext context, RenderTarget programBgra, long frameIndex)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(programBgra);
        if (_stopping || _failure is not null)
        {
            return;
        }

        if (_firstFrame < 0)
        {
            _firstFrame = frameIndex;
        }

        var slot = (int)(_submitted % StagingCount);
        if (_stagingPending[slot])
        {
            ReadBack(context, slot);
        }

        context.CopyResource(_staging[slot], programBgra.Texture);
        _stagingFrame[slot] = frameIndex;
        _stagingPending[slot] = true;
        _submitted++;

        // Read back the copy made two frames ago (the GPU has finished it by now).
        var oldest = (int)(_submitted % StagingCount);
        if (_stagingPending[oldest])
        {
            ReadBack(context, oldest);
        }
    }

    private unsafe void ReadBack(ID3D11DeviceContext context, int slot)
    {
        _stagingPending[slot] = false;
        if (!_free.TryDequeue(out var buffer))
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        if (context.Map(_staging[slot], 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).Failure)
        {
            _free.TryEnqueue(buffer);
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            var rowBytes = _width * 4;
            fixed (byte* dst = buffer)
            {
                var src = (byte*)mapped.DataPointer;
                for (var y = 0; y < _height; y++)
                {
                    Buffer.MemoryCopy(src + ((long)y * mapped.RowPitch), dst + ((long)y * rowBytes), rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            context.Unmap(_staging[slot], 0);
        }

        _filled.TryEnqueue(new CapturedFrame(buffer, _stagingFrame[slot]));
        _wake.Set();
    }

    // ------------------------------------------------------------ control thread

    public async Task<RecordingResult> StopAsync(CancellationToken ct)
    {
        _stopping = true;
        _wake.Set();
        await Task.Run(() => _thread.Join(), ct).ConfigureAwait(false);
        var error = _failure;
        try
        {
            CloseOutput();
        }
        catch (InvalidDataException ex)
        {
            _log.Error(ex, "Closing the recording failed");
            error ??= "Recording";
        }

        var remuxed = false;
        if (error is null || _written > 0)
        {
            var mp4 = Path.ChangeExtension(_mkvPath, ".mp4");
            remuxed = await Task.Run(() => Mp4Remuxer.TryRemux(_mkvPath, mp4, _log), ct).ConfigureAwait(false);
            if (remuxed)
            {
                File.Delete(_mkvPath);
                FilePath = mp4;
            }
            else
            {
                error ??= "RemuxFailed";
            }
        }

        return new RecordingResult(FilePath, remuxed, _written, DroppedFrames, error);
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        try
        {
            CloseOutput();
        }
        catch (InvalidDataException ex)
        {
            _log.Warning(ex, "Recorder cleanup");
        }

        foreach (var s in _staging)
        {
            s?.Dispose();
        }

        _wake.Dispose();
    }

    // ------------------------------------------------------------ encoder thread

    private void EncodeThread()
    {
        try
        {
            while (true)
            {
                if (_filled.TryDequeue(out var f))
                {
                    Encode(f);
                    _free.TryEnqueue(f.Pixels);
                    continue;
                }

                if (_stopping)
                {
                    break;
                }

                _wake.WaitOne(50);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            _log.Error(ex, "Encoding failed");
            _failure = "Recording";
        }
    }

    private unsafe void Encode(CapturedFrame f)
    {
        Check(ffmpeg.av_frame_make_writable(_frame), "frame writable");
        fixed (byte* src = f.Pixels)
        {
            _srcData[0] = src;
            _srcStride[0] = _width * 4;
            for (uint i = 0; i < 4; i++)
            {
                _dstData[i] = _frame->data[i];
                _dstStride[i] = _frame->linesize[i];
            }

            ffmpeg.sws_scale(_sws, _srcData, _srcStride, 0, _height, _dstData, _dstStride);
        }

        _frame->pts = f.FrameIndex - _firstFrame;
        Check(ffmpeg.avcodec_send_frame(_codec, _frame), "send frame");
        DrainPackets();
        _written++;
    }

    private unsafe void DrainPackets()
    {
        while (true)
        {
            var r = ffmpeg.avcodec_receive_packet(_codec, _packet);
            if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF)
            {
                return;
            }

            Check(r, "receive packet");
            ffmpeg.av_packet_rescale_ts(_packet, _codec->time_base, _stream->time_base);
            _packet->stream_index = _stream->index;
            Check(ffmpeg.av_interleaved_write_frame(_oc, _packet), "write packet");
        }
    }

    // ------------------------------------------------------------ setup / teardown

    private unsafe void OpenEncoder(out string encoderName)
    {
        var rate = _format.Rate;
        foreach (var candidate in EncoderCatalog.H264)
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name(candidate.FfmpegName);
            if (codec is null)
            {
                continue;
            }

            var ctx = ffmpeg.avcodec_alloc_context3(codec);
            ctx->width = _width;
            ctx->height = _height;
            ctx->time_base = new AVRational { num = rate.Denominator, den = rate.Numerator };
            ctx->framerate = new AVRational { num = rate.Numerator, den = rate.Denominator };
            ctx->pix_fmt = candidate.UsesNv12 ? AVPixelFormat.AV_PIX_FMT_NV12 : AVPixelFormat.AV_PIX_FMT_YUV420P;
            ctx->bit_rate = EncoderCatalog.BitrateFor(_width, _height, rate.Value);
            ctx->gop_size = rate.NominalFps * 2;
            ctx->max_b_frames = 0;
            ctx->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
            ctx->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
            ctx->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            ctx->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            var opened = ffmpeg.avcodec_open2(ctx, codec, null);
            if (opened < 0)
            {
                _log.Information("Encoder {Encoder} unavailable: {Error}", candidate.FfmpegName, FfmpegRuntime.ErrorText(opened));
                ffmpeg.avcodec_free_context(&ctx);
                continue;
            }

            _codec = ctx;
            encoderName = candidate.DisplayName;
            _frame = ffmpeg.av_frame_alloc();
            _frame->format = (int)ctx->pix_fmt;
            _frame->width = _width;
            _frame->height = _height;
            Check(ffmpeg.av_frame_get_buffer(_frame, 32), "frame buffer");
            _packet = ffmpeg.av_packet_alloc();
            _sws = ffmpeg.sws_getContext(_width, _height, AVPixelFormat.AV_PIX_FMT_BGRA, _width, _height, ctx->pix_fmt, (int)SwsFlags.SWS_POINT, null, null, null);
            var bt709 = *(int_array4*)ffmpeg.sws_getCoefficients(ffmpeg.SWS_CS_ITU709);
            ffmpeg.sws_setColorspaceDetails(
                _sws,
                bt709,
                1,
                bt709,
                0,
                0,
                1 << 16,
                1 << 16);
            return;
        }

        throw new RecorderException("NoEncoder", "No H.264 encoder could be opened.");
    }

    private unsafe void OpenOutput()
    {
        AVFormatContext* oc = null;
        Check(ffmpeg.avformat_alloc_output_context2(&oc, null, "matroska", _mkvPath), "output context");
        _oc = oc;
        _stream = ffmpeg.avformat_new_stream(oc, null);
        Check(ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _codec), "stream parameters");
        _stream->time_base = _codec->time_base;
        _stream->avg_frame_rate = _codec->framerate;
        var tc = Timecode.FromTimeOfDay(DateTime.Now.TimeOfDay, _format.Rate).ToString();
        ffmpeg.av_dict_set(&_stream->metadata, "timecode", tc, 0);
        ffmpeg.av_dict_set(&oc->metadata, "encoder", "Naqsh", 0);
        Check(ffmpeg.avio_open(&oc->pb, _mkvPath, ffmpeg.AVIO_FLAG_WRITE), "open file");
        Check(ffmpeg.avformat_write_header(oc, null), "write header");
    }

    private unsafe void CloseOutput()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            if (_codec is not null && _oc is not null && _failure is null)
            {
                ffmpeg.avcodec_send_frame(_codec, null);
                DrainPackets();
            }

            if (_oc is not null)
            {
                ffmpeg.av_write_trailer(_oc);
            }
        }
        finally
        {
            if (_oc is not null)
            {
                ffmpeg.avio_closep(&_oc->pb);
                ffmpeg.avformat_free_context(_oc);
                _oc = null;
            }

            var codec = _codec;
            ffmpeg.avcodec_free_context(&codec);
            _codec = null;
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
        }
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
        {
            throw new InvalidDataException($"FFmpeg {what} failed: {FfmpegRuntime.ErrorText(result)}");
        }
    }

    private readonly record struct CapturedFrame(byte[] Pixels, long FrameIndex);
}

/// <summary>Factory used by the engine host.</summary>
public sealed class ProgramRecorderFactory : IRecorderFactory
{
    private readonly ILogger _log;

    public ProgramRecorderFactory(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IRecorder Start(GpuDevice gpu, VideoFormat format, string folder)
    {
        try
        {
            return ProgramRecorder.Start(gpu, format, folder, _log);
        }
        catch (InvalidDataException ex)
        {
            throw new RecorderException("Recording", ex.Message);
        }
    }
}
