using FFmpeg.AutoGen;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Media;
using Naqsh.Engine.Sources;
using Serilog;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Naqsh.Inputs;

/// <summary>
/// Video file input. A decoder thread (FFmpeg, software decode in Phase 1) converts frames to BGRA
/// into a fixed ring of pooled buffers; the render thread picks the frame whose timestamp matches
/// the playback clock (nearest frame) and uploads it to a dynamic texture. The render-thread path
/// allocates nothing. Auto-play: plays when taken to Program, pauses and rewinds when it leaves.
/// </summary>
public sealed unsafe class VideoFileSource : IVideoSource
{
    private const int RingSize = 6;
    private const int MaxWidth = 3840;
    private const int MaxHeight = 2160;

    private readonly ILogger _log;
    private readonly string _path;
    private readonly ID3D11Device1 _device;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly RingFrame[] _ring = new RingFrame[RingSize];
    private volatile bool _stop;

    // Producer/consumer counters (single producer = decoder, single consumer = render thread).
    private long _write;
    private long _read;

    // Seek requests from the render thread.
    private int _generation;

    // Published by the decoder thread after open.
    private volatile int _width;
    private volatile int _height;
    private volatile InputStatus _status = InputStatus.Loading;
    private double? _duration;

    // Render-thread state.
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _view;
    private DrawSource _current = DrawSource.Black with { Slate = DrawSource.OfflineSlate };
    private bool _playing;
    private long _playStartClock;
    private long _playStartMedia;
    private long _pausedMedia;
    private long _shownPts = long.MinValue;
    private bool _hasShown;

    public VideoFileSource(InputDescriptor descriptor, GpuDevice gpu, string fullPath, ILogger log)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentNullException.ThrowIfNull(gpu);
        _device = gpu.Device;
        _path = fullPath;
        _log = log;
        for (var i = 0; i < RingSize; i++)
        {
            _ring[i] = new RingFrame();
        }

