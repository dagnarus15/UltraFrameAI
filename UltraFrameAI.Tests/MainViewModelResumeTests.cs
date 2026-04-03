using System.Reflection;
using System.Text.Json;
using System.Windows;
using UltraFrameAI;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class MainViewModelResumeTests
{
    [Fact]
    public async Task PrepareRunListAsync_ResumeDecision_MarksItemForResume()
    {
        await RunOnStaThreadAsync(async () =>
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

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

                await CreatePartialOutputAsync(ffmpeg, sourcePath, outputPath);
                await File.WriteAllTextAsync(
                    PipelineService.GetResumeStatePath(outputPath),
                    JsonSerializer.Serialize(new
                    {
                        Version = 1,
                        SourcePath = sourcePath,
                        OutputPath = outputPath,
                        Codec = "libx264",
                        TargetHeight = 1080,
                        UpscalerBackend = UpscalerBackendKind.RealEsrgan.ToString(),
                        RefinerBackend = RefinerBackendKind.None.ToString(),
                        ProcessedFrames = 0,
                        TotalFrames = 80,
                        Stage = "Preparing",
                        Complete = false,
                        CanResume = true,
                        UpdatedUtc = DateTime.UtcNow
                    }));

                Assert.True(File.Exists(outputPath));

                var viewModel = new MainViewModel
                {
                    RootFolder = tempRoot,
                    OutputFolder = outputDir,
                    Overwrite = false,
                    SelectedCodec = "x264",
                    SelectedTarget = "1080p",
                    SelectedContainer = "mkv",
                    EncoderPreset = "slower",
                    FfmpegThreadsText = "1",
                    UpscalerThreadsText = "1:1:1",
                    TileSizeText = "-1",
                    UseAntiFlicker = false
                };

                viewModel.OutputConflictRequested += _ => Task.FromResult(OutputConflictDecision.Resume);
                viewModel.Items.Add(item);
                AttachQueueItem(viewModel, item);

                var prepared = await PrepareRunListAsync(viewModel, new[] { item }, BuildOptions(viewModel), CancellationToken.None);

                Assert.Single(prepared);
                Assert.True(item.ResumeRequested);
                Assert.True(item.ResumeProcessedFrames > 0);
            }
            finally
            {
                TestSupport.TryDeleteDirectory(tempRoot);
            }
        });
    }

    [Fact]
    public async Task TryGetResumeInfo_InvalidPartialOutput_ReturnsFalse()
    {
        await RunOnStaThreadAsync(async () =>
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            var tempRoot = TestSupport.CreateTempDirectory();
            try
            {
                var sourcePath = Path.Combine(tempRoot, "source.mkv");
                var outputPath = Path.Combine(tempRoot, "output.mkv");
                await File.WriteAllTextAsync(sourcePath, "source");
                await File.WriteAllTextAsync(outputPath, "not-a-real-video");

                var viewModel = new MainViewModel(persistUserState: false);
                var item = new QueueItemViewModel
                {
                    Index = 1,
                    Title = "source.mkv",
                    SourcePath = sourcePath,
                    OutputPath = outputPath
                };

                var method = typeof(MainViewModel).GetMethod("TryGetResumeInfo", BindingFlags.Instance | BindingFlags.NonPublic);
                if (method is null)
                {
                    throw new MissingMethodException(nameof(MainViewModel), "TryGetResumeInfo");
                }

                var args = new object[] { item, BuildOptions(viewModel), 0 };
                var result = (bool)(method.Invoke(viewModel, args) ?? false);

                Assert.False(result);
            }
            finally
            {
                TestSupport.TryDeleteDirectory(tempRoot);
            }
        });
    }

    [Fact]
    public async Task PrepareRunListAsync_RecoverableBrokenOutput_UsesRecoveredResumeSource()
    {
        await RunOnStaThreadAsync(async () =>
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            var tempRoot = TestSupport.CreateTempDirectory();
            try
            {
                var repoRoot = TestSupport.RepoRoot;
                var ffmpeg = TestSupport.ResolveExistingFile(
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    Path.Combine(repoRoot, @"dist\UltraFrameAI\ffmpeg.exe"));

                var sourcePath = Path.Combine(tempRoot, "source.mkv");
                var outputDir = Path.Combine(tempRoot, "output");
                Directory.CreateDirectory(outputDir);
                await TestSupport.CreateSyntheticVideoAsync(ffmpeg, sourcePath, 128, 128, 8, 10.0);

                var goodPartialPath = Path.Combine(outputDir, "good-partial.mkv");
                var brokenOutputPath = Path.Combine(outputDir, "source.mkv");
                var item = new QueueItemViewModel
                {
                    Index = 1,
                    Title = "source.mkv",
                    SourcePath = sourcePath,
                    OutputPath = brokenOutputPath
                };

                await CreatePartialOutputAsync(ffmpeg, sourcePath, goodPartialPath);
                File.Copy(goodPartialPath, brokenOutputPath, true);
                using (var stream = new FileStream(brokenOutputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.SetLength(Math.Max(1024, stream.Length / 2));
                }

                await File.WriteAllTextAsync(
                    PipelineService.GetResumeStatePath(brokenOutputPath),
                    JsonSerializer.Serialize(new
                    {
                        Version = 1,
                        SourcePath = sourcePath,
                        OutputPath = brokenOutputPath,
                        Codec = "libx264",
                        TargetHeight = 1080,
                        UpscalerBackend = UpscalerBackendKind.RealEsrgan.ToString(),
                        RefinerBackend = RefinerBackendKind.None.ToString(),
                        ProcessedFrames = 0,
                        TotalFrames = 80,
                        Stage = "Preparing",
                        Complete = false,
                        CanResume = true,
                        UpdatedUtc = DateTime.UtcNow
                    }));

                var viewModel = new MainViewModel
                {
                    RootFolder = tempRoot,
                    OutputFolder = outputDir,
                    Overwrite = false,
                    SelectedCodec = "x264",
                    SelectedTarget = "1080p",
                    SelectedContainer = "mkv",
                    EncoderPreset = "slower",
                    FfmpegThreadsText = "1",
                    UpscalerThreadsText = "1:1:1",
                    TileSizeText = "-1",
                    UseAntiFlicker = false
                };

                viewModel.OutputConflictRequested += _ => Task.FromResult(OutputConflictDecision.Resume);
                viewModel.Items.Add(item);
                AttachQueueItem(viewModel, item);

                var prepared = await PrepareRunListAsync(viewModel, new[] { item }, BuildOptions(viewModel), CancellationToken.None);

                Assert.Single(prepared);
                Assert.True(item.ResumeRequested);
                Assert.True(item.ResumeProcessedFrames > 0);
                Assert.False(string.IsNullOrWhiteSpace(item.ResumeSourceOutputPath));
                Assert.True(File.Exists(item.ResumeSourceOutputPath));
            }
            finally
            {
                TestSupport.TryDeleteDirectory(tempRoot);
            }
        });
    }

    private static PipelineOptions BuildOptions(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod("BuildOptions", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(nameof(MainViewModel), "BuildOptions");
        }

        return (PipelineOptions)(method.Invoke(viewModel, Array.Empty<object>()) ?? throw new InvalidOperationException("BuildOptions returned null."));
    }

    private static async Task<List<QueueItemViewModel>> PrepareRunListAsync(
        MainViewModel viewModel,
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        CancellationToken cancellationToken)
    {
        var method = typeof(MainViewModel).GetMethod("PrepareRunListAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(nameof(MainViewModel), "PrepareRunListAsync");
        }

        var task = (Task<List<QueueItemViewModel>>) (method.Invoke(viewModel, new object[] { items, options, cancellationToken })
            ?? throw new InvalidOperationException("PrepareRunListAsync returned null."));
        return await task;
    }

    private static void AttachQueueItem(MainViewModel viewModel, QueueItemViewModel item)
    {
        var method = typeof(MainViewModel).GetMethod("AttachQueueItem", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(nameof(MainViewModel), "AttachQueueItem");
        }

        method.Invoke(viewModel, new object[] { item });
    }

    private static async Task CreatePartialOutputAsync(string ffmpegPath, string sourcePath, string outputPath)
    {
        var args =
            $"-hide_banner -y -nostats -loglevel error -t 3 -i \"{sourcePath}\" -c copy \"{outputPath}\"";

        var exitCode = await ProcessRunner.RunAsync(
            ffmpegPath,
            args,
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            null,
            null,
            CancellationToken.None);

        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to create partial output for resume test.");
        }
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
