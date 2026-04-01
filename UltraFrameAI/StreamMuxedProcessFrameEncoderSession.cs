using System.Text;

namespace UltraFrameAI;

internal sealed class StreamMuxedProcessFrameEncoderSession : IFrameEncoderSession
{
    private readonly FrameEncoderSessionConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<string>? _onStderr;
    private ProcessFrameEncoderSession? _finalEncodeSession;
    private FfmpegApiFrameEncoderSession? _muxSession;
    private string? _lastError;
    private bool _opened;
    private bool _flushed;

    public StreamMuxedProcessFrameEncoderSession(
        FrameEncoderSessionConfig config,
        CancellationToken cancellationToken,
        Action<string>? onStderr)
    {
        _config = config;
        _cancellationToken = cancellationToken;
        _onStderr = onStderr;
    }

    public int ExitCode => _finalEncodeSession?.ExitCode ?? (_opened ? 0 : -1);

    public string? LastError => _finalEncodeSession?.LastError ?? _lastError;

    public bool SupportsPerFrameTimestamps => true;

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_opened)
        {
            return;
        }

        var workingDirectory = Path.GetDirectoryName(_config.OutputPath) ?? Environment.CurrentDirectory;
        _finalEncodeSession = new ProcessFrameEncoderSession(
            _config.FfmpegPath,
            BuildFinalEncodeArguments(),
            workingDirectory,
            _cancellationToken,
            line =>
            {
                _lastError = line;
                _onStderr?.Invoke(line);
            });
        await _finalEncodeSession.OpenAsync(cancellationToken).ConfigureAwait(false);

        var muxConfig = new FrameEncoderSessionConfig(
            _config.UpWidth,
            _config.UpHeight,
            _config.EncodeFps,
            string.Empty,
            string.Empty,
            false,
            "ffv1",
            "medium",
            0,
            "nut",
            _config.UpHeight,
            "pipe:0",
            _config.FfmpegPath);

        _muxSession = new FfmpegApiFrameEncoderSession(
            muxConfig,
            line =>
            {
                _lastError = line;
                _onStderr?.Invoke(line);
            },
            _finalEncodeSession.InputStream);
        await _muxSession.OpenAsync(cancellationToken).ConfigureAwait(false);

        _opened = true;
    }

    public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        EnsureOpened();
        return _muxSession!.WriteFrameAsync(frame, cancellationToken);
    }

    public ValueTask SubmitTimestampAsync(double timestampSeconds, CancellationToken cancellationToken)
    {
        EnsureOpened();
        return _muxSession!.SubmitTimestampAsync(timestampSeconds, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        EnsureOpened();
        if (_flushed)
        {
            return;
        }

        await _muxSession!.FlushAsync(cancellationToken).ConfigureAwait(false);
        _muxSession.Dispose();
        _muxSession = null;
        _flushed = true;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        if (_finalEncodeSession is not null)
        {
            await _finalEncodeSession.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        try
        {
            _muxSession?.Dispose();
        }
        catch
        {
        }

        try
        {
            _finalEncodeSession?.Dispose();
        }
        catch
        {
        }

        _muxSession = null;
        _finalEncodeSession = null;
    }

    private string BuildFinalEncodeArguments()
    {
        var args = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
        args.Append("-f nut -i pipe:0 ");
        args.Append($"-i {Q(_config.SourcePath)} ");
        if (_config.HasSubtitles)
        {
            args.Append($"-i {Q(_config.SubtitlePath)} ");
        }

        args.Append($"-map 0:v:0 -map 1:a? -fps_mode passthrough -enc_time_base -1 -vf {Q($"scale=-2:{_config.Height}:flags=lanczos,setsar=1")} ");
        args.Append($"-c:v {_config.Codec} -preset {_config.Preset} -tune animation -crf {_config.Crf} -pix_fmt yuv420p ");
        args.Append("-c:a copy ");
        if (_config.HasSubtitles)
        {
            args.Append("-map 2:s? -c:s copy ");
        }
        else
        {
            args.Append("-sn ");
        }

        args.Append(Q(_config.OutputPath));
        return args.ToString();
    }

    private void EnsureOpened()
    {
        if (!_opened || _muxSession is null)
        {
            throw new InvalidOperationException("Stream-muxed encoder session is not open.");
        }
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
