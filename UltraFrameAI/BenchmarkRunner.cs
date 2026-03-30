using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed record BenchmarkMetricsSummary(
    double? StartCpu,
    double? AvgCpu,
    double? PeakCpu,
    double? StartRam,
    double? AvgRam,
    double? PeakRam,
    double? StartGpu,
    double? AvgGpu,
    double? PeakGpu,
    double? StartVram,
    double? AvgVram,
    double? PeakVram)
{
    public static BenchmarkMetricsSummary Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null);
}

public sealed record BenchmarkRequest(
    string SourcePath,
    string? OutputDir = null,
    int SampleSeconds = 20,
    string BaselineThreads = "4:4:4",
    int BaselineTileSize = 1024,
    int? GpuId = null);

public enum BenchmarkProgressKind
{
    BenchmarkStarted,
    CaseStarted,
    CaseProgress,
    CaseCompleted,
    BenchmarkCompleted
}

public sealed record BenchmarkProgressUpdate(
    BenchmarkProgressKind Kind,
    int StepIndex,
    int TotalSteps,
    string Group,
    string CaseName,
    double Progress,
    string ProgressText,
    string ElapsedText,
    string EtaText,
    string CurrentStatus,
    string CurrentDetail,
    string StageElapsedText,
    BenchmarkCaseResult? Result = null);

public sealed record BenchmarkCaseResult(
    string Group,
    string Name,
    string Codec,
    string Preset,
    bool AntiFlicker,
    string ContentMode,
    double Strength,
    string UpscalerThreads,
    int TileSize,
    double QualityScore,
    TimeSpan Elapsed,
    long OutputBytes,
    bool Success,
    string? Error,
    BenchmarkMetricsSummary Metrics);

public sealed record BenchmarkReport(
    string SourcePath,
    string SampleFile,
    double SampleDuration,
    string OutputRoot,
    IReadOnlyList<BenchmarkCaseResult> Results);

public static class BenchmarkRunner
{
    private const string GroupCodecPreset = "Codec/Preset";
    private const string GroupAntiFlicker = "Anti-flicker";
    private const string GroupUpscalerThreads = "Upscaler threads";
    private const string GroupTileSize = "Tile size";

    private sealed record BenchmarkCase(
        string Name,
        string Codec,
        string Preset,
        bool UseAntiFlicker,
        string ContentMode,
        double AntiFlickerStrength,
        string UpscalerThreads,
        int TileSize,
        double QualityScore);

    private sealed record BenchmarkResult(
        string Group,
        string Name,
        string Codec,
        string Preset,
        bool AntiFlicker,
        string ContentMode,
        double Strength,
        string UpscalerThreads,
        int TileSize,
        double QualityScore,
        TimeSpan Elapsed,
        long OutputBytes,
        bool Success,
        string? Error,
        BenchmarkMetricsSummary Metrics);

