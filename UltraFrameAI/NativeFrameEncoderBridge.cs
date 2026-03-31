namespace UltraFrameAI;

internal sealed class NativeFrameEncoderBridge : IFrameEncoderBridge
{
    private readonly ProcessFrameEncoderBridge _fallback = new();
    private static bool? _isAvailable;
    private static readonly object SyncRoot = new();

    public static bool IsAvailable()
    {
        lock (SyncRoot)
        {
            if (_isAvailable.HasValue)
            {
                return _isAvailable.Value;
            }

            _isAvailable = ProbeAvailability();
            return _isAvailable.Value;
        }
    }

    private static bool ProbeAvailability()
    {
        if (!FfmpegApiRuntime.TryInitialize(out var error))
        {
            _ = error;
            return false;
        }

        return true;
    }

    public string BuildEncoderArguments(
        int upWidth,
        int upHeight,
        double encodeFps,
        string sourcePath,
        string subtitlePath,
        bool hasSubtitles,
        string codec,
        string preset,
        int crf,
        string outputContainer,
        int height,
        string outputPath)
    {
        // The native backend seam exists, but the actual FFmpeg API implementation is still
        // a future step. If the native DLL is unavailable or not ready, keep subprocess mode.
        if (!IsAvailable())
        {
            return _fallback.BuildEncoderArguments(
                upWidth,
                upHeight,
                encodeFps,
                sourcePath,
                subtitlePath,
                hasSubtitles,
                codec,
                preset,
                crf,
                outputContainer,
                height,
                outputPath);
        }

        return _fallback.BuildEncoderArguments(
            upWidth,
            upHeight,
            encodeFps,
            sourcePath,
            subtitlePath,
            hasSubtitles,
            codec,
            preset,
            crf,
            outputContainer,
            height,
            outputPath);
    }

    public Stream CreateFrameInputStream() => _fallback.CreateFrameInputStream();

    public IFrameEncoderSession CreateSession(
        FrameEncoderSessionConfig config,
        CancellationToken cancellationToken,
        Action<string>? onStderr)
    {
        if (!IsAvailable()
            || !string.Equals(config.OutputContainer, "mkv", StringComparison.OrdinalIgnoreCase))
        {
            return _fallback.CreateSession(config, cancellationToken, onStderr);
        }

        return new FfmpegApiFrameEncoderSession(config, onStderr);
    }

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
            // Ignore probe cleanup failures.
        }
    }
}
