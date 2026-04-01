using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace UltraFrameAI;

internal sealed unsafe class FfmpegApiFrameEncoderSession : IFrameEncoderSession
{
    private readonly FrameEncoderSessionConfig _config;
    private readonly Action<string>? _onStderr;
    private AVFormatContext* _formatCtx;
    private AVFormatContext* _sourceCtx;
    private AVFormatContext* _subtitleCtx;
    private AVCodecContext* _codecCtx;
    private AVStream* _stream;
    private AVStream* _audioStream;
    private AVStream* _subtitleStream;
    private SwsContext* _sws;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private bool _opened;
    private bool _headerWritten;
    private string? _lastError;
    private bool _hasPendingTimestamp;
    private double _pendingTimestampSeconds;
    private long _nextPts;
    private AVRational _timeBase;
    private int _audioStreamIndex = -1;
    private int _subtitleStreamIndex = -1;

    public FfmpegApiFrameEncoderSession(FrameEncoderSessionConfig config, Action<string>? onStderr)
    {
        _config = config;
        _onStderr = onStderr;
    }

    public int ExitCode => _opened ? 0 : -1;

    public string? LastError => _lastError;

    public bool SupportsPerFrameTimestamps => true;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_opened)
        {
            return Task.CompletedTask;
        }

        if (!FfmpegApiRuntime.TryInitialize(out var error))
        {
            _lastError = error ?? "FFmpeg API initialization failed.";
            throw new InvalidOperationException(_lastError);
        }

        EnsureNoCancellation(cancellationToken);

        var outputPath = _config.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Output path is empty.");
        }

        var outputContainer = NormalizeOutputContainer(_config.OutputContainer);
        AVFormatContext* formatCtx = null;
        var allocResult = ffmpeg.avformat_alloc_output_context2(&formatCtx, null, outputContainer, outputPath);
        if (allocResult < 0 || formatCtx == null)
        {
            ThrowFfmpeg("Unable to create output format context.", allocResult);
        }

        _formatCtx = formatCtx;

        var encoder = ffmpeg.avcodec_find_encoder_by_name(_config.Codec);
        if (encoder == null)
        {
            ThrowFfmpeg($"Encoder '{_config.Codec}' not found.", ffmpeg.AVERROR_ENCODER_NOT_FOUND);
        }

        _stream = ffmpeg.avformat_new_stream(_formatCtx, encoder);
        if (_stream == null)
        {
            ThrowFfmpeg("Unable to create output stream.", ffmpeg.AVERROR_UNKNOWN);
        }

        _codecCtx = ffmpeg.avcodec_alloc_context3(encoder);
        if (_codecCtx == null)
        {
            ThrowFfmpeg("Unable to allocate codec context.", ffmpeg.AVERROR(12));
        }

        _codecCtx->codec_id = encoder->id;
        _codecCtx->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _codecCtx->width = _config.UpWidth;
        _codecCtx->height = _config.UpHeight;
        _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

        var fps = _config.EncodeFps > 0 ? _config.EncodeFps : 25.0;
        var fpsDen = 1000;
        var fpsNum = (int)Math.Round(fps * fpsDen);
        _timeBase = new AVRational { num = 1, den = fpsDen };
        _codecCtx->time_base = _timeBase;
        _codecCtx->framerate = new AVRational { num = fpsNum, den = fpsDen };

        if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
        {
            _codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        }

        var preset = string.IsNullOrWhiteSpace(_config.Preset) ? "medium" : _config.Preset;
        ffmpeg.av_opt_set(_codecCtx->priv_data, "preset", preset, 0);
        ffmpeg.av_opt_set_int(_codecCtx->priv_data, "crf", _config.Crf, 0);
        ffmpeg.av_opt_set(_codecCtx->priv_data, "tune", "animation", 0);

        var openResult = ffmpeg.avcodec_open2(_codecCtx, encoder, null);
        if (openResult < 0)
        {
            ThrowFfmpeg("Unable to open codec.", openResult);
        }

        var paramResult = ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _codecCtx);
        if (paramResult < 0)
        {
            ThrowFfmpeg("Unable to copy codec parameters.", paramResult);
        }

        _stream->time_base = _codecCtx->time_base;

        OpenSourceContext();
        OpenSubtitleContext();
        CreateAuxiliaryOutputStreams();

        if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            var ioResult = ffmpeg.avio_open2(&_formatCtx->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE, null, null);
            if (ioResult < 0)
            {
                ThrowFfmpeg("Unable to open output file.", ioResult);
            }
        }

        var headerResult = ffmpeg.avformat_write_header(_formatCtx, null);
        if (headerResult < 0)
        {
            ThrowFfmpeg("Unable to write format header.", headerResult);
        }

        _headerWritten = true;

        _frame = ffmpeg.av_frame_alloc();
        if (_frame == null)
        {
            ThrowFfmpeg("Unable to allocate frame.", ffmpeg.AVERROR(12));
        }

        _frame->format = (int)_codecCtx->pix_fmt;
        _frame->width = _codecCtx->width;
        _frame->height = _codecCtx->height;

        var frameBufferResult = ffmpeg.av_frame_get_buffer(_frame, 32);
        if (frameBufferResult < 0)
        {
            ThrowFfmpeg("Unable to allocate frame buffer.", frameBufferResult);
        }

        _packet = ffmpeg.av_packet_alloc();
        if (_packet == null)
        {
            ThrowFfmpeg("Unable to allocate packet.", ffmpeg.AVERROR(12));
        }

        _sws = ffmpeg.sws_getContext(
            _config.UpWidth,
            _config.UpHeight,
            AVPixelFormat.AV_PIX_FMT_BGR24,
            _config.UpWidth,
            _config.UpHeight,
            _codecCtx->pix_fmt,
            2,
            null,
            null,
            null);

        if (_sws == null)
        {
            ThrowFfmpeg("Unable to create sws context.", ffmpeg.AVERROR(12));
        }

        _opened = true;
        return Task.CompletedTask;
    }

    public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        EnsureOpened();
        EnsureNoCancellation(cancellationToken);

        var writable = ffmpeg.av_frame_make_writable(_frame);
        if (writable < 0)
        {
            ThrowFfmpeg("Frame is not writable.", writable);
        }

        fixed (byte* srcPtr = frame.Span)
        {
            var srcData = new byte_ptrArray4();
            var srcLinesize = new int_array4();
            var dstData = new byte_ptrArray4();
            var dstLinesize = new int_array4();
            srcData[0] = srcPtr;
            srcLinesize[0] = _config.UpWidth * 3;
            dstData[0] = _frame->data[0];
            dstData[1] = _frame->data[1];
            dstData[2] = _frame->data[2];
            dstData[3] = _frame->data[3];
            dstLinesize[0] = _frame->linesize[0];
            dstLinesize[1] = _frame->linesize[1];
            dstLinesize[2] = _frame->linesize[2];
            dstLinesize[3] = _frame->linesize[3];

            ffmpeg.sws_scale(_sws, srcData, srcLinesize, 0, _config.UpHeight, dstData, dstLinesize);
        }

        _frame->pts = GetNextPts();

        var sendResult = ffmpeg.avcodec_send_frame(_codecCtx, _frame);
        if (sendResult < 0)
        {
            ThrowFfmpeg("Failed to send frame to encoder.", sendResult);
        }

        DrainPackets(cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask SubmitTimestampAsync(double timestampSeconds, CancellationToken cancellationToken)
    {
        _pendingTimestampSeconds = timestampSeconds;
        _hasPendingTimestamp = true;
        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        EnsureOpened();
        EnsureNoCancellation(cancellationToken);

        var sendResult = ffmpeg.avcodec_send_frame(_codecCtx, null);
        if (sendResult < 0 && sendResult != ffmpeg.AVERROR_EOF)
        {
            ThrowFfmpeg("Failed to flush encoder.", sendResult);
        }

        DrainPackets(cancellationToken);
        CopyAuxiliaryPackets(cancellationToken);
        return Task.CompletedTask;
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public void Dispose()
    {
        try
        {
            if (_headerWritten && _formatCtx != null)
            {
                ffmpeg.av_write_trailer(_formatCtx);
            }
        }
        catch
        {
        }

        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = packet;
        }

        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = frame;
        }

        if (_sws != null)
        {
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
        }

        if (_codecCtx != null)
        {
            var codecCtx = _codecCtx;
            ffmpeg.avcodec_free_context(&codecCtx);
            _codecCtx = codecCtx;
        }

        if (_formatCtx != null)
        {
            if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                var pb = _formatCtx->pb;
                ffmpeg.avio_closep(&pb);
                _formatCtx->pb = pb;
            }

            ffmpeg.avformat_free_context(_formatCtx);
            _formatCtx = null;
        }

        _headerWritten = false;

        CloseInputContext(ref _sourceCtx);
        CloseInputContext(ref _subtitleCtx);
    }

    private long GetNextPts()
    {
        if (_hasPendingTimestamp)
        {
            _hasPendingTimestamp = false;
            return (long)Math.Round(_pendingTimestampSeconds * _timeBase.den / _timeBase.num);
        }

        return _nextPts++;
    }

    private void DrainPackets(CancellationToken cancellationToken)
    {
        EnsureNoCancellation(cancellationToken);

        while (true)
        {
            var receiveResult = ffmpeg.avcodec_receive_packet(_codecCtx, _packet);
            if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
            {
                break;
            }

            if (receiveResult < 0)
            {
                ThrowFfmpeg("Failed to receive packet from encoder.", receiveResult);
            }

            ffmpeg.av_packet_rescale_ts(_packet, _codecCtx->time_base, _stream->time_base);
            _packet->stream_index = _stream->index;
            var writeResult = ffmpeg.av_write_frame(_formatCtx, _packet);
            if (writeResult < 0)
            {
                ThrowFfmpeg("Failed to write packet.", writeResult);
            }

            ffmpeg.av_packet_unref(_packet);
        }
    }

    private void OpenSourceContext()
    {
        if (string.IsNullOrWhiteSpace(_config.SourcePath))
        {
            return;
        }

        AVFormatContext* inputCtx = null;
        var openResult = ffmpeg.avformat_open_input(&inputCtx, _config.SourcePath, null, null);
        if (openResult < 0 || inputCtx == null)
        {
            ThrowFfmpeg("Unable to open source input.", openResult);
        }

        var infoResult = ffmpeg.avformat_find_stream_info(inputCtx, null);
        if (infoResult < 0)
        {
            ffmpeg.avformat_close_input(&inputCtx);
            ThrowFfmpeg("Unable to read source stream info.", infoResult);
        }

        _sourceCtx = inputCtx;
    }

    private void OpenSubtitleContext()
    {
        if (string.IsNullOrWhiteSpace(_config.SubtitlePath) || !_config.HasSubtitles)
        {
            return;
        }

        AVFormatContext* inputCtx = null;
        var openResult = ffmpeg.avformat_open_input(&inputCtx, _config.SubtitlePath, null, null);
        if (openResult < 0 || inputCtx == null)
        {
            return;
        }

        var infoResult = ffmpeg.avformat_find_stream_info(inputCtx, null);
        if (infoResult < 0)
        {
            ffmpeg.avformat_close_input(&inputCtx);
            return;
        }

        _subtitleCtx = inputCtx;
    }

    private void CreateAuxiliaryOutputStreams()
    {
        if (_sourceCtx != null)
        {
            _audioStreamIndex = ffmpeg.av_find_best_stream(_sourceCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (_audioStreamIndex >= 0)
            {
                _audioStream = CreateCopyStream(_sourceCtx->streams[_audioStreamIndex]);
            }
        }

        if (_subtitleCtx != null)
        {
            _subtitleStreamIndex = ffmpeg.av_find_best_stream(_subtitleCtx, AVMediaType.AVMEDIA_TYPE_SUBTITLE, -1, -1, null, 0);
            if (_subtitleStreamIndex >= 0)
            {
                _subtitleStream = CreateCopyStream(_subtitleCtx->streams[_subtitleStreamIndex]);
            }
        }
    }

    private AVStream* CreateCopyStream(AVStream* inputStream)
    {
        var outStream = ffmpeg.avformat_new_stream(_formatCtx, null);
        if (outStream == null)
        {
            ThrowFfmpeg("Unable to create auxiliary output stream.", ffmpeg.AVERROR_UNKNOWN);
        }

        var copyResult = ffmpeg.avcodec_parameters_copy(outStream->codecpar, inputStream->codecpar);
        if (copyResult < 0)
        {
            ThrowFfmpeg("Unable to copy stream parameters.", copyResult);
        }

        outStream->time_base = inputStream->time_base;
        return outStream;
    }

    private void CopyAuxiliaryPackets(CancellationToken cancellationToken)
    {
        CopyStreamPackets(_sourceCtx, _audioStream, _audioStreamIndex, cancellationToken);
        CopyStreamPackets(_subtitleCtx, _subtitleStream, _subtitleStreamIndex, cancellationToken);
    }

    private void CopyStreamPackets(AVFormatContext* inputCtx, AVStream* outputStream, int selectedIndex, CancellationToken cancellationToken)
    {
        if (inputCtx == null || outputStream == null || selectedIndex < 0)
        {
            return;
        }

        while (true)
        {
            EnsureNoCancellation(cancellationToken);
            var readResult = ffmpeg.av_read_frame(inputCtx, _packet);
            if (readResult == ffmpeg.AVERROR_EOF)
            {
                break;
            }

            if (readResult < 0)
            {
                ThrowFfmpeg("Unable to read auxiliary packet.", readResult);
            }

            if (_packet->stream_index != selectedIndex)
            {
                ffmpeg.av_packet_unref(_packet);
                continue;
            }

            ffmpeg.av_packet_rescale_ts(_packet, inputCtx->streams[selectedIndex]->time_base, outputStream->time_base);
            _packet->stream_index = outputStream->index;

            var writeResult = ffmpeg.av_write_frame(_formatCtx, _packet);
            if (writeResult < 0)
            {
                ffmpeg.av_packet_unref(_packet);
                ThrowFfmpeg("Unable to write auxiliary packet.", writeResult);
            }

            ffmpeg.av_packet_unref(_packet);
        }
    }

    private static void CloseInputContext(ref AVFormatContext* ctx)
    {
        if (ctx == null)
        {
            return;
        }

        var local = ctx;
        ffmpeg.avformat_close_input(&local);
        ctx = null;
    }

    private void EnsureOpened()
    {
        if (!_opened)
        {
            throw new InvalidOperationException("Encoder session is not open.");
        }
    }

    private static void EnsureNoCancellation(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private void ThrowFfmpeg(string message, int errorCode)
    {
        var detail = GetFfmpegError(errorCode);
        var text = string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}";
        _lastError = text;
        _onStderr?.Invoke(text);
        throw new InvalidOperationException(text);
    }

    private static string? GetFfmpegError(int error)
    {
        const int bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        return Marshal.PtrToStringUTF8((nint)buffer);
    }

    private static string? NormalizeOutputContainer(string? container)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            return null;
        }

        return container.Trim().ToLowerInvariant() switch
        {
            "mkv" => "matroska",
            _ => null
        };
    }
}
