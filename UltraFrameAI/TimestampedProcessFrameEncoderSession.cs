using System.Globalization;
using System.Text;

namespace UltraFrameAI;

internal sealed class TimestampedProcessFrameEncoderSession : IFrameEncoderSession
{
    private readonly FrameEncoderSessionConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<string>? _onStderr;
    private readonly string _stageDirectory;
    private readonly string _stageVideoPath;
    private FfmpegApiFrameEncoderSession? _timestampMuxSession;
    private ProcessFrameEncoderSession? _finalEncodeSession;
    private string? _lastError;
    private bool _opened;
    private bool _flushed;
    private static readonly bool KeepStageFiles =
        string.Equals(
            Environment.GetEnvironmentVariable("ULTRAFRAMEAI_KEEP_STAGE_FILES"),
            "1",
            StringComparison.Ordinal);

    public TimestampedProcessFrameEncoderSession(
        FrameEncoderSessionConfig config,
        CancellationToken cancellationToken,
        Action<string>? onStderr)
    {
        _config = config;
        _cancellationToken = cancellationToken;
        _onStderr = onStderr;
        _stageDirectory = Path.Combine(
            Path.GetTempPath(),
            "UltraFrameAI",
            "encode-stage",
            Guid.NewGuid().ToString("N"));
        _stageVideoPath = Path.Combine(_stageDirectory, "timestamped-stage.mkv");
    }

    public int ExitCode => _finalEncodeSession?.ExitCode ?? (_opened ? 0 : -1);

    public string? LastError => _finalEncodeSession?.LastError ?? _lastError;

    public bool SupportsPerFrameTimestamps => true;

    public bool IsAlive => _timestampMuxSession?.IsAlive ?? _finalEncodeSession?.IsAlive ?? true;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_opened)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_stageDirectory);

        var stageConfig = new FrameEncoderSessionConfig(
            _config.UpWidth,
            _config.UpHeight,
            _config.EncodeFps,
            _config.SourcePath,
            _config.SubtitlePath,
            _config.HasSubtitles,
            "ffv1",
            "medium",
            0,
            "mkv",
            _config.Height,
            _stageVideoPath,
            _config.FfmpegPath);

        _timestampMuxSession = new FfmpegApiFrameEncoderSession(stageConfig, line =>
        {
            _lastError = line;
            _onStderr?.Invoke(line);
        });
        _opened = true;
        return _timestampMuxSession.OpenAsync(cancellationToken);
    }

    public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        EnsureOpened();
        return _timestampMuxSession!.WriteFrameAsync(frame, cancellationToken);
    }

    public ValueTask SubmitTimestampAsync(double timestampSeconds, CancellationToken cancellationToken)
    {
        EnsureOpened();
        return _timestampMuxSession!.SubmitTimestampAsync(timestampSeconds, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        EnsureOpened();
        if (_flushed)
        {
            return;
        }

        await _timestampMuxSession!.FlushAsync(cancellationToken).ConfigureAwait(false);
        _timestampMuxSession.Dispose();
        _timestampMuxSession = null;

        var finalArgs = BuildFinalEncodeArguments();
        var workingDirectory = Path.GetDirectoryName(_config.OutputPath) ?? Environment.CurrentDirectory;
        _finalEncodeSession = new ProcessFrameEncoderSession(
            _config.FfmpegPath,
            finalArgs,
            workingDirectory,
            _cancellationToken,
            line =>
            {
                _lastError = line;
                _onStderr?.Invoke(line);
            });
        await _finalEncodeSession.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _finalEncodeSession.FlushAsync(cancellationToken).ConfigureAwait(false);
        _flushed = true;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        if (_finalEncodeSession is not null)
        {
            await _finalEncodeSession.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void SetPaused(bool paused)
    {
        _finalEncodeSession?.SetPaused(paused);
    }

    public void Abort()
    {
        try
        {
            _finalEncodeSession?.Abort();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            _timestampMuxSession?.Dispose();
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

        if (!KeepStageFiles)
        {
            TryDelete(_stageVideoPath);
            TryDeleteDirectory(_stageDirectory);
        }
        _timestampMuxSession = null;
        _finalEncodeSession = null;
    }

    private string BuildFinalEncodeArguments()
    {
        var args = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
        args.Append($"-i {Q(_stageVideoPath)} ");
        args.Append($"-map 0:v:0 -map 0:a? -fps_mode passthrough -enc_time_base -1 -vf {Q($"scale=-2:{_config.Height}:flags=lanczos,setsar=1")} ");
        args.Append($"-c:v {_config.Codec} -preset {_config.Preset} -tune animation -crf {_config.Crf} -pix_fmt yuv420p ");
        args.Append("-c:a copy ");
        if (_config.HasSubtitles)
        {
            args.Append("-map 0:s? -c:s copy ");
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
        if (!_opened || _timestampMuxSession is null)
        {
            throw new InvalidOperationException("Timestamped encoder session is not open.");
        }
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
