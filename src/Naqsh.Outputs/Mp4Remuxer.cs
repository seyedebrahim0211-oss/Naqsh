using FFmpeg.AutoGen;
using Naqsh.Engine.Media;
using Serilog;

namespace Naqsh.Outputs;

/// <summary>Stream-copies a Matroska recording into a standard MP4 (fast-start, timecode kept).</summary>
public static unsafe class Mp4Remuxer
{
    public static bool TryRemux(string inputPath, string outputPath, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        AVFormatContext* input = null;
        AVFormatContext* output = null;
        AVPacket* packet = null;
        AVDictionary* options = null;
        var ok = false;
        try
        {
            Check(ffmpeg.avformat_open_input(&input, inputPath, null, null), "open input");
            Check(ffmpeg.avformat_find_stream_info(input, null), "stream info");
            Check(ffmpeg.avformat_alloc_output_context2(&output, null, "mp4", outputPath), "output context");
            ffmpeg.av_dict_copy(&output->metadata, input->metadata, 0);
            var map = new int[input->nb_streams];
            var next = 0;
            for (var i = 0; i < input->nb_streams; i++)
            {
                var inStream = input->streams[i];
                var type = inStream->codecpar->codec_type;
                if (type != AVMediaType.AVMEDIA_TYPE_VIDEO && type != AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    map[i] = -1;
                    continue;
                }

                map[i] = next++;
                var outStream = ffmpeg.avformat_new_stream(output, null);
                Check(ffmpeg.avcodec_parameters_copy(outStream->codecpar, inStream->codecpar), "copy parameters");
                outStream->codecpar->codec_tag = 0;
                outStream->time_base = inStream->time_base;
                ffmpeg.av_dict_copy(&outStream->metadata, inStream->metadata, 0);
            }

            Check(ffmpeg.avio_open(&output->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE), "open output");
            ffmpeg.av_dict_set(&options, "movflags", "+faststart", 0);
            Check(ffmpeg.avformat_write_header(output, &options), "write header");
            packet = ffmpeg.av_packet_alloc();
            while (ffmpeg.av_read_frame(input, packet) >= 0)
            {
                var si = packet->stream_index;
                if (si >= map.Length || map[si] < 0)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                var inStream = input->streams[si];
                var outStream = output->streams[map[si]];
                ffmpeg.av_packet_rescale_ts(packet, inStream->time_base, outStream->time_base);
                packet->stream_index = map[si];
                packet->pos = -1;
                Check(ffmpeg.av_interleaved_write_frame(output, packet), "write packet");
            }

            Check(ffmpeg.av_write_trailer(output), "write trailer");
            ok = true;
            log.Information("Remuxed {Input} to {Output}", inputPath, outputPath);
        }
        catch (InvalidDataException ex)
        {
            log.Error(ex, "Remux to MP4 failed; the MKV is kept");
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avformat_close_input(&input);
            if (output is not null)
            {
                ffmpeg.avio_closep(&output->pb);
                ffmpeg.avformat_free_context(output);
            }

            if (!ok && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }

        return ok;
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
        {
            throw new InvalidDataException($"FFmpeg {what} failed: {FfmpegRuntime.ErrorText(result)}");
        }
    }
}
