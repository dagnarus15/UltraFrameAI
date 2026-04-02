using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

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
                FfmpegThreads = 1,
                UpscalerThreads = "1:1:1",
                TileSize = -1,
                GpuId = null,
                FfmpegPath = ffmpeg,
                FfprobePath = ffprobe,
                UpscalerPath = upscaler,
                ModelDir = modelDir,
                UseAntiFlicker = true,
                AntiFlickerMode = AntiFlickerMode.LumaStabilizer,
                ContentMode = "Anime",
                AntiFlickerStrength = 65,
                EncoderPreset = "slower",
                PreserveIncompleteOutput = false,
                UseNativeEncoderBackend = false,
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
}
