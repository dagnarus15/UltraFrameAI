using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class PipelineServiceTests
{
    [Theory]
    [InlineData(1.25, 8.0, 10)]
    [InlineData(10.0, 25.0, 250)]
    [InlineData(0.0, 25.0, 0)]
    [InlineData(10.0, 0.0, 0)]
    public void EstimateFrameCount_ComputesExpectedCount(double duration, double fps, int expected)
    {
        Assert.Equal(expected, PipelineService.EstimateFrameCount(duration, fps));
    }

    [Theory]
    [InlineData("30000/1001", 29.970, 3)]
    [InlineData("25", 25.0, 0)]
    [InlineData("garbage", 0.0, 0)]
    public void ParseFraction_HandlesCommonInputs(string value, double expected, int precision)
    {
        var actual = PipelineService.ParseFraction(value);
        if (precision > 0)
        {
            Assert.InRange(actual, expected - Math.Pow(10, -precision), expected + Math.Pow(10, -precision));
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task RunAsync_EmptyQueue_ReportsIdleAndReturns()
    {
        var pipeline = new PipelineService();
        var reports = new List<PipelineProgress>();

        await pipeline.RunAsync(
            Array.Empty<QueueItemViewModel>(),
            CreateOptions(),
            reports.Add,
            CancellationToken.None);

        Assert.Single(reports);
        Assert.Equal(LocalizedStrings.LogIdle, reports[0].Stage);
        Assert.Equal(LocalizedStrings.LogNoItemsFound, reports[0].CurrentStatus);
    }

    [Fact]
    public async Task RunAsync_SkipRequestedItem_ReportsSkippedWithoutExternalTools()
    {
        var pipeline = new PipelineService();
        var tempRoot = TestSupport.CreateTempDirectory();
        try
        {
            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "episode.mkv",
                SourcePath = Path.Combine(tempRoot, "source.mkv"),
                OutputPath = Path.Combine(tempRoot, "output.mkv"),
                SkipRequested = true
            };
            var reports = new List<PipelineProgress>();

            await pipeline.RunAsync(
                new[] { item },
                CreateOptions(tempRoot),
                reports.Add,
                CancellationToken.None);

            Assert.Contains(reports, report => report.CurrentStatus == LocalizedStrings.LogSkippingEncode);
            Assert.True(item.SkipRequested);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task RunAsync_OutputAlreadyExists_ReportsSkipWithoutExternalTools()
    {
        var pipeline = new PipelineService();
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
            var outputPath = Path.Combine(tempRoot, "output.mkv");
            await TestSupport.CreateSyntheticVideoAsync(ffmpeg, sourcePath);
            await File.WriteAllTextAsync(outputPath, "existing");

            var item = new QueueItemViewModel
            {
                Index = 1,
                Title = "episode.mkv",
                SourcePath = sourcePath,
                OutputPath = outputPath
            };

            var reports = new List<PipelineProgress>();

            await pipeline.RunAsync(
                new[] { item },
                CreateOptions(tempRoot, overwrite: false, ffmpeg, ffprobe, upscaler, modelDir),
                reports.Add,
                CancellationToken.None);

            Assert.Contains(reports, report => report.CurrentStatus == LocalizedStrings.LogOutputExists);
            Assert.False(item.IsBusy);
        }
        finally
        {
            TestSupport.TryDeleteDirectory(tempRoot);
        }
    }

    private static PipelineOptions CreateOptions(
        string? tempRoot = null,
        bool overwrite = true,
        string? ffmpeg = null,
        string? ffprobe = null,
        string? upscaler = null,
        string? modelDir = null)
    {
        var root = tempRoot ?? TestSupport.CreateTempDirectory();
        return new PipelineOptions
        {
            RootFolder = root,
            OutputFolder = Path.Combine(root, "output"),
            Overwrite = overwrite,
            UseX265 = false,
            FfmpegThreads = 0,
            UpscalerThreads = "4:4:4",
            TileSize = 1024,
            GpuId = null,
            FfmpegPath = ffmpeg ?? "ffmpeg.exe",
            FfprobePath = ffprobe ?? "ffprobe.exe",
            UpscalerPath = upscaler ?? "realesrgan-ncnn-vulkan.exe",
            ModelDir = modelDir ?? Path.Combine(root, "models"),
            UseAntiFlicker = false,
            ContentMode = "Anime",
            AntiFlickerStrength = 65,
            EncoderPreset = "slower",
            UseNativeEncoderBackend = false
        };
    }
}