        _thread = new Thread(DecodeThread) { IsBackground = true, Name = "Naqsh video decode" };
        _thread.Start();
    }

    public Guid Id => Descriptor.Id;

    public InputDescriptor Descriptor { get; }

    public InputStatus Status => _status;

    public double? DurationSeconds => _duration;

    public DrawSource Current => _current;

    public void OnProgramChanged(bool onProgram, long clockTicks)
    {
        if (!Descriptor.AutoPlay)
        {
            return;
        }

        if (onProgram)
        {
            _playing = true;
            _playStartClock = clockTicks;
            _playStartMedia = _pausedMedia;
        }
        else
        {
            // Pause and rewind to the start so the clip is cued for the next take.
            _playing = false;
            _pausedMedia = 0;
            _hasShown = false;
            _shownPts = long.MinValue;
            Interlocked.Increment(ref _generation);
            _wake.Set();
        }
    }

    public void Update(ID3D11DeviceContext context, in FrameContext frame)
    {
        if (_status != InputStatus.Ready)
        {
            if (_status is InputStatus.Offline or InputStatus.ComponentMissing)
            {
                _current = DrawSource.Black with { Slate = DrawSource.OfflineSlate };
            }

            return;
        }

        if (_texture is null)
        {
            CreateTexture();
        }

        var halfFrame = frame.Format.Rate.FrameToTicks(1) / 2;
        var mediaTime = _playing ? _playStartMedia + (frame.ClockTicks - _playStartClock) : _pausedMedia;
        var gen = Volatile.Read(ref _generation);
        var r = _read;
        var w = Volatile.Read(ref _write);
        RingFrame? best = null;
        while (r < w)
        {
            var f = _ring[r % RingSize];
            if (f.Generation != gen)
            {
                r++;
                continue;
            }

            if (!_hasShown || f.PtsTicks <= mediaTime + halfFrame)
            {
                best = f;
                r++;
                if (!_hasShown)
                {
                    break;
                }

                continue;
            }

            break;
        }

        if (best is not null)
        {
            Upload(context, best);
            _shownPts = best.PtsTicks;
            if (!_hasShown)
            {
                _hasShown = true;
                if (!_playing)
                {
                    _pausedMedia = best.PtsTicks;
                }
            }
        }

        if (r != _read)
        {
            Volatile.Write(ref _read, r);
            _wake.Set();
        }

        if (_playing)
        {
            _pausedMedia = Math.Max(_shownPts, 0);
        }

        _current = new DrawSource(_view, _width, _height, false, DrawSource.BlackSlate);
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
        {
            _log.Warning("Video decoder thread for {Id} did not stop", Id);
        }

        _view?.Dispose();
        _texture?.Dispose();
        _wake.Dispose();
    }

    private void CreateTexture()
    {
        var desc = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)_width,
            (uint)_height,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            1,
            0,
            ResourceOptionFlags.None);
        _texture = _device.CreateTexture2D(desc);
        _view = _device.CreateShaderResourceView(_texture);
    }

    private void Upload(ID3D11DeviceContext context, RingFrame f)
    {
        if (context.Map(_texture!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None, out var mapped).Failure)
        {
            return;
        }

        try
        {
            var rowBytes = _width * 4;
            fixed (byte* src = f.Pixels)
            {
                var dst = (byte*)mapped.DataPointer;
                for (var y = 0; y < _height; y++)
                {
                    Buffer.MemoryCopy(src + ((long)y * rowBytes), dst + ((long)y * mapped.RowPitch), mapped.RowPitch, rowBytes);
                }
            }
        }
        finally
        {
            context.Unmap(_texture!, 0);
        }
    }

    // ------------------------------------------------------------------ decoder thread

    private void DecodeThread()
    {
        if (!FfmpegRuntime.EnsureLoaded(_log))
        {
            _status = InputStatus.ComponentMissing;
            return;
        }

        var delay = TimeSpan.FromSeconds(2);
        while (!_stop)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    throw new FileNotFoundException("Video file not found.", _path);
                }

                RunDecoder();
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                if (_status != InputStatus.Offline)
                {
                    _log.Warning(ex, "Video {Id} unavailable; retrying", Id);
                }

                _status = InputStatus.Offline;
            }

            _wake.WaitOne(delay);
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private void RunDecoder()
    {
        AVFormatContext* fmt = null;
        AVCodecContext* codec = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        SwsContext* sws = null;
        try
        {
            Check(ffmpeg.avformat_open_input(&fmt, _path, null, null), "open");
            Check(ffmpeg.avformat_find_stream_info(fmt, null), "stream info");
            AVCodec* decoder = null;
            var streamIndex = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
            Check(streamIndex, "no video stream");
            var stream = fmt->streams[streamIndex];
            codec = ffmpeg.avcodec_alloc_context3(decoder);
            Check(ffmpeg.avcodec_parameters_to_context(codec, stream->codecpar), "codec parameters");
            codec->thread_count = 0;
            Check(ffmpeg.avcodec_open2(codec, decoder, null), "open decoder");

            var (w, h) = FitSize(codec->width, codec->height);
            for (var i = 0; i < RingSize; i++)
            {
                _ring[i].Pixels = new byte[w * h * 4];
                _ring[i].Generation = -1;
            }

            _duration = fmt->duration > 0 ? fmt->duration / (double)ffmpeg.AV_TIME_BASE : null;
            _width = w;
            _height = h;
            _status = InputStatus.Ready;
            _log.Information("Video opened {Id}: {W}x{H} {Codec}", Id, codec->width, codec->height, ffmpeg.avcodec_get_name(codec->codec_id));

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            var tb = stream->time_base;
            var srcData = new byte*[4];
            var srcStride = new int[4];
            var dstData = new byte*[4];
            var dstStride = new int[4] { w * 4, 0, 0, 0 };
            var localGen = Volatile.Read(ref _generation);
            long loopBase = 0;
            long lastPts = 0;
            long frameDur = 0;
            var eof = false;
            var swsFormat = AVPixelFormat.AV_PIX_FMT_NONE;
            var swsW = 0;
            var swsH = 0;

            while (!_stop)
            {
                var gen = Volatile.Read(ref _generation);
                if (gen != localGen)
                {
                    ffmpeg.av_seek_frame(fmt, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    ffmpeg.avcodec_flush_buffers(codec);
                    localGen = gen;
                    loopBase = 0;
                    eof = false;
                }

                if (Volatile.Read(ref _write) - Volatile.Read(ref _read) >= RingSize || eof)
                {
                    _wake.WaitOne(20);
                    continue;
                }

                var rd = ffmpeg.av_read_frame(fmt, packet);
                if (rd == ffmpeg.AVERROR_EOF)
                {
                    ffmpeg.avcodec_send_packet(codec, null);
                }
                else if (rd < 0)
                {
                    throw new InvalidDataException("Read error: " + FfmpegRuntime.ErrorText(rd));
                }
                else if (packet->stream_index != streamIndex)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }
                else
                {
                    ffmpeg.avcodec_send_packet(codec, packet);
                    ffmpeg.av_packet_unref(packet);
                }

                while (ffmpeg.avcodec_receive_frame(codec, frame) == 0)
                {
                    var ts = frame->best_effort_timestamp;
                    var pts = ts == ffmpeg.AV_NOPTS_VALUE ? lastPts + frameDur : ffmpeg.av_rescale_q(ts, tb, new AVRational { num = 1, den = 10_000_000 });
                    if (pts > lastPts)
                    {
                        frameDur = pts - lastPts;
                    }

                    lastPts = pts;
                    if (sws is null || frame->format != (int)swsFormat || frame->width != swsW || frame->height != swsH)
                    {
                        ffmpeg.sws_freeContext(sws);
                        swsFormat = (AVPixelFormat)frame->format;
                        swsW = frame->width;
                        swsH = frame->height;
                        sws = ffmpeg.sws_getContext(swsW, swsH, swsFormat, w, h, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
                        if (sws is null)
                        {
                            throw new InvalidDataException("Unsupported pixel format.");
                        }
                    }

                    var slot = _ring[Volatile.Read(ref _write) % RingSize];
                    for (uint i = 0; i < 4; i++)
                    {
                        srcData[i] = frame->data[i];
                        srcStride[i] = frame->linesize[i];
                    }

                    fixed (byte* dst = slot.Pixels)
                    {
                        dstData[0] = dst;
                        ffmpeg.sws_scale(sws, srcData, srcStride, 0, swsH, dstData, dstStride);
                    }

                    slot.PtsTicks = loopBase + pts;
                    slot.Generation = localGen;
                    Volatile.Write(ref _write, Volatile.Read(ref _write) + 1);
                    ffmpeg.av_frame_unref(frame);
                }

                if (rd == ffmpeg.AVERROR_EOF)
                {
                    if (Descriptor.Loop)
                    {
                        loopBase += lastPts + Math.Max(frameDur, 1);
                        ffmpeg.av_seek_frame(fmt, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
                        ffmpeg.avcodec_flush_buffers(codec);
                        lastPts = 0;
                    }
                    else
                    {
                        eof = true;
                    }
                }
            }
        }
        finally
        {
            ffmpeg.sws_freeContext(sws);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&codec);
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    private static (int W, int H) FitSize(int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            throw new InvalidDataException("Invalid video size.");
        }

        if (w <= MaxWidth && h <= MaxHeight)
        {
            return (w & ~1, h & ~1);
        }

        var scale = Math.Min((double)MaxWidth / w, (double)MaxHeight / h);
        return ((int)(w * scale) & ~1, (int)(h * scale) & ~1);
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
        {
            throw new InvalidDataException($"FFmpeg {what} failed: {FfmpegRuntime.ErrorText(result)}");
        }
    }

    private sealed class RingFrame
    {
        public byte[] Pixels { get; set; } = [];

        public long PtsTicks { get; set; }

        public int Generation { get; set; } = -1;
    }
}
