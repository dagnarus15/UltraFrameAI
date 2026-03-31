using FFmpeg.AutoGen;

namespace UltraFrameAI;

internal static class FfmpegApiRuntime
{
    private static readonly object SyncRoot = new();
    private static bool? _isInitialized;
    private static string? _lastError;

    public static bool TryInitialize(out string? error)
    {
        lock (SyncRoot)
        {
            if (_isInitialized.HasValue)
            {
                error = _lastError;
                return _isInitialized.Value;
            }

            var initialized = InitializeCore(out error);
            _isInitialized = initialized;
            _lastError = error;
            return initialized;
        }
    }

    public static bool TryInitialize() => TryInitialize(out _);

    private static bool InitializeCore(out string? error)
    {
        error = null;

        if (!FfmpegSharedLibraryLoader.TryLoadRequiredLibraries(out var directory) || string.IsNullOrWhiteSpace(directory))
        {
            error = "FFmpeg shared libraries or policy manifest are not available.";
            return false;
        }

        try
        {
            ffmpeg.RootPath = directory;
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            var version = ffmpeg.av_version_info();
            if (version is null)
            {
                error = "FFmpeg API version query failed.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