    private static string CaseCodecPreset(string codec, string preset)
        => string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkCaseCodecPreset, codec, LocalizedPresetForCase(preset));

    private static string CaseAntiFlickerOff => LocalizedStrings.BenchmarkCaseAntiFlickerOff;
    private static string CaseAntiFlickerVideo => LocalizedStrings.BenchmarkCaseAntiFlickerVideo;
    private static string CaseAntiFlickerFaces => LocalizedStrings.BenchmarkCaseAntiFlickerFaces;
    private static string CaseAntiFlickerAnime => LocalizedStrings.BenchmarkCaseAntiFlickerAnime;
    private static string CaseAntiFlickerAnimeUltra => LocalizedStrings.BenchmarkCaseAntiFlickerAnimeUltra;

    internal static string LocalizedPresetForCase(string preset) => preset switch
    {
        "slower" => LocalizedStrings.BenchmarkShortCodecSlow,
        "slow" => LocalizedStrings.BenchmarkShortCodecSlow,
        "medium" => LocalizedStrings.BenchmarkShortCodecMed,
        "fast" => LocalizedStrings.BenchmarkShortCodecFast,
        "faster" => LocalizedStrings.BenchmarkShortCodecFast,
        "veryfast" => LocalizedStrings.BenchmarkShortCodecVFast,
        _ => preset
    };

    internal static string LocalizedContentMode(string contentMode) => contentMode switch
    {
        "video" => LocalizedStrings.ContentModeVideo,
        "faces" => LocalizedStrings.ContentModeFaces,
        "anime" => LocalizedStrings.ContentModeAnime,
        "anime-ultra" => LocalizedStrings.ContentModeAnimeUltra,
        "animeultra" => LocalizedStrings.ContentModeAnimeUltra,
        _ => contentMode
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var settings = Parse(args);
        if (settings is null)
        {
            return 1;
        }

        var repoRoot = FindRepoRoot();
        var ffmpeg = FindFile("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe", repoRoot);
        var ffprobe = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe", repoRoot);
        var upscaler = FindFile("realesrgan-ncnn-vulkan.exe", Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-20220424", "realesrgan-ncnn-vulkan.exe"), repoRoot);
        var modelDir = FindDirectory("models", Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-20220424", "models"), repoRoot);

        var source = ResolveSource(settings.SourcePath);
        var outputRoot = settings.OutputDir ?? Path.Combine(Path.GetDirectoryName(source) ?? repoRoot, $"UltraFrameAI-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outputRoot);

        var sampleDir = Path.Combine(outputRoot, "sample");
        Directory.CreateDirectory(sampleDir);
        var sampleFile = Path.Combine(sampleDir, Path.GetFileNameWithoutExtension(source) + "-sample.mkv");
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDuration = await GetDurationAsync(ffprobe, source, cancellationToken).ConfigureAwait(false);
        var sampleWindow = ChooseSampleWindow(sourceDuration, settings.SampleSeconds);
        await CreateSampleClipAsync(ffmpeg, source, sampleFile, sampleWindow.Start, sampleWindow.Length, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var sampleDuration = await GetDurationAsync(ffprobe, sampleFile, cancellationToken).ConfigureAwait(false);
        var pipeline = new PipelineService();
        var results = new List<BenchmarkResult>();
        var logPath = Path.Combine(outputRoot, "benchmark.log");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSource} {source}");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSample} {sampleFile}");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSampleStart} {sampleWindow.Start:0.###} s");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSampleDuration} {sampleDuration:0.###} s");
        AppendLine(logPath, string.Empty);

        var baselineThreads = settings.BaselineThreads;
        var baselineTile = settings.BaselineTileSize;

        var groups = new List<(string Group, IReadOnlyList<BenchmarkCase> Cases)>
        {
            (GroupCodecPreset, new[]
            {
                new BenchmarkCase(CaseCodecPreset("x264", "medium"), "x264", "medium", false, "video", 0, baselineThreads, baselineTile, 46),
                new BenchmarkCase(CaseCodecPreset("x264", "slower"), "x264", "slower", false, "video", 0, baselineThreads, baselineTile, 50),
                new BenchmarkCase(CaseCodecPreset("x265", "medium"), "x265", "medium", false, "video", 0, baselineThreads, baselineTile, 68),
                new BenchmarkCase(CaseCodecPreset("x265", "slower"), "x265", "slower", false, "video", 0, baselineThreads, baselineTile, 73),
            }),
            (GroupAntiFlicker, new[]
            {
                new BenchmarkCase(CaseAntiFlickerOff, "x264", "medium", false, "video", 0, baselineThreads, baselineTile, 42),
                new BenchmarkCase(CaseAntiFlickerVideo, "x264", "medium", true, "video", 42, baselineThreads, baselineTile, 55),
                new BenchmarkCase(CaseAntiFlickerFaces, "x264", "medium", true, "faces", 35, baselineThreads, baselineTile, 60),
                new BenchmarkCase(CaseAntiFlickerAnime, "x264", "medium", true, "anime", 65, baselineThreads, baselineTile, 69),
                new BenchmarkCase(CaseAntiFlickerAnimeUltra, "x264", "medium", true, "anime-ultra", 88, baselineThreads, baselineTile, 78),
            }),
            (GroupUpscalerThreads, new[]
            {
                new BenchmarkCase("4:4:4", "x264", "medium", false, "video", 0, "4:4:4", baselineTile, 58),
                new BenchmarkCase("6:6:6", "x264", "medium", false, "video", 0, "6:6:6", baselineTile, 58),
                new BenchmarkCase("8:8:8", "x264", "medium", false, "video", 0, "8:8:8", baselineTile, 58),
                new BenchmarkCase("2:2:2", "x264", "medium", false, "video", 0, "2:2:2", baselineTile, 58),
                new BenchmarkCase("1:1:1", "x264", "medium", false, "video", 0, "1:1:1", baselineTile, 58),
            }),
            (GroupTileSize, new[]
            {
                new BenchmarkCase("1024", "x264", "medium", false, "video", 0, baselineThreads, 1024, 58),
                new BenchmarkCase("1536", "x264", "medium", false, "video", 0, baselineThreads, 1536, 59),
                new BenchmarkCase("2048", "x264", "medium", false, "video", 0, baselineThreads, 2048, 60),
                new BenchmarkCase("512", "x264", "medium", false, "video", 0, baselineThreads, 512, 56),
                new BenchmarkCase("4096", "x264", "medium", false, "video", 0, baselineThreads, 4096, 61),
                new BenchmarkCase("256", "x264", "medium", false, "video", 0, baselineThreads, 256, 53),
            }),
        };

        var totalSteps = groups.Sum(group => group.Cases.Count);
        var currentStep = 0;

        foreach (var (group, cases) in groups)
        {
            AppendLine(logPath, $"== {group} ==");
            foreach (var benchCase in cases)
            {
                currentStep++;
                var result = await RunCaseAsync(
                    pipeline,
                    sampleFile,
                    outputRoot,
                    group,
                    benchCase,
                    settings,
                    modelDir,
                    ffmpeg,
                    ffprobe,
                    upscaler,
                    currentStep,
                    totalSteps,
                    sampleDuration,
                    null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                results.Add(result);
                AppendLine(logPath, FormatResult(result));
            }

            AppendLine(logPath, string.Empty);
        }

        AppendLine(logPath, LocalizedStrings.BenchmarkLogSummary);
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogFastestOverall, FormatSummary(results.OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestCodecPreset, FormatSummary(results.Where(r => r.Group == GroupCodecPreset).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestAntiFlicker, FormatSummary(results.Where(r => r.Group == GroupAntiFlicker).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestUpscalerThreads, FormatSummary(results.Where(r => r.Group == GroupUpscalerThreads).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestTileSize, FormatSummary(results.Where(r => r.Group == GroupTileSize).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendBestSettings(logPath, results);
        AppendRecommendedFastPreset(logPath, results);
        AppendMetricsSummary(logPath, results);
        AppendLine(logPath, string.Empty);

        WriteReports(outputRoot, source, sampleFile, sampleDuration, results);
        if (!string.IsNullOrWhiteSpace(settings.DoneFile))
        {
            File.WriteAllText(settings.DoneFile, DateTime.Now.ToString("O", CultureInfo.InvariantCulture), new UTF8Encoding(false));
        }
        return 0;
    }

    private static async Task<BenchmarkResult> RunCaseAsync(
        PipelineService pipeline,
        string sampleFile,
        string outputRoot,
        string group,
        BenchmarkCase benchCase,
        BenchmarkSettings settings,
        string modelDir,
        string ffmpeg,
        string ffprobe,
        string upscaler,
        int stepIndex,
        int totalSteps,
        double sampleDuration,
        IProgress<BenchmarkProgressUpdate>? uiProgress,
        CancellationToken cancellationToken)
    {
        var caseDir = Path.Combine(outputRoot, Sanitize($"{group}-{benchCase.Name}"));
        Directory.CreateDirectory(caseDir);
        var outputPath = Path.Combine(caseDir, "output.mkv");

        var item = new QueueItemViewModel
        {
            Index = 1,
            Title = benchCase.Name,
            SourcePath = sampleFile,
            OutputPath = outputPath
        };
        item.ResetUiState();

        uiProgress?.Report(new BenchmarkProgressUpdate(
            BenchmarkProgressKind.CaseStarted,
            stepIndex,
            totalSteps,
            group,
            benchCase.Name,
            0,
            "0%",
            "--:--:--",
            "--:--:--",
            "Starting",
            string.Empty,
            "--:--:--"));

        var options = new PipelineOptions
        {
            RootFolder = Path.GetDirectoryName(sampleFile) ?? Path.GetPathRoot(sampleFile) ?? Environment.CurrentDirectory,
            OutputFolder = caseDir,
            Overwrite = true,
            UseX265 = string.Equals(benchCase.Codec, "x265", StringComparison.OrdinalIgnoreCase),
            FfmpegThreads = 0,
            UpscalerThreads = benchCase.UpscalerThreads,
            TileSize = benchCase.TileSize,
            GpuId = settings.GpuId,
            FfmpegPath = ffmpeg,
            FfprobePath = ffprobe,
            UpscalerPath = upscaler,
            ModelDir = modelDir,
            UseAntiFlicker = benchCase.UseAntiFlicker,
            ContentMode = benchCase.ContentMode,
            AntiFlickerStrength = benchCase.AntiFlickerStrength,
            EncoderPreset = benchCase.Preset
        };

        var started = Stopwatch.StartNew();
        var metricsSampler = new BenchmarkMetricsSampler();
        metricsSampler.Start();
        BenchmarkMetricsSummary metrics = BenchmarkMetricsSummary.Empty;
        var metricsCaptured = false;
        var lastConsoleProgress = string.Empty;
        var completionPrinted = false;
        try
        {
            await pipeline.RunAsync(new[] { item }, options, pipelineProgress =>
            {
                if (completionPrinted)
                {
                    return;
                }

                var liveMetrics = metricsSampler.GetLatestSnapshot();
                var consoleLine = FormatConsoleProgress(stepIndex, totalSteps, benchCase.Name, pipelineProgress, liveMetrics);
                var isCompletion = pipelineProgress.Progress >= 100;
                if (pipelineProgress.IsHeartbeat || isCompletion || !string.Equals(consoleLine, lastConsoleProgress, StringComparison.Ordinal))
                {
                    lastConsoleProgress = consoleLine;
                    WriteConsoleProgress(consoleLine, isCompletion);
                    uiProgress?.Report(new BenchmarkProgressUpdate(
                        BenchmarkProgressKind.CaseProgress,
                        stepIndex,
                        totalSteps,
                        group,
                        benchCase.Name,
                        pipelineProgress.Progress,
                        pipelineProgress.ProgressText,
                        pipelineProgress.ElapsedText,
                        pipelineProgress.EtaText,
                        pipelineProgress.CurrentStatus,
                        pipelineProgress.CurrentDetail,
                        pipelineProgress.StageElapsedText));
                    if (isCompletion)
                    {
                        completionPrinted = true;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            started.Stop();
            metrics = await metricsSampler.StopAsync().ConfigureAwait(false);
            metricsCaptured = true;
            return new BenchmarkResult(group, benchCase.Name, benchCase.Codec, benchCase.Preset, benchCase.UseAntiFlicker, benchCase.ContentMode, benchCase.AntiFlickerStrength, benchCase.UpscalerThreads, benchCase.TileSize, benchCase.QualityScore, started.Elapsed, File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0, false, LocalizedStrings.BenchmarkErrorPipelineFailed, metrics);
        }
        finally
        {
            if (!metricsCaptured)
            {
                metrics = await metricsSampler.StopAsync().ConfigureAwait(false);
                metricsCaptured = true;
            }
        }

        started.Stop();
        var bytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
        var qualityScore = await EvaluateQualityScoreAsync(
            ffmpeg,
            ffprobe,
            sampleFile,
            outputPath,
            sampleDuration,
            caseDir,
            cancellationToken).ConfigureAwait(false);
        var result = new BenchmarkResult(group, benchCase.Name, benchCase.Codec, benchCase.Preset, benchCase.UseAntiFlicker, benchCase.ContentMode, benchCase.AntiFlickerStrength, benchCase.UpscalerThreads, benchCase.TileSize, qualityScore, started.Elapsed, bytes, true, null, metrics);
        uiProgress?.Report(new BenchmarkProgressUpdate(
            BenchmarkProgressKind.CaseCompleted,
            stepIndex,
            totalSteps,
            group,
            benchCase.Name,
            100,
            "100%",
            TimeSpan.Zero.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            "00:00:00",
            "Completed",
            string.Empty,
            TimeSpan.Zero.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            new BenchmarkCaseResult(
                result.Group,
                result.Name,
                result.Codec,
                result.Preset,
                result.AntiFlicker,
                result.ContentMode,
                result.Strength,
                result.UpscalerThreads,
                result.TileSize,
                result.QualityScore,
                result.Elapsed,
                result.OutputBytes,
                result.Success,
                result.Error,
                result.Metrics)));
        return result;
    }

    public static async Task<BenchmarkReport> RunInteractiveAsync(
        BenchmarkRequest request,
        IProgress<BenchmarkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = new BenchmarkSettings(
            request.SourcePath,
            request.OutputDir,
            request.SampleSeconds,
            request.BaselineThreads,
            request.BaselineTileSize,
            request.GpuId,
            null);

        var repoRoot = FindRepoRoot();
        var ffmpeg = FindFile("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe", repoRoot);
        var ffprobe = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe", repoRoot);
        var upscaler = FindFile("realesrgan-ncnn-vulkan.exe", Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-20220424", "realesrgan-ncnn-vulkan.exe"), repoRoot);
        var modelDir = FindDirectory("models", Path.Combine(repoRoot, "realesrgan-ncnn-vulkan-20220424", "models"), repoRoot);

        var source = ResolveSource(settings.SourcePath);
        var outputRoot = settings.OutputDir ?? Path.Combine(Path.GetDirectoryName(source) ?? repoRoot, $"UltraFrameAI-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outputRoot);

        var sampleDir = Path.Combine(outputRoot, "sample");
        Directory.CreateDirectory(sampleDir);
        var sampleFile = Path.Combine(sampleDir, Path.GetFileNameWithoutExtension(source) + "-sample.mkv");
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDuration = await GetDurationAsync(ffprobe, source, cancellationToken).ConfigureAwait(false);
        var sampleWindow = ChooseSampleWindow(sourceDuration, settings.SampleSeconds);
        await CreateSampleClipAsync(ffmpeg, source, sampleFile, sampleWindow.Start, sampleWindow.Length, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var sampleDuration = await GetDurationAsync(ffprobe, sampleFile, cancellationToken).ConfigureAwait(false);
        var pipeline = new PipelineService();
        var results = new List<BenchmarkResult>();
        var logPath = Path.Combine(outputRoot, "benchmark.log");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSource} {source}");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSample} {sampleFile}");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSampleStart} {sampleWindow.Start:0.###} s");
        AppendLine(logPath, $"{LocalizedStrings.BenchmarkLogSampleDuration} {sampleDuration:0.###} s");
        AppendLine(logPath, string.Empty);

        var baselineThreads = settings.BaselineThreads;
        var baselineTile = settings.BaselineTileSize;

        var groups = new List<(string Group, IReadOnlyList<BenchmarkCase> Cases)>
        {
            (GroupCodecPreset, new[]
            {
                new BenchmarkCase(CaseCodecPreset("x264", "medium"), "x264", "medium", false, "video", 0, baselineThreads, baselineTile, 46),
                new BenchmarkCase(CaseCodecPreset("x264", "slower"), "x264", "slower", false, "video", 0, baselineThreads, baselineTile, 50),
                new BenchmarkCase(CaseCodecPreset("x265", "medium"), "x265", "medium", false, "video", 0, baselineThreads, baselineTile, 68),
                new BenchmarkCase(CaseCodecPreset("x265", "slower"), "x265", "slower", false, "video", 0, baselineThreads, baselineTile, 73),
            }),
            (GroupAntiFlicker, new[]
            {
                new BenchmarkCase(CaseAntiFlickerOff, "x264", "medium", false, "video", 0, baselineThreads, baselineTile, 42),
                new BenchmarkCase(CaseAntiFlickerVideo, "x264", "medium", true, "video", 42, baselineThreads, baselineTile, 55),
                new BenchmarkCase(CaseAntiFlickerFaces, "x264", "medium", true, "faces", 35, baselineThreads, baselineTile, 60),
                new BenchmarkCase(CaseAntiFlickerAnime, "x264", "medium", true, "anime", 65, baselineThreads, baselineTile, 69),
                new BenchmarkCase(CaseAntiFlickerAnimeUltra, "x264", "medium", true, "anime-ultra", 88, baselineThreads, baselineTile, 78),
            }),
            (GroupUpscalerThreads, new[]
            {
                new BenchmarkCase("4:4:4", "x264", "medium", false, "video", 0, "4:4:4", baselineTile, 58),
                new BenchmarkCase("6:6:6", "x264", "medium", false, "video", 0, "6:6:6", baselineTile, 58),
                new BenchmarkCase("8:8:8", "x264", "medium", false, "video", 0, "8:8:8", baselineTile, 58),
                new BenchmarkCase("2:2:2", "x264", "medium", false, "video", 0, "2:2:2", baselineTile, 58),
                new BenchmarkCase("1:1:1", "x264", "medium", false, "video", 0, "1:1:1", baselineTile, 58),
            }),
            (GroupTileSize, new[]
            {
                new BenchmarkCase("1024", "x264", "medium", false, "video", 0, baselineThreads, 1024, 58),
                new BenchmarkCase("1536", "x264", "medium", false, "video", 0, baselineThreads, 1536, 59),
                new BenchmarkCase("2048", "x264", "medium", false, "video", 0, baselineThreads, 2048, 60),
                new BenchmarkCase("512", "x264", "medium", false, "video", 0, baselineThreads, 512, 56),
                new BenchmarkCase("4096", "x264", "medium", false, "video", 0, baselineThreads, 4096, 61),
                new BenchmarkCase("256", "x264", "medium", false, "video", 0, baselineThreads, 256, 53),
            }),
        };

        var totalSteps = groups.Sum(group => group.Cases.Count);
        var currentStep = 0;
        progress?.Report(new BenchmarkProgressUpdate(
            BenchmarkProgressKind.BenchmarkStarted,
            0,
            totalSteps,
            string.Empty,
            string.Empty,
            0,
            "0%",
            "--:--:--",
            "--:--:--",
            LocalizedStrings.BenchmarkStatusStarting,
            string.Empty,
            "--:--:--"));

        foreach (var (group, cases) in groups)
        {
            AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogGroupSection, GetLocalizedGroupName(group)));
            foreach (var benchCase in cases)
            {
                currentStep++;
                var result = await RunCaseAsync(
                    pipeline,
                    sampleFile,
                    outputRoot,
                    group,
                    benchCase,
                    settings,
                    modelDir,
                    ffmpeg,
                    ffprobe,
                    upscaler,
                    currentStep,
                    totalSteps,
                    sampleDuration,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                results.Add(result);
                AppendLine(logPath, FormatResult(result));
            }

            AppendLine(logPath, string.Empty);
        }

        AppendLine(logPath, LocalizedStrings.BenchmarkLogSummary);
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogFastestOverall, FormatSummary(results.OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestCodecPreset, FormatSummary(results.Where(r => r.Group == GroupCodecPreset).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestAntiFlicker, FormatSummary(results.Where(r => r.Group == GroupAntiFlicker).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestUpscalerThreads, FormatSummary(results.Where(r => r.Group == GroupUpscalerThreads).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendLine(logPath, string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkLogBestTileSize, FormatSummary(results.Where(r => r.Group == GroupTileSize).OrderBy(r => r.Elapsed).FirstOrDefault())));
        AppendBestSettings(logPath, results);
        AppendRecommendedFastPreset(logPath, results);
        AppendMetricsSummary(logPath, results);
        AppendLine(logPath, string.Empty);

        WriteReports(outputRoot, source, sampleFile, sampleDuration, results);
        progress?.Report(new BenchmarkProgressUpdate(
            BenchmarkProgressKind.BenchmarkCompleted,
            totalSteps,
            totalSteps,
            string.Empty,
            string.Empty,
            100,
            "100%",
            TimeSpan.Zero.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            "00:00:00",
            LocalizedStrings.BenchmarkStatusFinished,
            string.Empty,
            "00:00:00"));

        return new BenchmarkReport(
            source,
            sampleFile,
            sampleDuration,
            outputRoot,
            results.Select(result => new BenchmarkCaseResult(
                result.Group,
                result.Name,
                result.Codec,
                result.Preset,
                result.AntiFlicker,
                result.ContentMode,
                result.Strength,
                result.UpscalerThreads,
                result.TileSize,
                result.QualityScore,
                result.Elapsed,
                result.OutputBytes,
                result.Success,
                result.Error,
                result.Metrics)).ToList());
    }

    private sealed record BenchmarkSettings(string SourcePath, string? OutputDir, int SampleSeconds, string BaselineThreads, int BaselineTileSize, int? GpuId, string? DoneFile);

    private static BenchmarkSettings? Parse(string[] args)
    {
        string? source = null;
        string? output = null;
        string? doneFile = null;
        int sampleSeconds = 20;
        string baselineThreads = "4:4:4";
        int baselineTile = 1024;
        int? gpuId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? NextValue()
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                i++;
                return args[i];
            }

            if (arg.Equals("--benchmark-source", StringComparison.OrdinalIgnoreCase) || arg.Equals("--source", StringComparison.OrdinalIgnoreCase))
            {
                source = NextValue();
            }
            else if (arg.Equals("--benchmark-output", StringComparison.OrdinalIgnoreCase) || arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                output = NextValue();
            }
            else if (arg.Equals("--benchmark-done-file", StringComparison.OrdinalIgnoreCase) || arg.Equals("--done-file", StringComparison.OrdinalIgnoreCase))
            {
                doneFile = NextValue();
            }
            else if (arg.Equals("--benchmark-seconds", StringComparison.OrdinalIgnoreCase) || arg.Equals("--seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(NextValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    sampleSeconds = parsed;
                }
            }
            else if (arg.Equals("--threads", StringComparison.OrdinalIgnoreCase))
            {
                baselineThreads = NextValue() ?? baselineThreads;
            }
            else if (arg.Equals("--tile", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(NextValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    baselineTile = parsed;
                }
            }
            else if (arg.Equals("--gpu", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(NextValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    gpuId = parsed;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine(LocalizedStrings.BenchmarkErrorRequiresSource);
            return null;
        }

        return new BenchmarkSettings(source!, output, sampleSeconds, baselineThreads, baselineTile, gpuId, doneFile);
    }

    private static string ResolveSource(string source)
    {
        if (Directory.Exists(source))
        {
            var video = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(IsVideoFile);
            if (video is null)
            {
                throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkErrorNoVideoFilesFound, source));
            }

            return video;
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkErrorSourceFileNotFound, source));
        }

        return Path.GetFullPath(source);
    }

    private static async Task CreateSampleClipAsync(string ffmpeg, string source, string output, double start, int sampleSeconds, CancellationToken ct)
    {
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        var length = Math.Max(1, sampleSeconds > 0 ? sampleSeconds : 20);
        var args = $"-hide_banner -y -ss {start.ToString("0.###", CultureInfo.InvariantCulture)} -i {Quote(source)} -t {length.ToString(CultureInfo.InvariantCulture)} -map 0:v:0 -map 0:a? -c copy -avoid_negative_ts make_zero {Quote(output)}";
        var runner = ProcessRunner.RunAsync(ffmpeg, args, Path.GetDirectoryName(output) ?? Environment.CurrentDirectory, _ => { }, _ => { }, ct);
        var exitCode = await runner.ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkErrorUnableToCreateSampleClipWithExitCode, exitCode));
        }

        if (!File.Exists(output))
        {
            throw new InvalidOperationException(LocalizedStrings.BenchmarkErrorUnableToCreateSampleClip);
        }
    }

    internal static (double Start, int Length) ChooseSampleWindow(double sourceDuration, int requestedSeconds)
    {
        var length = Math.Max(1, requestedSeconds);
        if (sourceDuration <= 0)
        {
            return (0, length);
        }

        if (sourceDuration < length)
        {
            return (0, Math.Max(1, (int)Math.Floor(sourceDuration)));
        }

        if (sourceDuration <= length * 2)
        {
            var startNearMiddle = Math.Max(0, (sourceDuration - length) * 0.5);
            return (startNearMiddle, length);
        }

        var centered = sourceDuration * 0.5 - length * 0.5;
        var start = Math.Clamp(centered, length * 0.15, Math.Max(0, sourceDuration - length));
        return (start, length);
    }

    private static async Task<double> GetDurationAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(ffprobe, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Quote(sourcePath)}", Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, ct).ConfigureAwait(false);
        return lines.Count > 0 && double.TryParse(lines[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static async Task<double> EvaluateQualityScoreAsync(
        string ffmpeg,
        string ffprobe,
        string sourcePath,
        string outputPath,
        double sampleDuration,
        string workDir,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath) || !File.Exists(outputPath))
            {
                return 50;
            }

            var (width, height) = await GetVideoDimensionsAsync(ffprobe, sourcePath, ct).ConfigureAwait(false);
            if (width <= 0 || height <= 0)
            {
                return 50;
            }

            var timestamps = PickQualityTimestamps(sampleDuration).ToArray();
            if (timestamps.Length == 0)
            {
                return 50;
            }

            var sampleDir = Path.Combine(workDir, "quality");
            Directory.CreateDirectory(sampleDir);
            var scores = new List<double>();

            for (var i = 0; i < timestamps.Length; i++)
            {
                var timestamp = timestamps[i];
                var sourceFrame = Path.Combine(sampleDir, $"source-{i:00}.png");
                var outputFrame = Path.Combine(sampleDir, $"output-{i:00}.png");

                await ExtractFrameAsync(ffmpeg, sourcePath, sourceFrame, timestamp, width, height, false, ct).ConfigureAwait(false);
                await ExtractFrameAsync(ffmpeg, outputPath, outputFrame, timestamp, width, height, true, ct).ConfigureAwait(false);

                var sourcePixels = LoadGrayPixels(sourceFrame, out var sourceWidth, out var sourceHeight);
                var outputPixels = LoadGrayPixels(outputFrame, out var outputWidth, out var outputHeight);
                if (sourceWidth == outputWidth && sourceHeight == outputHeight && sourcePixels.Length == outputPixels.Length)
                {
                    scores.Add(ComputeFrameScore(sourcePixels, outputPixels, sourceWidth, sourceHeight));
                }
            }

            return scores.Count > 0 ? scores.Average() : 50;
        }
        catch
        {
            return 50;
        }
    }

    private static IReadOnlyList<double> PickQualityTimestamps(double duration)
    {
        if (duration <= 0.5)
        {
            return new[] { 0.1 };
        }

        var points = new List<double>();
        foreach (var fraction in new[] { 0.2, 0.5, 0.8 })
        {
            var time = Math.Clamp(duration * fraction, 0.05, Math.Max(0.05, duration - 0.05));
            if (points.Count == 0 || Math.Abs(points[^1] - time) > 0.02)
            {
                points.Add(time);
            }
        }

        return points;
    }

    private static async Task<(int Width, int Height)> GetVideoDimensionsAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 {Quote(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        var line = lines.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line))
        {
            return (0, 0);
        }

        var parts = line.Trim().Split('x', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            return (width, height);
        }

        return (0, 0);
    }

    private static async Task ExtractFrameAsync(
        string ffmpeg,
        string inputPath,
        string outputPath,
        double timestamp,
        int sourceWidth,
        int sourceHeight,
        bool scaleToSource,
        CancellationToken ct)
    {
        var filter = scaleToSource
            ? $"scale={sourceWidth}:{sourceHeight}:flags=lanczos,format=gray"
            : "format=gray";
        var args = $"-hide_banner -loglevel error -y -ss {timestamp.ToString("0.###", CultureInfo.InvariantCulture)} -i {Quote(inputPath)} -frames:v 1 -vf {Quote(filter)} {Quote(outputPath)}";
        var exitCode = await ProcessRunner.RunAsync(
            ffmpeg,
            args,
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            null,
            null,
            ct).ConfigureAwait(false);

        if (exitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"Unable to extract frame from {inputPath}");
        }
    }

    private static byte[] LoadGrayPixels(string imagePath, out int width, out int height)
    {
        var decoder = BitmapDecoder.Create(
            new Uri(imagePath, UriKind.Absolute),
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        width = gray.PixelWidth;
        height = gray.PixelHeight;
        var pixels = new byte[Math.Max(1, width * height)];
        gray.CopyPixels(pixels, width, 0);
        return pixels;
    }

    private static double ComputeFrameScore(byte[] sourcePixels, byte[] outputPixels, int width, int height)
    {
        var pixelCount = Math.Max(1, width * height);
        double mse = 0;
        double meanAbs = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            var diff = sourcePixels[i] - outputPixels[i];
            mse += diff * diff;
            meanAbs += Math.Abs(diff);
        }

        mse /= pixelCount;
        meanAbs /= pixelCount;

        var sourceEdge = ComputeEdgeEnergy(sourcePixels, width, height);
        var outputEdge = ComputeEdgeEnergy(outputPixels, width, height);
        var edgeDelta = Math.Abs(outputEdge - sourceEdge) / Math.Max(1.0, sourceEdge);

        var mseScore = 100.0 * Math.Exp(-mse / (255.0 * 255.0) * 14.0);
        var toneScore = 100.0 * Math.Exp(-meanAbs / 255.0 * 7.0);
        var edgeScore = 100.0 * Math.Exp(-edgeDelta * 3.5);

        return Math.Clamp((mseScore * 0.55) + (toneScore * 0.2) + (edgeScore * 0.25), 0, 100);
    }

    private static double ComputeEdgeEnergy(byte[] pixels, int width, int height)
    {
        if (width <= 1 || height <= 1)
        {
            return 0;
        }

        double energy = 0;
        long comparisons = 0;
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var current = pixels[rowOffset + x];
                if (x + 1 < width)
                {
                    energy += Math.Abs(current - pixels[rowOffset + x + 1]);
                    comparisons++;
                }

                if (y + 1 < height)
                {
                    energy += Math.Abs(current - pixels[rowOffset + width + x]);
                    comparisons++;
                }
            }
        }

        return comparisons > 0 ? energy / comparisons : 0;
    }

    private static void WriteReports(string outputRoot, string source, string sampleFile, double sampleDuration, IReadOnlyList<BenchmarkResult> results)
    {
        var csvPath = Path.Combine(outputRoot, "benchmark-results.csv");
        var mdPath = Path.Combine(outputRoot, "benchmark-results.md");
        var csv = new StringBuilder();
        csv.AppendLine("Group,Name,Codec,Preset,AntiFlicker,ContentMode,Strength,UpscalerThreads,TileSize,ElapsedSeconds,OutputMB,CpuStartPct,CpuAvgPct,CpuPeakPct,RamStartPct,RamAvgPct,RamPeakPct,GpuStartPct,GpuAvgPct,GpuPeakPct,VramStartPct,VramAvgPct,VramPeakPct,Success,Error");
        foreach (var result in results)
        {
            csv.AppendLine(string.Join(",",
                Csv(result.Group),
                Csv(result.Name),
                Csv(result.Codec),
                Csv(result.Preset),
                result.AntiFlicker ? "true" : "false",
                Csv(LocalizedContentMode(result.ContentMode)),
                result.Strength.ToString("0.##", CultureInfo.InvariantCulture),
                Csv(result.UpscalerThreads),
                result.TileSize.ToString(CultureInfo.InvariantCulture),
                result.Elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                (result.OutputBytes / 1024d / 1024d).ToString("0.###", CultureInfo.InvariantCulture),
                Csv(MetricValue(result.Metrics.StartCpu)),
                Csv(MetricValue(result.Metrics.AvgCpu)),
                Csv(MetricValue(result.Metrics.PeakCpu)),
                Csv(MetricBytesGiBValue(result.Metrics.StartRam)),
                Csv(MetricBytesGiBValue(result.Metrics.AvgRam)),
                Csv(MetricBytesGiBValue(result.Metrics.PeakRam)),
                Csv(MetricValue(result.Metrics.StartGpu)),
                Csv(MetricValue(result.Metrics.AvgGpu)),
                Csv(MetricValue(result.Metrics.PeakGpu)),
                Csv(MetricBytesGiBValue(result.Metrics.StartVram)),
                Csv(MetricBytesGiBValue(result.Metrics.AvgVram)),
                Csv(MetricBytesGiBValue(result.Metrics.PeakVram)),
                result.Success ? "true" : "false",
                Csv(result.Error ?? string.Empty)));
        }

        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));

        var md = new StringBuilder();
        md.AppendLine("# UltraFrameAI Benchmark");
        md.AppendLine();
        md.AppendLine($"- {LocalizedStrings.BenchmarkLogSource} `{source}`");
        md.AppendLine($"- {LocalizedStrings.BenchmarkLogSample} `{sampleFile}`");
        md.AppendLine($"- {LocalizedStrings.BenchmarkLogSampleDuration} `{sampleDuration:0.###} s`");
        md.AppendLine();
        md.AppendLine($"| {LocalizedStrings.BenchmarkReportTableGroup} | {LocalizedStrings.BenchmarkReportTableCase} | {LocalizedStrings.BenchmarkReportTableCodec} | {LocalizedStrings.BenchmarkReportTablePreset} | {LocalizedStrings.BenchmarkReportTableAf} | {LocalizedStrings.BenchmarkReportTableMode} | {LocalizedStrings.BenchmarkReportTableStrength} | {LocalizedStrings.BenchmarkReportTableThreads} | {LocalizedStrings.BenchmarkReportTableTile} | {LocalizedStrings.BenchmarkReportTableTime} | {LocalizedStrings.BenchmarkReportTableOutputMb} | {LocalizedStrings.BenchmarkReportTableCpu} | {LocalizedStrings.BenchmarkReportTableRamGb} | {LocalizedStrings.BenchmarkReportTableGpu} | {LocalizedStrings.BenchmarkReportTableVramGb} |");
        md.AppendLine("|---|---|---|---|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var result in results)
        {
            md.AppendLine($"| {EscapeMd(result.Group)} | {EscapeMd(result.Name)} | {EscapeMd(result.Codec)} | {EscapeMd(result.Preset)} | {(result.AntiFlicker ? "on" : "off")} | {EscapeMd(LocalizedContentMode(result.ContentMode))} | {result.Strength:0.##} | {EscapeMd(result.UpscalerThreads)} | {result.TileSize} | {result.Elapsed.TotalSeconds:0.###} | {(result.OutputBytes / 1024d / 1024d):0.###} | {MetricTriplet(result.Metrics.StartCpu, result.Metrics.AvgCpu, result.Metrics.PeakCpu)} | {MetricTriplet(result.Metrics.StartRam, result.Metrics.AvgRam, result.Metrics.PeakRam)} | {MetricTriplet(result.Metrics.StartGpu, result.Metrics.AvgGpu, result.Metrics.PeakGpu)} | {MetricTriplet(result.Metrics.StartVram, result.Metrics.AvgVram, result.Metrics.PeakVram)} |");
        }

        md.AppendLine();
        md.AppendLine($"## {LocalizedStrings.BenchmarkSummaryHeader}");
        AppendSummaryMd(md, LocalizedStrings.BenchmarkSummaryFastestOverall, results.OrderBy(r => r.Elapsed).FirstOrDefault());
        AppendSummaryMd(md, LocalizedStrings.BenchmarkSummaryBestCodecPreset, results.Where(r => r.Group == GroupCodecPreset).OrderBy(r => r.Elapsed).FirstOrDefault());
        AppendSummaryMd(md, LocalizedStrings.BenchmarkSummaryBestAntiFlicker, results.Where(r => r.Group == GroupAntiFlicker).OrderBy(r => r.Elapsed).FirstOrDefault());
        AppendSummaryMd(md, LocalizedStrings.BenchmarkSummaryBestUpscalerThreads, results.Where(r => r.Group == GroupUpscalerThreads).OrderBy(r => r.Elapsed).FirstOrDefault());
        AppendSummaryMd(md, LocalizedStrings.BenchmarkSummaryBestTileSize, results.Where(r => r.Group == GroupTileSize).OrderBy(r => r.Elapsed).FirstOrDefault());
        AppendBestSettingsMd(md, results);
        AppendRecommendedFastPresetMd(md, results);
        AppendMetricsSummaryMd(md, results);

        File.WriteAllText(mdPath, md.ToString(), new UTF8Encoding(false));
    }

    private static void AppendSummaryMd(StringBuilder md, string label, BenchmarkResult? result)
    {
        if (result is null)
        {
            md.AppendLine($"- {label}: {LocalizedStrings.BenchmarkNotAvailable}");
            return;
        }

        md.AppendLine($"- {label}: **{EscapeMd(result.Name)}** in {result.Elapsed.TotalSeconds:0.###} s");
    }

    private static void AppendBestSettings(string path, IReadOnlyList<BenchmarkResult> results)
    {
        AppendLine(path, LocalizedStrings.BenchmarkLogBestSettings);
        AppendLine(path, $"{LocalizedStrings.BenchmarkLegendCodecPreset}: {FormatSummaryWithTime(GetBest(results, GroupCodecPreset))}");
        AppendLine(path, $"{LocalizedStrings.BenchmarkLegendAntiFlicker}: {FormatSummaryWithTime(GetBest(results, GroupAntiFlicker))}");
        AppendLine(path, $"{LocalizedStrings.BenchmarkLegendThreads}: {FormatSummaryWithTime(GetBest(results, GroupUpscalerThreads))}");
        AppendLine(path, $"{LocalizedStrings.BenchmarkLegendTileSize}: {FormatSummaryWithTime(GetBest(results, GroupTileSize))}");
    }

    private static void AppendBestSettingsMd(StringBuilder md, IReadOnlyList<BenchmarkResult> results)
    {
        md.AppendLine();
        md.AppendLine($"## {LocalizedStrings.BenchmarkSummaryBestSettings}");
        AppendBestSettingMd(md, LocalizedStrings.BenchmarkLegendCodecPreset, GetBest(results, GroupCodecPreset));
        AppendBestSettingMd(md, LocalizedStrings.BenchmarkLegendAntiFlicker, GetBest(results, GroupAntiFlicker));
        AppendBestSettingMd(md, LocalizedStrings.BenchmarkLegendThreads, GetBest(results, GroupUpscalerThreads));
        AppendBestSettingMd(md, LocalizedStrings.BenchmarkLegendTileSize, GetBest(results, GroupTileSize));
    }

    private static void AppendBestSettingMd(StringBuilder md, string label, BenchmarkResult? result)
    {
        if (result is null)
        {
            md.AppendLine($"- {label}: {LocalizedStrings.BenchmarkNotAvailable}");
            return;
        }

        md.AppendLine($"- {label}: **{EscapeMd(result.Name)}** in {result.Elapsed.TotalSeconds:0.###} s");
    }

    private static void AppendRecommendedFastPresetMd(StringBuilder md, IReadOnlyList<BenchmarkResult> results)
    {
        var codec = GetBest(results, GroupCodecPreset);
        var antiFlicker = GetBest(results, GroupAntiFlicker);
        var threads = GetBest(results, GroupUpscalerThreads);
        var tile = GetBest(results, GroupTileSize);

        md.AppendLine();
        md.AppendLine($"## {LocalizedStrings.BenchmarkSummaryRecommendedFastPreset}");
        if (codec is null || antiFlicker is null || threads is null || tile is null)
        {
            md.AppendLine($"- {LocalizedStrings.BenchmarkNotAvailable}");
            return;
        }

        md.AppendLine($"- {LocalizedStrings.BenchmarkLegendCodecPreset}: **{EscapeMd(codec.Codec)} / {EscapeMd(codec.Preset)}**");
        md.AppendLine($"- {LocalizedStrings.BenchmarkLegendAntiFlicker}: **{EscapeMd(antiFlicker.Name)}**");
        md.AppendLine($"- {LocalizedStrings.BenchmarkLegendThreads}: **{EscapeMd(threads.UpscalerThreads)}**");
        md.AppendLine($"- {LocalizedStrings.BenchmarkLegendTileSize}: **{tile.TileSize}**");
    }

    private static void AppendMetricsSummaryMd(StringBuilder md, IReadOnlyList<BenchmarkResult> results)
    {
        var metrics = CollectMetrics(results);

        md.AppendLine();
        md.AppendLine($"## {LocalizedStrings.BenchmarkSummaryMetricsSummary}");
        md.AppendLine($"- CPU start/avg/peak: **{MetricTriplet(metrics.StartCpu, metrics.AvgCpu, metrics.PeakCpu)}**");
        md.AppendLine($"- RAM start/avg/peak: **{MetricTripletGiB(metrics.StartRam, metrics.AvgRam, metrics.PeakRam)} GB**");
        md.AppendLine($"- RAM quick: **{MetricPairGiB(metrics.StartRam, metrics.PeakRam)} GB**");
        md.AppendLine($"- GPU start/avg/peak: **{MetricTriplet(metrics.StartGpu, metrics.AvgGpu, metrics.PeakGpu)}**");
        md.AppendLine($"- VRAM start/avg/peak: **{MetricTripletGiB(metrics.StartVram, metrics.AvgVram, metrics.PeakVram)} GB**");
        md.AppendLine($"- VRAM quick: **{MetricPairGiB(metrics.StartVram, metrics.PeakVram)} GB**");
    }

    private static void AppendRecommendedFastPreset(string path, IReadOnlyList<BenchmarkResult> results)
    {
        var codec = GetBest(results, GroupCodecPreset);
        var antiFlicker = GetBest(results, GroupAntiFlicker);
        var threads = GetBest(results, GroupUpscalerThreads);
        var tile = GetBest(results, GroupTileSize);

        AppendLine(path, LocalizedStrings.BenchmarkLogRecommendedFastPreset);
        if (codec is null || antiFlicker is null || threads is null || tile is null)
        {
            AppendLine(path, $"  {LocalizedStrings.BenchmarkNotAvailable}");
            return;
        }

        AppendLine(path, $"  {LocalizedStrings.BenchmarkLegendCodecPreset}: {codec.Codec} / {codec.Preset}");
        AppendLine(path, $"  {LocalizedStrings.BenchmarkLegendAntiFlicker}: {antiFlicker.Name}");
        AppendLine(path, $"  {LocalizedStrings.BenchmarkLegendThreads}: {threads.UpscalerThreads}");
        AppendLine(path, $"  {LocalizedStrings.BenchmarkLegendTileSize}: {tile.TileSize}");
    }

    private static BenchmarkResult? GetBest(IReadOnlyList<BenchmarkResult> results, string group)
        => results.Where(r => r.Group == group).OrderBy(r => r.Elapsed).FirstOrDefault();

    public static string BuildWeightedRecommendationText(IReadOnlyList<BenchmarkCaseResult> results)
    {
        var successful = results.Where(result => result.Success).ToList();
        var codec = successful.Where(result => result.Group == GroupCodecPreset).OrderBy(result => result.Elapsed).FirstOrDefault();
        var antiFlicker = successful.Where(result => result.Group == GroupAntiFlicker).OrderBy(result => result.Elapsed).FirstOrDefault();
        var threads = successful.Where(result => result.Group == GroupUpscalerThreads).OrderBy(result => result.Elapsed).FirstOrDefault();
        var tile = successful.Where(result => result.Group == GroupTileSize).OrderBy(result => result.Elapsed).FirstOrDefault();

        if (codec is null || antiFlicker is null || threads is null || tile is null)
        {
            return LocalizedStrings.BenchmarkNotAvailable;
        }

        return string.Join(Environment.NewLine, new[]
        {
            $"{LocalizedStrings.BenchmarkLegendCodecPreset}: {codec.Codec} / {codec.Preset}",
            $"{LocalizedStrings.BenchmarkLegendAntiFlicker}: {antiFlicker.Name}",
            $"{LocalizedStrings.BenchmarkLegendThreads}: {threads.UpscalerThreads}",
            $"{LocalizedStrings.BenchmarkLegendTileSize}: {tile.TileSize}",
        });
    }

    private static string GetLocalizedGroupName(string group)
        => group switch
        {
            GroupCodecPreset => LocalizedStrings.BenchmarkLegendCodecPreset,
            GroupAntiFlicker => LocalizedStrings.BenchmarkLegendAntiFlicker,
            GroupUpscalerThreads => LocalizedStrings.BenchmarkLegendThreads,
            GroupTileSize => LocalizedStrings.BenchmarkLegendTileSize,
            _ => group
        };

    private static string FormatResult(BenchmarkResult result)
    {
        var state = result.Success ? LocalizedStrings.BenchmarkSuccess : LocalizedStrings.BenchmarkFailure;
        return $"{state} | {result.Name} | {result.Elapsed.TotalSeconds:0.###} s | {result.OutputBytes / 1024d / 1024d:0.###} MB | CPU {MetricTriplet(result.Metrics.StartCpu, result.Metrics.AvgCpu, result.Metrics.PeakCpu)} | RAM {MetricTripletGiB(result.Metrics.StartRam, result.Metrics.AvgRam, result.Metrics.PeakRam)} GB | GPU {MetricTriplet(result.Metrics.StartGpu, result.Metrics.AvgGpu, result.Metrics.PeakGpu)} | VRAM {MetricTripletGiB(result.Metrics.StartVram, result.Metrics.AvgVram, result.Metrics.PeakVram)} GB | Mode {LocalizedContentMode(result.ContentMode)}";
    }

    private static string FormatSummary(BenchmarkResult? result)
    {
        if (result is null)
        {
            return LocalizedStrings.BenchmarkNotAvailable;
        }

        return $"{GetLocalizedGroupName(result.Group)} / {result.Name} ({result.Elapsed.TotalSeconds:0.###} s)";
    }

    private static string FormatSummaryWithTime(BenchmarkResult? result) => FormatSummary(result);

    private static string MetricValue(double? value) => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    private static string MetricTriplet(double? start, double? average, double? peak)
        => start.HasValue || average.HasValue || peak.HasValue
            ? $"{MetricValue(start)}/{MetricValue(average)}/{MetricValue(peak)}"
            : LocalizedStrings.BenchmarkNotAvailable;

    private static string MetricBytesGiBValue(double? bytes)
        => bytes.HasValue ? (bytes.Value / 1024d / 1024d / 1024d).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    private static string MetricTripletGiB(double? startBytes, double? averageBytes, double? peakBytes)
        => startBytes.HasValue || averageBytes.HasValue || peakBytes.HasValue
            ? $"{MetricBytesGiBValue(startBytes)}/{MetricBytesGiBValue(averageBytes)}/{MetricBytesGiBValue(peakBytes)}"
            : LocalizedStrings.BenchmarkNotAvailable;

    private static string MetricPairGiB(double? startBytes, double? peakBytes)
        => startBytes.HasValue || peakBytes.HasValue
            ? $"{MetricBytesGiBValue(startBytes)}/{MetricBytesGiBValue(peakBytes)}"
            : LocalizedStrings.BenchmarkNotAvailable;

    private static void AppendMetricsSummary(string path, IReadOnlyList<BenchmarkResult> results)
    {
        var metrics = CollectMetrics(results);
        AppendLine(path, LocalizedStrings.BenchmarkLogMetricsSummary);
        AppendLine(path, $"  CPU start/avg/peak: {MetricTriplet(metrics.StartCpu, metrics.AvgCpu, metrics.PeakCpu)}");
        AppendLine(path, $"  RAM start/avg/peak: {MetricTripletGiB(metrics.StartRam, metrics.AvgRam, metrics.PeakRam)} GB");
        AppendLine(path, $"  RAM quick: {MetricPairGiB(metrics.StartRam, metrics.PeakRam)} GB");
        AppendLine(path, $"  GPU start/avg/peak: {MetricTriplet(metrics.StartGpu, metrics.AvgGpu, metrics.PeakGpu)}");
        AppendLine(path, $"  VRAM start/avg/peak: {MetricTripletGiB(metrics.StartVram, metrics.AvgVram, metrics.PeakVram)} GB");
        AppendLine(path, $"  VRAM quick: {MetricPairGiB(metrics.StartVram, metrics.PeakVram)} GB");
    }

    private static void WriteConsoleProgress(string line, bool complete)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(line);
            return;
        }

        var fitted = FitConsoleLine(line);
        Console.Write("\r");
        Console.Write(fitted.PadRight(GetConsoleLineWidth()));
        Console.Out.Flush();
        if (complete)
        {
            Console.WriteLine();
            Console.Out.Flush();
        }
    }

    private static string FormatConsoleProgress(int stepIndex, int totalSteps, string caseName, PipelineProgress progress, BenchmarkMetricsSnapshot? liveMetrics)
    {
        var ram = liveMetrics?.Ram.HasValue == true ? $"{MetricBytesGiBValue(liveMetrics.Ram)} GB" : "-- GB";
        var vram = liveMetrics?.Vram.HasValue == true ? $"{MetricBytesGiBValue(liveMetrics.Vram)} GB" : "-- GB";
        return $"[{stepIndex}/{totalSteps}] {caseName} | {progress.ProgressText} | ETA {progress.EtaText} | RAM {ram} | VRAM {vram}";
    }

    private static string FitConsoleLine(string line)
    {
        var width = GetConsoleLineWidth();
        if (line.Length <= width)
        {
            return line;
        }

        return width > 1 ? line[..(width - 1)] + "..." : line[..width];
    }

    private static int GetConsoleLineWidth()
    {
        try
        {
            return Math.Max(40, Console.WindowWidth - 1);
        }
        catch
        {
            return 160;
        }
    }

    private static BenchmarkMetricsSummary CollectMetrics(IReadOnlyList<BenchmarkResult> results)
    {
        double? Avg(Func<BenchmarkMetricsSummary, double?> selector)
        {
            var values = results.Select(r => selector(r.Metrics)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return values.Length == 0 ? null : values.Average();
        }

        double? Peak(Func<BenchmarkMetricsSummary, double?> selector)
        {
            var values = results.Select(r => selector(r.Metrics)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return values.Length == 0 ? null : values.Max();
        }

        double? Start(Func<BenchmarkMetricsSummary, double?> selector)
        {
            var values = results.Select(r => selector(r.Metrics)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return values.Length == 0 ? null : values.Average();
        }

        return new BenchmarkMetricsSummary(
            Start(m => m.StartCpu),
            Avg(m => m.AvgCpu),
            Peak(m => m.PeakCpu),
            Start(m => m.StartRam),
            Avg(m => m.AvgRam),
            Peak(m => m.PeakRam),
            Start(m => m.StartGpu),
            Avg(m => m.AvgGpu),
            Peak(m => m.PeakGpu),
            Start(m => m.StartVram),
            Avg(m => m.AvgVram),
            Peak(m => m.PeakVram));
    }

    private static void AppendLine(string path, string text)
    {
        File.AppendAllText(path, text + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string EscapeMd(string value) => value.Replace("|", "\\|");

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mkv" or ".mp4" or ".mov" or ".m4v" or ".avi" or ".webm" or ".ts" or ".m2ts" or ".flv" or ".wmv";
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "realesrgan-ncnn-vulkan-20220424")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string FindRepoRoot() => ResolveRepoRoot();

    private static string FindFile(string fileName, string fallback, string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(repoRoot, fileName),
            fallback
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Unable to find {fileName}");
    }

    private static string FindDirectory(string name, string fallback, string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(repoRoot, name),
            fallback
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"Unable to find {name}");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "benchmark" : cleaned;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}

internal sealed class BenchmarkMetricsSampler
{
    private readonly List<BenchmarkMetricsSnapshot> _samples = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string? _nvidiaSmiPath;
    private Task? _systemTask;
    private Task? _gpuTask;
    private Process? _gpuProcess;
    private volatile BenchmarkMetricsSnapshot? _latestGpu;
    private bool _haveCpuBaseline;
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;

    public BenchmarkMetricsSampler()
    {
        _nvidiaSmiPath = FindNvidiaSmiPath();
    }

    public void Start()
    {
        _systemTask = Task.Run(() => RunSystemSamplerAsync(_cts.Token));
        if (!string.IsNullOrWhiteSpace(_nvidiaSmiPath))
        {
            _gpuTask = Task.Run(() => RunGpuSamplerAsync(_cts.Token));
        }
    }

    public BenchmarkMetricsSnapshot? GetLatestSnapshot()
    {
        lock (_gate)
        {
            return _samples.Count > 0 ? _samples[^1] : null;
        }
    }

    public async Task<BenchmarkMetricsSummary> StopAsync()
    {
        _cts.Cancel();
        TryKillTree(_gpuProcess);

        try
        {
            if (_systemTask is not null)
            {
                await _systemTask.ConfigureAwait(false);
            }

            if (_gpuTask is not null)
            {
                await _gpuTask.ConfigureAwait(false);
            }
        }
        catch
        {
        }

        lock (_gate)
        {
            if (_samples.Count == 0)
            {
                return BenchmarkMetricsSummary.Empty;
            }

            BenchmarkMetricsSnapshot? First(Func<BenchmarkMetricsSnapshot, double?> selector)
            {
                return _samples.FirstOrDefault(sample => selector(sample).HasValue);
            }

            double? Avg(Func<BenchmarkMetricsSnapshot, double?> selector)
            {
                var values = _samples.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                return values.Length == 0 ? null : values.Average();
            }

            double? Peak(Func<BenchmarkMetricsSnapshot, double?> selector)
            {
                var values = _samples.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                return values.Length == 0 ? null : values.Max();
            }

            return new BenchmarkMetricsSummary(
                First(s => s.Cpu)?.Cpu,
                Avg(s => s.Cpu),
                Peak(s => s.Cpu),
                First(s => s.Ram)?.Ram,
                Avg(s => s.Ram),
                Peak(s => s.Ram),
                First(s => s.Gpu)?.Gpu,
                Avg(s => s.Gpu),
                Peak(s => s.Gpu),
                First(s => s.Vram)?.Vram,
                Avg(s => s.Vram),
                Peak(s => s.Vram));
        }
    }

    private async Task RunSystemSamplerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cpu = SampleCpuPercent();
            var ram = SampleRamBytes();
            var gpu = _latestGpu;
            lock (_gate)
            {
                _samples.Add(new BenchmarkMetricsSnapshot(cpu, ram, gpu?.Gpu, gpu?.Vram));
            }

            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunGpuSamplerAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_nvidiaSmiPath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _nvidiaSmiPath,
            Arguments = "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits -l 1 -i 0",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _gpuProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_gpuProcess.Start())
        {
            return;
        }

        using var registration = ct.Register(() => TryKillTree(_gpuProcess));
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _gpuProcess.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var snapshot = ParseGpuLine(line);
                if (snapshot is not null)
                {
                    _latestGpu = snapshot;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            TryKillTree(_gpuProcess);
        }
    }

    private static BenchmarkMetricsSnapshot? ParseGpuLine(string line)
    {
        var tokens = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return null;
        }

        if (!TryParseDouble(tokens[0], out var gpu) ||
            !TryParseDouble(tokens[1], out var vramUsed) ||
            !TryParseDouble(tokens[2], out var vramTotal) ||
            vramTotal <= 0)
        {
            return null;
        }

        var vramBytes = vramUsed * 1024d * 1024d;
        return new BenchmarkMetricsSnapshot(null, null, gpu, vramBytes);
    }

    private double? SampleCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var idleValue = FileTimeToUInt64(idle);
        var kernelValue = FileTimeToUInt64(kernel);
        var userValue = FileTimeToUInt64(user);

        if (!_haveCpuBaseline)
        {
            _haveCpuBaseline = true;
            _prevIdle = idleValue;
            _prevKernel = kernelValue;
            _prevUser = userValue;
            return null;
        }

        var idleDelta = idleValue - _prevIdle;
        var kernelDelta = kernelValue - _prevKernel;
        var userDelta = userValue - _prevUser;
        var total = kernelDelta + userDelta;
        _prevIdle = idleValue;
        _prevKernel = kernelValue;
        _prevUser = userValue;

        if (total == 0)
        {
            return null;
        }

        var busy = total - idleDelta;
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }

    private static double? SampleRamBytes()
    {
        var state = new MEMORYSTATUSEX();
        state.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        if (!GlobalMemoryStatusEx(ref state) || state.ullTotalPhys == 0)
        {
            return null;
        }

        return state.ullTotalPhys - state.ullAvailPhys;
    }

    private static string? FindNvidiaSmiPath()
    {
        var candidates = new[]
        {
            "nvidia-smi.exe",
            Path.Combine(AppContext.BaseDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), "nvidia-smi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryParseDouble(string value, out double parsed) => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static ulong FileTimeToUInt64(FILETIME time) => ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    private static void TryKillTree(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

internal sealed record BenchmarkMetricsSnapshot(
    double? Cpu,
    double? Ram,
    double? Gpu,
    double? Vram);
