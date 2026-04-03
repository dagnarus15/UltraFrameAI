using System.Globalization;
using UltraFrameAI;

namespace UltraFrameAI.Tests;

internal static class TestSupport
{
    public static string RepoRoot => FindRepoRoot();

    public static string ResolveExistingFile(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not find a required executable.", candidates.FirstOrDefault());
    }

    public static string ResolveExistingDirectory(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find a required directory.");
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UltraFrameAI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDeleteDirectory(string path)
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

    public static string PowerShellExe => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    public static async Task CreateSyntheticVideoAsync(string ffmpeg, string outputPath)
        => await CreateSyntheticVideoAsync(ffmpeg, outputPath, 64, 64, 8, 1.25).ConfigureAwait(false);

    public static async Task CreateSyntheticVideoAsync(
        string ffmpeg,
        string outputPath,
        int width,
        int height,
        double fps,
        double durationSeconds)
    {
        var args =
            $"-hide_banner -y -nostats -loglevel error " +
            $"-f lavfi -i testsrc2=size={width}x{height}:rate={fps.ToString(CultureInfo.InvariantCulture)}:duration={durationSeconds.ToString(CultureInfo.InvariantCulture)} " +
            $"-f lavfi -i sine=frequency=880:sample_rate=48000:duration={durationSeconds.ToString(CultureInfo.InvariantCulture)} " +
            $"-shortest -c:v libx264 -preset ultrafast -crf 35 -pix_fmt yuv420p -c:a aac -b:a 64k {Quote(outputPath)}";

        var exitCode = await ProcessRunner.RunAsync(
            ffmpeg,
            args,
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            null,
            null,
            CancellationToken.None);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create synthetic video. Exit code: {exitCode}");
        }
    }

    public static async Task<double> GetDurationAsync(string ffprobe, string path)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Quote(path)}",
            Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            CancellationToken.None);

        return lines.Count > 0 && double.TryParse(lines[0], CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "UltraFrameAI")) &&
                Directory.Exists(Path.Combine(current.FullName, "realesrgan-ncnn-vulkan-fork")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
