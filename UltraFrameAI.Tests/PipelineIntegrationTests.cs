using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;
using System.Text.Json;

namespace UltraFrameAI.Tests;

public sealed class PipelineIntegrationTests
{
    [Fact(Timeout = 240_000)]
    public async Task PipePipeline_Processes_SmallSyntheticVideo_EndToEnd()
    {
        var originalLanguage = LocalizedStrings.CurrentLanguage;
        LocalizedStrings.SetLanguage(UiLanguage.English);

        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var repoRoot = TestSupport.RepoRoot;
            var ffmpeg = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffmpeg.exe"));
            var ffprobe = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffprobe.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffprobe.exe"));
            var upscaler = TestSupport.ResolveExistingFile(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\realesrgan-ncnn-vulkan.exe"));
            var modelDir = TestSupport.ResolveExistingDirectory(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-20220424\models"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\models"));

            var sourcePath = Path.Combine(tempRoot, "source.mkv");
            var outputDir = Path.Combine(tempRoot, "output");
            Directory.CreateDirectory(outputDir);

            await TestSupport.CreateSyntheticVideoAsync(ffmpeg, sourcePath);

            var outputPath = Path.Combine(outputDir, "source_1080p_x264.mkv");
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "source.mkv",
                SourcePath = sourcePath,
                OutputPath = outputPath
            };

            var reports = new List<PipelineProgress>();
            var pipeline = new PipelineService();
            var options = new PipelineOptions
            {
                RootFolder = tempRoot,
                OutputFolder = outputDir,
                Overwrite = true,
                UseX265 = false,
                TargetHeight = 1080,
                FfmpegThreads = 1,
                UpscalerThreads = "1:1:1",
                TileSize = -1,
                GpuId = null,
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                UpscalerBackend = UpscalerBackendKind.RealEsrgan,
                UpscalerPath = upscaler,
                UpscalerWorkingDirectory = Path.GetDirectoryName(upscaler) ?? tempRoot,
                ModelDir = modelDir,
                ExternalUpscalerArgumentsTemplate = string.Empty,
                EncoderPreset = "slower",
                PreserveIncompleteOutput = false,
                RepairBrokenTimestamps = false
            };

