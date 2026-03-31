namespace UltraFrameAI;

internal static class FfmpegSharedLibraryLocator
{
    internal static readonly string[] RequiredLibraryPrefixes =
    [
        "avcodec-",
        "avformat-",
        "avutil-",
        "swscale-",
        "swresample-"
    ];

    private static readonly string[] SearchDirectoryEnvironmentVariables =
    [
        "ULTRAFRAMEAI_FFMPEG_DIR",
        "FFMPEG_DIR",
        "FFMPEG_ROOT"
    ];

    private static readonly string[] CommonInstallDirectories =
    [
        @"C:\ffmpeg\bin",
        @"C:\ffmpeg",
        @"C:\Program Files\ffmpeg\bin",
        @"C:\Program Files\FFmpeg\bin",
        @"C:\tools\ffmpeg\bin"
    ];

    public static bool HasRequiredLibraries() => TryFindSharedLibraryDirectory(out _);

    public static bool TryFindSharedLibraryDirectory(out string? directory)
    {
        foreach (var candidate in GetSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            if (HasAllRequiredLibraries(candidate))
            {
                directory = candidate;
                return true;
            }
        }

        directory = null;
        return false;
    }

    public static bool TryGetRequiredLibraryPaths(string directory, out IReadOnlyList<string> libraryPaths)
    {
        var paths = new List<string>(RequiredLibraryPrefixes.Length);

        foreach (var prefix in RequiredLibraryPrefixes)
        {
            var path = FindLibraryPath(directory, prefix);
            if (path is null)
            {
                libraryPaths = Array.Empty<string>();
                return false;
            }

            paths.Add(path);
        }

        libraryPaths = paths;
        return true;
    }

    private static bool HasAllRequiredLibraries(string directory)
    {
        try
        {
            return RequiredLibraryPrefixes.All(prefix => FindLibraryPath(directory, prefix) is not null);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        foreach (var environmentVariable in SearchDirectoryEnvironmentVariables)
        {
            foreach (var candidate in ExpandDirectoryCandidates(Environment.GetEnvironmentVariable(environmentVariable)))
            {
                yield return candidate;
            }
        }

        var current = AppContext.BaseDirectory;
        yield return current;
        yield return Directory.GetCurrentDirectory();

        var repoRoot = FindRepoRoot();
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            yield return Path.Combine(repoRoot, "dist", "UltraFrameAI");
            yield return Path.Combine(repoRoot, "UltraFrameAI");
        }

        foreach (var installDirectory in CommonInstallDirectories)
        {
            yield return installDirectory;
        }
    }

    private static IEnumerable<string> ExpandDirectoryCandidates(string? rawDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawDirectory))
        {
            yield break;
        }

        var directory = rawDirectory.Trim().Trim('"');
        yield return directory;
        yield return Path.Combine(directory, "bin");
    }

    private static string? FindLibraryPath(string directory, string prefix)
    {
        try
        {
            return Directory.EnumerateFiles(directory, $"{prefix}*.dll", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
