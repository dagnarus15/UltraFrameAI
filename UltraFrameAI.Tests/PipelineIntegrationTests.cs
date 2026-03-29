using System.Globalization;
using System.Diagnostics;
using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class PipelineIntegrationTests
{
    [Fact(Timeout = 240_000)]
    public async Task PipePipeline_Processes_SmallSyntheticVideo_EndToEnd()
    {
        LocalizedStrings.SetLanguage(UiLanguage.English);

        var repoRoot = FindRepoRoot();
        var ffmpeg = ResolveExistingFile(
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(repoRoot, @"dist\UltraFrameAI\ffmpeg.exe"));
        var ffprobe = ResolveExistingFile(
            @"C:\ffmpeg\bin\ffprobe.exe",
            Path.Combine(repoRoot, @"dist\UltraFrameAI\ffprobe.exe"));
        var upscaler = ResolveExistingFile(
            Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe"),
            Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\realesrgan-ncnn-vulkan.exe"));
        var modelDir = ResolveExistingDirectory(
            Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-20220424\models"),
            Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\models"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.mkv");
            var outputDir = Path.Combine(tempRoot, "output");
            var workDir = Path.Combine(tempRoot, "_work_source");
            Directory.CreateDirectory(outputDir);

            await CreateSyntheticVideoAsync(ffmpeg, sourcePath);

            var outputPath = Path.Combine(outputDir, "source_1080p_x264.mkv");
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "source.mkv",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                WorkPath = workDir
            };

            var reports = new List<PipelineProgress>();
            var pipeline = new PipelineService();
            var options = new PipelineOptions
            {
                RootFolder = tempRoot,
                OutputFolder = outputDir,
                Overwrite = true,
                KeepTemp = false,
                UseX265 = false,
                FfmpegThreads = 1,
                UpscalerThreads = "1:1:1",
                TileSize = -1,
                GpuId = null,
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                UpscalerPath = upscaler,
                ModelDir = modelDir,
                UsePipeMode = true
            };

            await pipeline.RunAsync(new[] { item }, options, reports.Add, CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
            Assert.Contains(reports, r => r.Stage == LocalizedStrings.LogBatchComplete);
            Assert.Contains(reports, r => r.Progress >= 99.9);

            var duration = await GetDurationAsync(ffprobe, outputPath);
            Assert.InRange(duration, 0.5, 10.0);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task CreateSyntheticVideoAsync(string ffmpeg, string outputPath)
    {
        var args =
            $"-hide_banner -y -nostats -loglevel error " +
            $"-f lavfi -i testsrc2=size=64x64:rate=8:duration=1.25 " +
            $"-f lavfi -i sine=frequency=880:sample_rate=48000:duration=1.25 " +
            $"-shortest -c:v libx264 -preset ultrafast -crf 35 -pix_fmt yuv420p -c:a aac -b:a 64k {Quote(outputPath)}";

        var exitCode = await ProcessRunner.RunAsync(
            ffmpeg,
            args,
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    private static async Task<double> GetDurationAsync(string ffprobe, string path)
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

    private static string ResolveExistingFile(params string[] candidates)
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

    private static string ResolveExistingDirectory(params string[] candidates)
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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UltraFrameAI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