            await pipeline.RunAsync(new[] { item }, options, reports.Add, CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
            Assert.Contains(reports, r => r.Stage == LocalizedStrings.LogBatchComplete);
            Assert.Contains(reports, r => r.Progress >= 99.9);

            var duration = await TestSupport.GetDurationAsync(ffprobe, outputPath);
            Assert.InRange(duration, 0.5, 10.0);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
            LocalizedStrings.SetLanguage(originalLanguage);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task PipePipeline_CanResume_FromExistingPartialOutput()
    {
        var originalLanguage = LocalizedStrings.CurrentLanguage;
        LocalizedStrings.SetLanguage(UiLanguage.English);

        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var repoRoot = TestSupport.RepoRoot;
            var ffmpeg = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffmpeg.exe"));
            var ffprobe = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffprobe.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffprobe.exe"));
            var upscaler = TestSupport.ResolveExistingFile(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\realesrgan-ncnn-vulkan.exe"));
            var modelDir = TestSupport.ResolveExistingDirectory(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-20220424\models"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\models"));

            var sourcePath = Path.Combine(tempRoot, "source.mkv");
            var outputDir = Path.Combine(tempRoot, "output");
            Directory.CreateDirectory(outputDir);

            await TestSupport.CreateSyntheticVideoAsync(ffmpeg, sourcePath, 128, 128, 8, 10.0);

            var outputPath = Path.Combine(outputDir, "source.mkv");
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "source.mkv",
                SourcePath = sourcePath,
                OutputPath = outputPath
            };

            await CreatePartialOutputAsync(ffmpeg, sourcePath, outputPath, 3.0);
            item.ResumeRequested = true;
            item.ResumeProcessedFrames = 24;

            var pipeline = new PipelineService();
            var options = new PipelineOptions
            {
                RootFolder = tempRoot,
                OutputFolder = outputDir,
                Overwrite = true,
                UseX265 = false,
                TargetHeight = 1080,
                OutputContainer = "mkv",
                FfmpegThreads = 1,
                UpscalerThreads = "1:1:1",
                TileSize = -1,
                GpuId = null,
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                UpscalerBackend = UpscalerBackendKind.RealEsrgan,
                UpscalerPath = upscaler,
                UpscalerWorkingDirectory = Path.GetDirectoryName(upscaler) ?? tempRoot,
                ModelDir = modelDir,
                ExternalUpscalerArgumentsTemplate = string.Empty,
                RefinerBackend = RefinerBackendKind.None,
                RefinerPath = string.Empty,
                RefinerWorkingDirectory = string.Empty,
                RefinerModelDir = string.Empty,
                RefinerArgumentsTemplate = string.Empty,
                EncoderPreset = "slower",
                PreserveIncompleteOutput = true,
                RepairBrokenTimestamps = false
            };

            Assert.True(File.Exists(outputPath));
            var resumeStatePath = PipelineService.GetResumeStatePath(outputPath);
            await File.WriteAllTextAsync(
                resumeStatePath,
                JsonSerializer.Serialize(new
                {
                    Version = 1,
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Codec = "libx264",
                    TargetHeight = 1080,
                    UpscalerBackend = UpscalerBackendKind.RealEsrgan.ToString(),
                    RefinerBackend = RefinerBackendKind.None.ToString(),
                    ProcessedFrames = item.ResumeProcessedFrames,
                    TotalFrames = 80,
                    Stage = "Cancelled",
                    Complete = false,
                    CanResume = true,
                    UpdatedUtc = DateTime.UtcNow
                }));

            var reports = new List<PipelineProgress>();
            await pipeline.RunAsync(new[] { item }, options, reports.Add, CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
            Assert.Contains(reports, r => r.Stage == LocalizedStrings.LogBatchComplete);

            var duration = await TestSupport.GetDurationAsync(ffprobe, outputPath);
            Assert.InRange(duration, 2.0, 12.0);

            Assert.False(File.Exists(resumeStatePath));
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
            LocalizedStrings.SetLanguage(originalLanguage);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task PipePipeline_ResumeRequested_DoesNotSkip_WhenOutputAlreadyExists_AndOverwriteDisabled()
    {
        var originalLanguage = LocalizedStrings.CurrentLanguage;
        LocalizedStrings.SetLanguage(UiLanguage.English);

        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var repoRoot = TestSupport.RepoRoot;
            var ffmpeg = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffmpeg.exe"));
            var ffprobe = TestSupport.ResolveExistingFile(
                @"C:\ffmpeg\bin\ffprobe.exe",
                Path.Combine(repoRoot, @"dist\UltraFrameAI\ffprobe.exe"));
            var upscaler = TestSupport.ResolveExistingFile(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\realesrgan-ncnn-vulkan.exe"));
            var modelDir = TestSupport.ResolveExistingDirectory(
                Path.Combine(repoRoot, @"realesrgan-ncnn-vulkan-20220424\models"),
                Path.Combine(repoRoot, @"dist\UltraFrameAI\realesrgan-ncnn-vulkan-20220424\models"));

            var sourcePath = Path.Combine(tempRoot, "source.mkv");
            var outputDir = Path.Combine(tempRoot, "output");
            Directory.CreateDirectory(outputDir);

            await TestSupport.CreateSyntheticVideoAsync(ffmpeg, sourcePath, 128, 128, 8, 10.0);

            var outputPath = Path.Combine(outputDir, "source.mkv");
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "source.mkv",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                ResumeRequested = true,
                ResumeProcessedFrames = 24
            };

            await CreatePartialOutputAsync(ffmpeg, sourcePath, outputPath, 3.0);
            var resumeStatePath = PipelineService.GetResumeStatePath(outputPath);
            await File.WriteAllTextAsync(
                resumeStatePath,
                JsonSerializer.Serialize(new
                {
                    Version = 1,
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Codec = "libx264",
                    TargetHeight = 1080,
                    UpscalerBackend = UpscalerBackendKind.RealEsrgan.ToString(),
                    RefinerBackend = RefinerBackendKind.None.ToString(),
                    ProcessedFrames = item.ResumeProcessedFrames,
                    TotalFrames = 80,
                    Stage = "Cancelled",
                    Complete = false,
                    CanResume = true,
                    UpdatedUtc = DateTime.UtcNow
                }));

            var pipeline = new PipelineService();
            var options = new PipelineOptions
            {
                RootFolder = tempRoot,
                OutputFolder = outputDir,
                Overwrite = false,
                UseX265 = false,
                TargetHeight = 1080,
                OutputContainer = "mkv",
                FfmpegThreads = 1,
                UpscalerThreads = "1:1:1",
                TileSize = -1,
                GpuId = null,
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                UpscalerBackend = UpscalerBackendKind.RealEsrgan,
                UpscalerPath = upscaler,
                UpscalerWorkingDirectory = Path.GetDirectoryName(upscaler) ?? tempRoot,
                ModelDir = modelDir,
                ExternalUpscalerArgumentsTemplate = string.Empty,
                RefinerBackend = RefinerBackendKind.None,
                RefinerPath = string.Empty,
                RefinerWorkingDirectory = string.Empty,
                RefinerModelDir = string.Empty,
                RefinerArgumentsTemplate = string.Empty,
                EncoderPreset = "slower",
                PreserveIncompleteOutput = true,
                RepairBrokenTimestamps = false
            };

            var reports = new List<PipelineProgress>();
            await pipeline.RunAsync(new[] { item }, options, reports.Add, CancellationToken.None);

            Assert.Contains(reports, r => r.Stage == LocalizedStrings.LogPreparing);
            Assert.DoesNotContain(reports, r => r.CurrentStatus == LocalizedStrings.LogOutputExists);
            Assert.Contains(reports, r =>
                r.Stage == LocalizedStrings.LogPreparing &&
                r.CurrentStatus == LocalizedStrings.Get("ResumePreflightLoadingFile") &&
                r.CurrentDetail == LocalizedStrings.Get("ResumePreflightLoadingFile") &&
                r.ProcessingFpsText == "--");
            Assert.Contains(reports, r => r.Stage == LocalizedStrings.LogBatchComplete);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
            LocalizedStrings.SetLanguage(originalLanguage);
        }
    }

    private static async Task CreatePartialOutputAsync(string ffmpegPath, string sourcePath, string outputPath, double durationSeconds)
    {
        var args =
            $"-hide_banner -y -nostats -loglevel error -t {durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{sourcePath}\" -c copy \"{outputPath}\"";

        var exitCode = await ProcessRunner.RunAsync(
            ffmpegPath,
            args,
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            null,
            null,
            CancellationToken.None);

        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to create partial output for resume integration test.");
        }
    }
}
