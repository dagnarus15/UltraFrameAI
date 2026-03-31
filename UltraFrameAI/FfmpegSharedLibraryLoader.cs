using System.Runtime.InteropServices;

namespace UltraFrameAI;

internal static class FfmpegSharedLibraryLoader
{
    private static readonly object SyncRoot = new();
    private static string? _loadedDirectory;

    public static bool TryLoadRequiredLibraries()
    {
        return TryLoadRequiredLibraries(out _);
    }

    public static bool TryLoadRequiredLibraries(out string? directory)
    {
        directory = null;

        if (!FfmpegSharedLibraryLocator.TryFindSharedLibraryDirectory(out var foundDirectory) || string.IsNullOrWhiteSpace(foundDirectory))
        {
            return false;
        }

        if (!FfmpegLicensePolicy.TryLoad(foundDirectory, out var policy) || !FfmpegLicensePolicy.IsStrictLlgplDynamic(policy))
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (string.Equals(_loadedDirectory, foundDirectory, StringComparison.OrdinalIgnoreCase))
            {
                directory = foundDirectory;
                return true;
            }

            if (!TryLoadRequiredLibraries(foundDirectory, NativeLibrary.Load, out _))
            {
                return false;
            }

            _loadedDirectory = foundDirectory;
            directory = foundDirectory;
            return true;
        }
    }

    internal static bool TryLoadRequiredLibraries(string directory, Func<string, nint> loadLibrary, out string? error)
    {
        error = null;

        if (!FfmpegLicensePolicy.TryLoad(directory, out var policy) || !FfmpegLicensePolicy.IsStrictLlgplDynamic(policy))
        {
            error = "FFmpeg policy manifest is missing or not LGPL-dynamic approved.";
            return false;
        }

        if (!FfmpegSharedLibraryLocator.TryGetRequiredLibraryPaths(directory, out var libraryPaths))
        {
            error = $"Missing FFmpeg shared libraries in '{directory}'.";
            return false;
        }

        foreach (var libraryPath in libraryPaths)
        {
            try
            {
                loadLibrary(libraryPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        return true;
    }
}
