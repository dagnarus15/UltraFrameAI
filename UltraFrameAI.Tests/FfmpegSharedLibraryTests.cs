using System.Runtime.InteropServices;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class FfmpegSharedLibraryTests
{
    [Fact]
    public void TryFindSharedLibraryDirectory_FindsDirectoryWithAllRequiredDlls()
    {
        var directory = CreateTempDirectory();
        try
        {
            CreateRequiredDllPlaceholders(directory);
            CreateStrictPolicyManifest(directory);

            WithEnvironmentDirectory(directory, () =>
            {
                var found = FfmpegSharedLibraryLocator.TryFindSharedLibraryDirectory(out var foundDirectory);

                Assert.True(found);
                Assert.Equal(directory, foundDirectory);
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryFindSharedLibraryDirectory_ReturnsFalse_WhenLibrariesAreMissing()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "avcodec-61.dll"), "stub");
            File.WriteAllText(Path.Combine(directory, "avformat-61.dll"), "stub");
            File.WriteAllText(Path.Combine(directory, "avutil-59.dll"), "stub");
            File.WriteAllText(Path.Combine(directory, "swscale-8.dll"), "stub");

            var found = FfmpegSharedLibraryLocator.TryGetRequiredLibraryPaths(directory, out var paths);

            Assert.False(found);
            Assert.Empty(paths);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryGetRequiredLibraryPaths_ReturnsOrderedLibraryPaths()
    {
        var directory = CreateTempDirectory();
        try
        {
            CreateRequiredDllPlaceholders(directory);

            var found = FfmpegSharedLibraryLocator.TryGetRequiredLibraryPaths(directory, out var paths);

            Assert.True(found);
            Assert.Equal(5, paths.Count);
            Assert.All(paths, path => Assert.StartsWith(directory, path, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("avcodec-61.dll", Path.GetFileName(paths[0]));
            Assert.Equal("avformat-61.dll", Path.GetFileName(paths[1]));
            Assert.Equal("avutil-59.dll", Path.GetFileName(paths[2]));
            Assert.Equal("swscale-8.dll", Path.GetFileName(paths[3]));
            Assert.Equal("swresample-5.dll", Path.GetFileName(paths[4]));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryLoadRequiredLibraries_UsesLoaderForEachRequiredDll()
    {
        var directory = CreateTempDirectory();
        try
        {
            CreateRequiredDllPlaceholders(directory);
            CreateStrictPolicyManifest(directory);
            var loaded = new List<string>();

            var result = FfmpegSharedLibraryLoader.TryLoadRequiredLibraries(
                directory,
                path =>
                {
                    loaded.Add(path);
                    return new IntPtr(1);
                },
                out var error);

            Assert.True(result);
            Assert.Null(error);
            Assert.Equal(5, loaded.Count);
            Assert.Equal("avcodec-61.dll", Path.GetFileName(loaded[0]));
            Assert.Equal("avformat-61.dll", Path.GetFileName(loaded[1]));
            Assert.Equal("avutil-59.dll", Path.GetFileName(loaded[2]));
            Assert.Equal("swscale-8.dll", Path.GetFileName(loaded[3]));
            Assert.Equal("swresample-5.dll", Path.GetFileName(loaded[4]));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryLoadRequiredLibraries_ReturnsFalse_WhenPolicyIsNotStrictLlgplDynamic()
    {
        var directory = CreateTempDirectory();
        try
        {
            CreateRequiredDllPlaceholders(directory);
            CreatePolicyManifest(directory, license: "GPL", dynamicLinking: false, allowNativeEncoder: false);
            var loaded = new List<string>();

            var result = FfmpegSharedLibraryLoader.TryLoadRequiredLibraries(
                directory,
                path =>
                {
                    loaded.Add(path);
                    return new IntPtr(1);
                },
                out var error);

            Assert.False(result);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Empty(loaded);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static void WithEnvironmentDirectory(string directory, Action action)
    {
        const string environmentVariable = "ULTRAFRAMEAI_FFMPEG_DIR";
        var previous = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, directory);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, previous);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UltraFrameAI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CreateRequiredDllPlaceholders(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "avcodec-61.dll"), "stub");
        File.WriteAllText(Path.Combine(directory, "avformat-61.dll"), "stub");
        File.WriteAllText(Path.Combine(directory, "avutil-59.dll"), "stub");
        File.WriteAllText(Path.Combine(directory, "swscale-8.dll"), "stub");
        File.WriteAllText(Path.Combine(directory, "swresample-5.dll"), "stub");
    }

    private static void CreateStrictPolicyManifest(string directory)
    {
        CreatePolicyManifest(directory, license: "LGPL", dynamicLinking: true, allowNativeEncoder: true);
    }

    private static void CreatePolicyManifest(string directory, string license, bool dynamicLinking, bool allowNativeEncoder)
    {
        var json = $$"""
        {
          "license": "{{license}}",
          "dynamicLinking": {{dynamicLinking.ToString().ToLowerInvariant()}},
          "allowNativeEncoder": {{allowNativeEncoder.ToString().ToLowerInvariant()}}
        }
        """;

        File.WriteAllText(Path.Combine(directory, "UltraFrameAI.ffmpeg.policy.json"), json);
    }

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
