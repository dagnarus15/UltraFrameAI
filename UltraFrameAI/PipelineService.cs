using System.Diagnostics;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class PipelineService
{
    private static readonly IFramePipelineBridge FrameBridge = new ProcessFramePipelineBridge();

    private sealed record SourceMetadata(double Duration, int Width, int Height, double Fps);
    private sealed record SourceMetadataCacheEntry(double Duration, int Width, int Height, double Fps, bool Complete);
    private sealed record TimestampCacheEntry(double[] Timestamps, bool Complete);
    private sealed record ResumeStateEntry(
        int Version,
        string SourcePath,
        string OutputPath,
        string Codec,
        int TargetHeight,
        string UpscalerBackend,
        string RefinerBackend,
        int ProcessedFrames,
        int TotalFrames,
        string Stage,
        bool Complete,
        bool CanResume,
        DateTime UpdatedUtc);
    private sealed record FrameWriteItem(byte[] Buffer, int Length, double? TimestampSeconds);
    private const int RawFrameQueueCapacity = 8;
    private const int EncodeFrameQueueCapacity = 4;

    private static readonly Regex DurationRegex = new(
        @"Duration:\s*(?<hh>\d+):(?<mm>\d+):(?<ss>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VideoSizeRegex = new(
        @"Video:.*?(?<width>\d+)x(?<height>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FpsRegex = new(
        @"(?<fps>\d+(?:\.\d+)?(?:/\d+(?:\.\d+)?)?)\s?fps\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed class TimestampRepairState
    {
        private readonly Queue<double> _recentDeltas = new();
        private readonly int _windowSize;
        private double? _lastTimestamp;

        public TimestampRepairState(int windowSize = 8)
        {
            _windowSize = Math.Max(4, windowSize);
        }

        public int RepairCount { get; private set; }

        public bool TryRepair(double rawTimestamp, out double repairedTimestamp)
        {
            repairedTimestamp = rawTimestamp;
            if (_lastTimestamp is null)
            {
                _lastTimestamp = rawTimestamp;
                return false;
            }

            var previous = _lastTimestamp.Value;
            var delta = rawTimestamp - previous;
            var expectedDelta = GetExpectedDelta(delta);
            var shouldRepair = delta <= 0
                || (!double.IsNaN(expectedDelta)
                    && expectedDelta > 0
                    && delta > Math.Max(expectedDelta * 6.0, expectedDelta + 0.5));

            if (shouldRepair)
            {
                repairedTimestamp = previous + expectedDelta;
                delta = repairedTimestamp - previous;
                RepairCount++;
            }

            RememberDelta(delta, expectedDelta);
            _lastTimestamp = repairedTimestamp;
            return shouldRepair;
        }

        private double GetExpectedDelta(double fallbackDelta)
        {
            if (_recentDeltas.Count == 0)
            {
                return fallbackDelta > 0 ? fallbackDelta : 1.0 / 24.0;
            }

            var ordered = _recentDeltas.OrderBy(x => x).ToArray();
            return ordered[ordered.Length / 2];
        }

        private void RememberDelta(double delta, double expectedDelta)
        {
            if (delta <= 0 || double.IsNaN(delta) || double.IsInfinity(delta))
            {
                return;
            }

            if (_recentDeltas.Count > 0
                && expectedDelta > 0
                && delta > Math.Max(expectedDelta * 3.0, expectedDelta + 0.12))
            {
                return;
            }

            _recentDeltas.Enqueue(delta);
            while (_recentDeltas.Count > _windowSize)
            {
                _recentDeltas.Dequeue();
            }
        }
    }

    private sealed class PipeEtaEstimator
    {
        private readonly Queue<double> _secondsPerFrameSamples = new();
        private readonly int _windowSize;
        private readonly double _tailMultiplier;
        private double _lastCurrent;
        private TimeSpan _lastElapsed;
        private bool _hasSample;

        public PipeEtaEstimator(int windowSize = 6, double tailMultiplier = 1.0)
        {
            _windowSize = Math.Max(2, windowSize);
            _tailMultiplier = Math.Max(0.5, tailMultiplier);
        }

        public string Estimate(double current, double total, TimeSpan elapsed, TimeSpan stageElapsed, bool draining = false)
        {
            if (current < 0 || total <= 0)
            {
                return "--:--:--";
            }

            current = Math.Min(current, total);

            if (_hasSample)
            {
                var deltaCurrent = current - _lastCurrent;
                var deltaSeconds = (elapsed - _lastElapsed).TotalSeconds;
                if (deltaCurrent > 0 && deltaSeconds > 0)
                {
                    EnqueueSecondsPerFrame(deltaSeconds / deltaCurrent);
                }
            }
            else
            {
                _hasSample = true;
            }

            _lastCurrent = current;
            _lastElapsed = elapsed;

            var secondsPerFrame = _secondsPerFrameSamples.Count > 0
                ? AverageSecondsPerFrame()
                : (current > 0 ? elapsed.TotalSeconds / current : 0);

            if (secondsPerFrame <= 0)
            {
                return "--:--:--";
            }

            var remainingFrames = Math.Max(0, total - current);
            var frameSeconds = remainingFrames * secondsPerFrame;
            var progress = total > 0 ? current / total : 0;
            var tailBlend = draining ? 1.0 : Math.Clamp((progress - 0.72) / 0.28, 0.0, 1.0);
            var tailSeconds = Math.Max(3.0, Math.Min(stageElapsed.TotalSeconds * 0.14 * _tailMultiplier, 24.0 * _tailMultiplier));
            tailSeconds = Math.Max(tailSeconds, secondsPerFrame * 5.0);
            var etaSeconds = frameSeconds + tailBlend * tailSeconds;

            if (draining)
            {
                etaSeconds = Math.Max(etaSeconds * (1.06 * _tailMultiplier), Math.Min(stageElapsed.TotalSeconds * 0.18 * _tailMultiplier, 24.0 * _tailMultiplier));
            }

            return Time(TimeSpan.FromSeconds(Math.Max(0, etaSeconds)));
        }

        private void EnqueueSecondsPerFrame(double secondsPerFrame)
        {
            if (secondsPerFrame <= 0)
            {
                return;
            }

            _secondsPerFrameSamples.Enqueue(secondsPerFrame);
            while (_secondsPerFrameSamples.Count > _windowSize)
            {
                _secondsPerFrameSamples.Dequeue();
            }
        }

        private double AverageSecondsPerFrame()
        {
            var sum = 0.0;
            foreach (var sample in _secondsPerFrameSamples)
            {
                sum += sample;
            }

            return sum / _secondsPerFrameSamples.Count;
        }
    }

    private sealed class ProcessLease : IDisposable
    {
        private readonly CancellationTokenRegistration _registration;

        public ProcessLease(Process process, Task stderrPump, CancellationTokenRegistration registration)
        {
            Process = process;
            StderrPump = stderrPump;
            _registration = registration;
        }

        public Process Process { get; }

        public Task StderrPump { get; }

        public void Dispose()
        {
            _registration.Dispose();
            try
            {
                if (!Process.HasExited)
                {
                    TryKillTree(Process);
                }
            }
            catch
            {
            }

            Process.Dispose();
        }
    }

    public PipelineService()
    {
    }

    public async Task RunAsync(
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        Action<PipelineProgress> report,
        CancellationToken cancellationToken)
        => await RunAsync(items, options, report, null, cancellationToken).ConfigureAwait(false);

    public async Task RunAsync(
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        Action<PipelineProgress> report,
        Action<RenderPreviewFrameUpdate>? previewReport,
        CancellationToken cancellationToken)
    {
        await RunPipeAsync(items, options, report, previewReport, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunPipeAsync(
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        Action<PipelineProgress> report,
        Action<RenderPreviewFrameUpdate>? previewReport,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            report(new PipelineProgress(0, 0, string.Empty, LocalizedStrings.LogIdle, 0, "0%", "--:--:--", "--:--:--", LocalizedStrings.LogNoItemsFound, string.Empty, LocalizedStrings.LogReady, StageElapsedText: "--:--:--"));
            return;
        }

        var total = items.Count;
        var overall = Stopwatch.StartNew();
        Directory.CreateDirectory(options.OutputFolder);

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            var itemWatch = Stopwatch.StartNew();
            var resumeStatePath = GetResumeStatePath(item.OutputPath);
            var resumeCodec = options.UseX265 ? "libx265" : "libx264";
            var resumeTargetHeight = options.UseX265 ? 2160 : 1080;
            var resumeTotalFrames = 0;
            var resumeProcessedFrames = 0;
            var isResumeRun = item.ResumeRequested && item.ResumeProcessedFrames > 0 && File.Exists(item.OutputPath);
            var resumeStartFrame = isResumeRun ? item.ResumeProcessedFrames : 0;
            var resumeWorkingDirectory = isResumeRun
                ? Path.Combine(Path.GetTempPath(), "UltraFrameAI", "resume-stage", Guid.NewGuid().ToString("N"))
                : string.Empty;
            var resumeContinuationOutputPath = isResumeRun ? Path.Combine(resumeWorkingDirectory, "continuation.mkv") : string.Empty;
            var resumePartialVideoPath = isResumeRun ? Path.Combine(resumeWorkingDirectory, "partial-video.mkv") : string.Empty;
            var resumeMergedVideoPath = isResumeRun ? Path.Combine(resumeWorkingDirectory, "merged-video.mkv") : string.Empty;
            var resumeMuxedOutputPath = isResumeRun ? Path.Combine(resumeWorkingDirectory, "final-output.mkv") : string.Empty;
            try
            {
                if (isResumeRun)
                {
                    Directory.CreateDirectory(resumeWorkingDirectory);
                }

                if (item.SkipRequested)
                {
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogSkippingEncode, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                    continue;
                }

                var perfRoot = ResolveRepoRoot();
                var perfLog = Path.Combine(
                    perfRoot,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.perf.log");
                var startupLog = Path.Combine(
                    perfRoot,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.startup.log");
                var perfLogExe = Path.Combine(
                    AppContext.BaseDirectory,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.perf.log");
                var perfLogFinal = Path.Combine(
                    options.OutputFolder,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.perf.log");

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", "00:00:00", "--:--:--", LocalizedStrings.LogPreparing, item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogStartingItem(item.Title), StageElapsedText: Time(itemWatch.Elapsed)));
                WriteStartupLog($"Preparing {Path.GetFileName(item.SourcePath)}");

                var metadataCacheHit = TryLoadSourceMetadataCache(item.SourcePath, out var cachedMetadata);
                var metadata = metadataCacheHit
                    ? cachedMetadata
                    : await GetSourceMetadataAsync(options.FfmpegPath, item.SourcePath, cancellationToken).ConfigureAwait(false);
                if (!metadataCacheHit)
                {
                    SaveSourceMetadataCache(item.SourcePath, metadata);
                }

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.Get("LogCheckingCache"), item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.Get("LogCheckingCache"), StageElapsedText: Time(itemWatch.Elapsed)));
                var estimatedFrames = EstimateFrameCount(metadata.Duration, metadata.Fps);
                var codec = resumeCodec;
                var crf = options.UseX265 ? 18 : 16;
                var height = resumeTargetHeight;
                var subtitle = Path.Combine(Path.GetDirectoryName(item.SourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(item.SourcePath) + ".RG Genshiken.ass");

                if (metadata.Width <= 0 || metadata.Height <= 0)
                {
                    throw new InvalidOperationException(LocalizedStrings.LogInvalidVideoInfo);
                }

                var timestampCacheHit = TryLoadTimestampCache(item.SourcePath, out var cachedTimestamps);
                if (timestampCacheHit)
                {
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.Get("LogTimestampCacheLoaded"), item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.Get("LogTimestampCacheLoaded"), StageElapsedText: Time(itemWatch.Elapsed)));
                }
                var upscaleScale = 2;
                var rawWidth = Math.Max(1, metadata.Width);
                var rawHeight = Math.Max(1, metadata.Height);
                var upWidth = checked(rawWidth * upscaleScale);
                var upHeight = checked(rawHeight * upscaleScale);
                var totalFrames = timestampCacheHit && cachedTimestamps.Length > 0
                    ? cachedTimestamps.Length
                    : Math.Max(estimatedFrames, 1);
                resumeTotalFrames = totalFrames;
                var upscaleFrameBudget = Math.Max(0, totalFrames - resumeStartFrame);
                WriteResumeState(
                    resumeStatePath,
                    new ResumeStateEntry(
                        1,
                        item.SourcePath,
                        item.OutputPath,
                        codec,
                        height,
                        options.UpscalerBackend.ToString(),
                        options.RefinerBackend.ToString(),
                        0,
                        totalFrames,
                        "Preparing",
                        Complete: false,
                        CanResume: true,
                        DateTime.UtcNow));
                var stageWatch = Stopwatch.StartNew();
                var etaEstimator = new PipeEtaEstimator();
                WriteStartupLog($"Video size ready: {rawWidth}x{rawHeight}");

                var overwriteAllowed = options.Overwrite || item.ForceOverwrite;
                if (File.Exists(item.OutputPath) && !overwriteAllowed)
                {
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogOutputExists, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                    continue;
                }

                stageWatch.Restart();

                var decodeLastError = string.Empty;
                var upscaleLastError = string.Empty;
                var encodeLastError = string.Empty;
                var decodeStartupStderrCount = 0;
                var upscaleStartupStderrCount = 0;
                var timestampBridge = timestampCacheHit ? null : new TimestampStreamBridge(Math.Max(totalFrames, 1));
                var cachedTimestampIndex = resumeStartFrame;
                var timestampRepairState = options.RepairBrokenTimestamps ? new TimestampRepairState() : null;
                var emittedTimestamps = new List<double>(Math.Max(totalFrames, 1));
                var timestampRepairLogged = false;
                double? latestFrameTimestampSeconds = null;

                async ValueTask<double?> GetTimestampForFrameAsync(CancellationToken ct)
                {
                    if (timestampCacheHit && cachedTimestamps.Length > 0)
                    {
                        if (cachedTimestampIndex < cachedTimestamps.Length)
                        {
                            latestFrameTimestampSeconds = cachedTimestamps[cachedTimestampIndex++];
                            return latestFrameTimestampSeconds;
                        }

                        latestFrameTimestampSeconds = cachedTimestamps[^1];
                        return latestFrameTimestampSeconds;
                    }

                    if (timestampBridge is not null)
                    {
                        latestFrameTimestampSeconds = await timestampBridge.DequeueAsync(ct).ConfigureAwait(false);
                        return latestFrameTimestampSeconds;
                    }

                    return null;
                }

                var decodeWorkingDirectory = Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory;
                var decodeArguments = FrameBridge.BuildDecodeArguments(item.SourcePath, !timestampCacheHit, resumeStartFrame);
                WriteStartupLog($"Decode exe: {options.FfmpegPath}");
                WriteStartupLog($"Decode cwd: {decodeWorkingDirectory}");
                using var decode = StartProcess(
                    options.FfmpegPath,
                    decodeArguments,
                    decodeWorkingDirectory,
                    cancellationToken,
                    line =>
                    {
                        if (timestampBridge is not null && timestampBridge.TryCaptureFromShowInfo(line))
                        {
                            return;
                        }

                        if (LooksLikeProcessError(line))
                        {
                            decodeLastError = line;
                        }

                        if (decodeStartupStderrCount < 16)
                        {
                            decodeStartupStderrCount++;
                            WriteStartupLog($"Decode stderr: {line}");
                        }
                    },
                    out var decodeStderr);
                var timestampCompletion = timestampBridge is null
                    ? Task.CompletedTask
                    : Task.Run(async () =>
                    {
                        try
                        {
                            await decodeStderr.ConfigureAwait(false);
                        }
                        finally
                        {
                            timestampBridge.Complete();
                        }
                    });
                WriteStartupLog("Starting decode");
                var upscaleWorkingDirectory = string.IsNullOrWhiteSpace(options.UpscalerWorkingDirectory)
                    ? (Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory)
                    : options.UpscalerWorkingDirectory;
                var upscaleArguments = FrameBridge.BuildUpscaleArguments(rawWidth, rawHeight, upscaleFrameBudget, options.UpscalerBackend, options.ModelDir, options.UpscalerThreads, options.TileSize, options.GpuId, options.ExternalUpscalerArgumentsTemplate);
                WriteStartupLog($"Upscaler backend: {options.UpscalerBackend}");
                WriteStartupLog($"Upscaler exe: {options.UpscalerPath}");
                WriteStartupLog($"Upscaler cwd: {upscaleWorkingDirectory}");
                WriteStartupLog($"Upscaler model dir: {options.ModelDir}");
                WriteStartupLog($"Upscaler args: {upscaleArguments}");
                using var upscale = StartProcess(
                    options.UpscalerPath,
                    upscaleArguments,
                    upscaleWorkingDirectory,
                    cancellationToken,
                    line =>
                    {
                        upscaleLastError = line;
                        if (upscaleStartupStderrCount < 24)
                        {
                            upscaleStartupStderrCount++;
                            WriteStartupLog($"Upscaler stderr: {line}");
                        }
                    },
                    out var upscaleStderr);
                WriteStartupLog("Starting upscaler");

                ProcessLease? refiner = null;
                Task refinerStderr = Task.CompletedTask;
                string refinerLastError = string.Empty;
                var refinerStartupStderrCount = 0;
                Stream? refinerIn = null;
                Stream? refinerOut = null;
                if (options.RefinerBackend != RefinerBackendKind.None)
                {
                    var refinerWorkingDirectory = string.IsNullOrWhiteSpace(options.RefinerWorkingDirectory)
                        ? (Path.GetDirectoryName(options.RefinerPath) ?? upscaleWorkingDirectory)
                        : options.RefinerWorkingDirectory;
                    var refinerArguments = FrameBridge.BuildUpscaleArguments(
                        upWidth,
                        upHeight,
                        upscaleFrameBudget,
                        options.RefinerBackend == RefinerBackendKind.StableSrExternal
                            ? UpscalerBackendKind.StableSrExternal
                            : UpscalerBackendKind.SupirExternal,
                        options.RefinerModelDir,
                        options.UpscalerThreads,
                        options.TileSize,
                        options.GpuId,
                        options.RefinerArgumentsTemplate);
                    WriteStartupLog($"Refiner backend: {options.RefinerBackend}");
                    WriteStartupLog($"Refiner exe: {options.RefinerPath}");
                    WriteStartupLog($"Refiner cwd: {refinerWorkingDirectory}");
                    WriteStartupLog($"Refiner model dir: {options.RefinerModelDir}");
                    WriteStartupLog($"Refiner args: {refinerArguments}");
                    refiner = StartProcess(
                        options.RefinerPath,
                        refinerArguments,
                        refinerWorkingDirectory,
                        cancellationToken,
                        line =>
                        {
                            refinerLastError = line;
                            if (refinerStartupStderrCount < 24)
                            {
                                refinerStartupStderrCount++;
                                WriteStartupLog($"Refiner stderr: {line}");
                            }
                        },
                        out refinerStderr);
                    refinerIn = refiner.Process.StandardInput.BaseStream;
                    refinerOut = refiner.Process.StandardOutput.BaseStream;
                    WriteStartupLog("Starting refiner");
                }

                var decodeOut = decode.Process.StandardOutput.BaseStream;
                var upscaleIn = upscale.Process.StandardInput.BaseStream;
                var upscaleOut = upscale.Process.StandardOutput.BaseStream;
                var encodeFps = metadata.Duration > 0 && totalFrames > 0
                    ? Math.Max(1.0, totalFrames / metadata.Duration)
                    : (metadata.Fps > 0 ? metadata.Fps : 25.0);

                var hasSub = File.Exists(subtitle);
                var outputContainer = "mkv";
                var encoderBridge = FrameEncoderBridgeFactory.CreateDefault(options.UseNativeEncoderBackend);
                if (options.UseNativeEncoderBackend && encoderBridge is NativeFrameEncoderBridge)
                {
                    if (!NativeFrameEncoderBridge.IsAvailable())
                    {
                        WriteStartupLog("Native encoder backend unavailable; using subprocess fallback.");
                    }
                    else if (!string.Equals(outputContainer, "mkv", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStartupLog($"Native encoder backend disabled for container '{outputContainer}'; using subprocess fallback.");
                    }
                    else
                    {
                        WriteStartupLog($"Native encoder backend requested for codec '{codec}'.");
                    }
                }

                var encodeWatch = Stopwatch.StartNew();
                var encodeSessionConfig = new FrameEncoderSessionConfig(
                    upWidth,
                    upHeight,
                    encodeFps,
                    isResumeRun ? string.Empty : item.SourcePath,
                    isResumeRun ? string.Empty : subtitle,
                    isResumeRun ? false : hasSub,
                    codec,
                    options.EncoderPreset,
                    crf,
                    outputContainer,
                    height,
                    isResumeRun ? resumeContinuationOutputPath : item.OutputPath,
                    options.FfmpegPath);
                using var encodeSession = encoderBridge.CreateSession(
                    encodeSessionConfig,
                    cancellationToken,
                    line => encodeLastError = line);
                WriteStartupLog($"Encoder session: {encodeSession.GetType().Name}");
                var encoderSupportsPerFrameTimestamps = encodeSession.SupportsPerFrameTimestamps;
                await encodeSession.OpenAsync(cancellationToken).ConfigureAwait(false);
                WriteStartupLog("Starting encoder");

                var inputFrameBytes = rawWidth * rawHeight * 3;
                var outputFrameBytes = upWidth * upHeight * 3;
                var antiFlicker = options.UseAntiFlicker && options.AntiFlickerStrength > 0
                    ? AntiFlickerProcessor.TryCreate(upWidth, upHeight, 3, options.AntiFlickerMode, options.ContentMode, options.AntiFlickerStrength)
                    : null;
                var currentFrames = resumeStartFrame;
                var lastTick = Stopwatch.StartNew();
                var previewUpdateWatch = Stopwatch.StartNew();
                var draining = false;
                var previewEnabled = previewReport is not null;
                var frameLoopWatch = Stopwatch.StartNew();
                long decodeReadTicks = 0;
                var upscaleWriteElapsed = TimeSpan.Zero;
                var upscaleReadElapsed = TimeSpan.Zero;
                var frameSendElapsed = TimeSpan.Zero;
                var timestampSubmitElapsed = TimeSpan.Zero;
                long rawQueueCurrent = 0;
                long rawQueueMax = 0;
                long rawQueueWriteWaitTicks = 0;
                long encodeQueueCurrent = 0;
                long encodeQueueMax = 0;
                long encodeQueueWriteWaitTicks = 0;
                var perfCopyWatch = Stopwatch.StartNew();
                var rawFrameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(RawFrameQueueCapacity)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
                var encodeFrameChannel = Channel.CreateBounded<FrameWriteItem>(new BoundedChannelOptions(EncodeFrameQueueCapacity)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
                using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var pipelineToken = pipelineCts.Token;
                var decodeProducer = Task.Run(async () =>
                {
                    byte[]? currentBuffer = null;
                    try
                    {
                        currentBuffer = ArrayPool<byte>.Shared.Rent(inputFrameBytes);
                        while (true)
                        {
                            var readStart = Stopwatch.GetTimestamp();
                            var hasFrame = await TryReadExactAsync(decodeOut, currentBuffer.AsMemory(0, inputFrameBytes), pipelineToken).ConfigureAwait(false);
                            Interlocked.Add(ref decodeReadTicks, Stopwatch.GetElapsedTime(readStart).Ticks);
                            if (!hasFrame)
                            {
                                break;
                            }

                            var nextBuffer = ArrayPool<byte>.Shared.Rent(inputFrameBytes);
                            var writeWaitStart = Stopwatch.GetTimestamp();
                            await rawFrameChannel.Writer.WriteAsync(currentBuffer, pipelineToken).ConfigureAwait(false);
                            Interlocked.Add(ref rawQueueWriteWaitTicks, Stopwatch.GetElapsedTime(writeWaitStart).Ticks);
                            UpdateMax(ref rawQueueMax, Interlocked.Increment(ref rawQueueCurrent));
                            currentBuffer = nextBuffer;
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        rawFrameChannel.Writer.TryComplete();
                    }
                    catch (Exception)
                    {
                        rawFrameChannel.Writer.TryComplete();
                    }
                    finally
                    {
                        if (currentBuffer is not null)
                        {
                            ArrayPool<byte>.Shared.Return(currentBuffer);
                        }

                        rawFrameChannel.Writer.TryComplete();
                    }
                }, pipelineToken);
                var encodeWriter = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var itemToWrite in encodeFrameChannel.Reader.ReadAllAsync(pipelineToken).ConfigureAwait(false))
                        {
                            Interlocked.Decrement(ref encodeQueueCurrent);
                            var writeStart = Stopwatch.GetTimestamp();
                            try
                            {
                                if (itemToWrite.TimestampSeconds is not null)
                                {
                                    var timestampStart = Stopwatch.GetTimestamp();
                                    await encodeSession.SubmitTimestampAsync(itemToWrite.TimestampSeconds.Value, pipelineToken).ConfigureAwait(false);
                                    timestampSubmitElapsed += Stopwatch.GetElapsedTime(timestampStart);
                                }

                                await encodeSession.WriteFrameAsync(itemToWrite.Buffer.AsMemory(0, itemToWrite.Length), pipelineToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                frameSendElapsed += Stopwatch.GetElapsedTime(writeStart);
                                ArrayPool<byte>.Shared.Return(itemToWrite.Buffer);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (pipelineToken.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        encodeFrameChannel.Writer.TryComplete();
                    }
                }, pipelineToken);
                using var firstFrameHeartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken);
                var firstFrameHeartbeat = Task.Run(async () =>
                {
                    try
                    {
                        while (!firstFrameHeartbeatCts.Token.IsCancellationRequested && currentFrames == 0)
                        {
                            await Task.Delay(1000, firstFrameHeartbeatCts.Token).ConfigureAwait(false);
                            if (currentFrames == 0)
                            {
                                UpdateProgress(heartbeat: true);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, firstFrameHeartbeatCts.Token);

                void WriteStartupLog(string message)
                {
                    try
                    {
                        File.AppendAllText(
                            startupLog,
                            $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}",
                            new UTF8Encoding(false));
                    }
                    catch
                    {
                    }
                }

                void WritePerfSnapshot(TimeSpan encodeElapsed, bool final = false)
                {
                    WritePerfLog(
                        perfLog,
                        itemWatch.Elapsed - stageWatch.Elapsed,
                        frameLoopWatch.Elapsed,
                        encodeElapsed,
                        TimeSpan.FromTicks(Interlocked.Read(ref decodeReadTicks)),
                        upscaleWriteElapsed,
                        upscaleReadElapsed,
                        frameSendElapsed,
                        timestampSubmitElapsed,
                        currentFrames,
                        totalFrames,
                        stageWatch.Elapsed,
                        Interlocked.Read(ref rawQueueCurrent),
                        Interlocked.Read(ref rawQueueMax),
                        TimeSpan.FromTicks(Interlocked.Read(ref rawQueueWriteWaitTicks)),
                        Interlocked.Read(ref encodeQueueCurrent),
                        Interlocked.Read(ref encodeQueueMax),
                        TimeSpan.FromTicks(Interlocked.Read(ref encodeQueueWriteWaitTicks)),
                        final);
                    if (final || perfCopyWatch.ElapsedMilliseconds >= 5000)
                    {
                        TryCopyPerfLog(perfLog, perfLogExe);
                        perfCopyWatch.Restart();
                    }
                }

                void UpdateProgress(bool heartbeat = false)
                {
                    var rawPct = totalFrames <= 0 ? 0 : currentFrames * 100.0 / totalFrames;
                    var pct = draining && rawPct >= 100 ? 99.9 : Math.Min(99.9, rawPct);
                    var phase = currentFrames == 0
                        ? LocalizedStrings.LogExtractingFrames
                        : draining || currentFrames >= totalFrames
                            ? LocalizedStrings.LogEncodingVideo
                            : currentFrames < totalFrames
                            ? LocalizedStrings.LogUpscalingFrames
                            : LocalizedStrings.LogEncodingVideo;
                    var currentText = totalFrames > 0 ? $"{Math.Min(currentFrames, totalFrames)}/{totalFrames}" : phase;
                    var processingFps = stageWatch.Elapsed.TotalSeconds > 0
                        ? currentFrames / stageWatch.Elapsed.TotalSeconds
                        : 0;
                    var processingFpsText = processingFps > 0
                        ? $"{processingFps:0.0} FPS"
                        : "--";
                    WriteResumeState(
                        resumeStatePath,
                        new ResumeStateEntry(
                            1,
                            item.SourcePath,
                            item.OutputPath,
                            codec,
                            height,
                        options.UpscalerBackend.ToString(),
                        options.RefinerBackend.ToString(),
                        currentFrames,
                        totalFrames,
                        phase,
                        Complete: false,
                        CanResume: true,
                        DateTime.UtcNow));
                    resumeProcessedFrames = currentFrames;
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, pct, $"{pct:0.0}%", Time(itemWatch.Elapsed), etaEstimator.Estimate(currentFrames, totalFrames, stageWatch.Elapsed, stageWatch.Elapsed, draining), phase, currentText, Summary(item.Index, total, overall.Elapsed), null, heartbeat, processingFpsText, latestFrameTimestampSeconds is double ts ? FormatFrameTimestamp(ts) : null));
                    if (heartbeat)
                    {
                        WritePerfSnapshot(TimeSpan.Zero);
                    }
                }

                UpdateProgress(heartbeat: true);

                var skipRequested = false;
                try
                {
                    await foreach (var inputBuffer in rawFrameChannel.Reader.ReadAllAsync(pipelineToken).ConfigureAwait(false))
                    {
                        byte[]? frameBuffer = null;
                        var frameQueued = false;
                        try
                        {
                            Interlocked.Decrement(ref rawQueueCurrent);
                            if (item.SkipRequested)
                            {
                                skipRequested = true;
                                break;
                            }

                            if (currentFrames == 0)
                            {
                                WriteStartupLog("First raw frame received");
                            }

                            var shouldUpdatePreview = previewEnabled
                                && (currentFrames == 0 || previewUpdateWatch.ElapsedMilliseconds >= 150);
                            if (shouldUpdatePreview)
                            {
                                previewUpdateWatch.Restart();
                                previewReport!(new RenderPreviewFrameUpdate(
                                    item.Index,
                                    true,
                                    RenderPreviewFramePayload.From(inputBuffer.AsSpan(0, inputFrameBytes)),
                                    rawWidth,
                                    rawHeight,
                                    rawWidth * 3));
                            }

                            var sectionStart = Stopwatch.GetTimestamp();
                            await upscaleIn.WriteAsync(inputBuffer.AsMemory(0, inputFrameBytes), pipelineToken).ConfigureAwait(false);
                            await upscaleIn.FlushAsync(pipelineToken).ConfigureAwait(false);
                            upscaleWriteElapsed += Stopwatch.GetElapsedTime(sectionStart);
                            if (currentFrames == 0)
                            {
                                WriteStartupLog("First frame written to upscaler");
                            }
                            sectionStart = Stopwatch.GetTimestamp();

                            frameBuffer = ArrayPool<byte>.Shared.Rent(outputFrameBytes);
                            await ReadExactAsync(upscaleOut, frameBuffer.AsMemory(0, outputFrameBytes), pipelineToken).ConfigureAwait(false);
                            upscaleReadElapsed += Stopwatch.GetElapsedTime(sectionStart);
                            if (currentFrames == 0)
                            {
                                WriteStartupLog("First upscaled frame received");
                            }

                            if (refinerIn is not null && refinerOut is not null)
                            {
                                sectionStart = Stopwatch.GetTimestamp();
                                await refinerIn.WriteAsync(frameBuffer.AsMemory(0, outputFrameBytes), pipelineToken).ConfigureAwait(false);
                                await refinerIn.FlushAsync(pipelineToken).ConfigureAwait(false);
                                var refinedBuffer = ArrayPool<byte>.Shared.Rent(outputFrameBytes);
                                try
                                {
                                    await ReadExactAsync(refinerOut, refinedBuffer.AsMemory(0, outputFrameBytes), pipelineToken).ConfigureAwait(false);
                                }
                                catch
                                {
                                    ArrayPool<byte>.Shared.Return(refinedBuffer);
                                    throw;
                                }

                                upscaleReadElapsed += Stopwatch.GetElapsedTime(sectionStart);
                                ArrayPool<byte>.Shared.Return(frameBuffer);
                                frameBuffer = refinedBuffer;
                                if (currentFrames == 0)
                                {
                                    WriteStartupLog("First refined frame received");
                                }
                            }

                            byte[]? processedBuffer = null;
                            if (antiFlicker is not null)
                            {
                                processedBuffer = ArrayPool<byte>.Shared.Rent(outputFrameBytes);
                                if (!antiFlicker.Process(frameBuffer, processedBuffer))
                                {
                                    antiFlicker.Dispose();
                                    antiFlicker = null;
                                    ArrayPool<byte>.Shared.Return(processedBuffer);
                                    processedBuffer = null;
                                }
                                else
                                {
                                    ArrayPool<byte>.Shared.Return(frameBuffer);
                                    frameBuffer = processedBuffer;
                                    processedBuffer = null;
                                }
                            }
                            if (shouldUpdatePreview)
                            {
                                var previewBuffer = frameBuffer;
                                previewReport!(new RenderPreviewFrameUpdate(
                                    item.Index,
                                    false,
                                    RenderPreviewFramePayload.From(previewBuffer.AsSpan(0, outputFrameBytes)),
                                    upWidth,
                                    upHeight,
                                    upWidth * 3));
                            }

                            var timestampSeconds = await GetTimestampForFrameAsync(pipelineToken).ConfigureAwait(false);
                            if (timestampSeconds is double rawTimestamp)
                            {
                                if (timestampRepairState is not null
                                    && timestampRepairState.TryRepair(rawTimestamp, out var repairedTimestamp))
                                {
                                    timestampSeconds = repairedTimestamp;
                                    if (!timestampRepairLogged)
                                    {
                                        timestampRepairLogged = true;
                                        WriteStartupLog($"Timestamp repair enabled: isolated spike corrected near frame {currentFrames + 1} ({rawTimestamp:0.######} -> {repairedTimestamp:0.######})");
                                    }
                                }

                                emittedTimestamps.Add(timestampSeconds.Value);
                            }

                            if (currentFrames == 0 && timestampSeconds is not null)
                            {
                                WriteStartupLog($"First frame timestamp: {timestampSeconds.Value:0.######}");
                                if (!encoderSupportsPerFrameTimestamps)
                                {
                                    WriteStartupLog("Warning: encoder session does not support per-frame timestamps; timestamp will not affect output timing.");
                                }
                            }

                            var encodeEnqueueStart = Stopwatch.GetTimestamp();
                            await encodeFrameChannel.Writer.WriteAsync(new FrameWriteItem(frameBuffer, outputFrameBytes, timestampSeconds), pipelineToken).ConfigureAwait(false);
                            Interlocked.Add(ref encodeQueueWriteWaitTicks, Stopwatch.GetElapsedTime(encodeEnqueueStart).Ticks);
                            UpdateMax(ref encodeQueueMax, Interlocked.Increment(ref encodeQueueCurrent));
                            frameQueued = true;
                            frameBuffer = null;

                            currentFrames++;
                            resumeProcessedFrames = currentFrames;
                            if (currentFrames > 0 && !firstFrameHeartbeatCts.IsCancellationRequested)
                            {
                                firstFrameHeartbeatCts.Cancel();
                            }
                            if (lastTick.ElapsedMilliseconds >= 1000)
                            {
                                UpdateProgress(heartbeat: true);
                                lastTick.Restart();
                            }
                        }
                        finally
                        {
                            if (!frameQueued && frameBuffer is not null)
                            {
                                ArrayPool<byte>.Shared.Return(frameBuffer);
                            }
                            ArrayPool<byte>.Shared.Return(inputBuffer);
                        }
                    }
                }
                finally
                {
                    try { firstFrameHeartbeatCts.Cancel(); } catch { }
                    try { antiFlicker?.Dispose(); } catch { }
                    try { refinerIn?.Close(); } catch { }
                    try { upscaleIn.Close(); } catch { }
                    try { await decodeProducer.ConfigureAwait(false); } catch { }
                    try { encodeFrameChannel.Writer.TryComplete(); } catch { }
                    try { await encodeWriter.ConfigureAwait(false); } catch { }
                    try { await encodeSession.FlushAsync(cancellationToken).ConfigureAwait(false); } catch { }
                    try { await timestampCompletion.ConfigureAwait(false); } catch { }
                }

                var frameLoopElapsed = frameLoopWatch.Elapsed;
                WritePerfSnapshot(encodeWatch.Elapsed);

                if (skipRequested)
                {
                    WriteResumeState(
                        resumeStatePath,
                        new ResumeStateEntry(
                            1,
                            item.SourcePath,
                            item.OutputPath,
                            codec,
                            height,
                            options.UpscalerBackend.ToString(),
                            options.RefinerBackend.ToString(),
                            currentFrames,
                            totalFrames,
                            "Skipped",
                            Complete: false,
                            CanResume: true,
                            DateTime.UtcNow));
                    item.IsInterrupted = true;
                    item.IsBusy = false;
                    item.Stage = LocalizedStrings.LogCancelled;
                    item.OutputState = LocalizedStrings.LogCancelled;
                    item.Detail = string.Empty;
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogSkippingEncode, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                    continue;
                }

                draining = true;
                await decode.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await upscale.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (refiner is not null)
                {
                    await refiner.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                await encodeSession.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(decodeStderr, upscaleStderr, refinerStderr).ConfigureAwait(false);

                var decodeExitCode = decode.Process.ExitCode;
                var upscaleExitCode = upscale.Process.ExitCode;
                var refinerExitCode = refiner?.Process.ExitCode ?? 0;
                var encodeExitCode = encodeSession.ExitCode;
                if (decodeExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(decodeLastError)
                        ? LocalizedStrings.LogBatchFailed(item.Title)
                        : $"{LocalizedStrings.LogBatchFailed(item.Title)} {decodeLastError}");
                }

                if (upscaleExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(upscaleLastError)
                        ? LocalizedStrings.LogBatchFailed(item.Title)
                        : $"{LocalizedStrings.LogBatchFailed(item.Title)} {upscaleLastError}");
                }

                if (refinerExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(refinerLastError)
                        ? LocalizedStrings.LogBatchFailed(item.Title)
                        : $"{LocalizedStrings.LogBatchFailed(item.Title)} {refinerLastError}");
                }

                if (encodeExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(encodeLastError)
                        ? LocalizedStrings.LogBatchFailed(item.Title)
                        : $"{LocalizedStrings.LogBatchFailed(item.Title)} {encodeLastError}");
                }
                var fallbackFrameDuration = metadata.Duration > 0 && currentFrames > 0
                    ? metadata.Duration / currentFrames
                    : (metadata.Fps > 0
                        ? 1.0 / metadata.Fps
                        : 0.04);
                var rawFrameTimestamps = timestampCacheHit && cachedTimestamps.Length > 0
                    ? (IReadOnlyList<double>)cachedTimestamps
                    : timestampBridge is not null && timestampBridge.HasValues
                        ? timestampBridge.Snapshot()
                        : BuildUniformTimestamps(currentFrames, fallbackFrameDuration);
                IReadOnlyList<double> frameTimestamps = emittedTimestamps.Count > 0
                    ? emittedTimestamps
                    : rawFrameTimestamps;
                var durations = frameTimestamps.Count > 0
                    ? GetFrameDurationsFromTimestamps(frameTimestamps, fallbackFrameDuration)
                    : BuildUniformDurations(currentFrames, fallbackFrameDuration);
                WritePerfSnapshot(encodeWatch.Elapsed, final: true);
                TryCopyPerfLog(perfLog, perfLogFinal);
                if (!timestampCacheHit && rawFrameTimestamps.Count > 0)
                {
                    SaveTimestampCache(item.SourcePath, rawFrameTimestamps);
                }
                var timingDebugLog = Path.Combine(
                    options.OutputFolder,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.timing.debug.log");
                WriteTimingDebugLog(timingDebugLog, metadata.Duration, frameTimestamps, durations);
                if (isResumeRun)
                {
                    WriteStartupLog($"Finalizing resumed render from frame {resumeStartFrame}");
                    await FinalizeResumedOutputAsync(
                        options.FfmpegPath,
                        item.SourcePath,
                        subtitle,
                        hasSub,
                        item.OutputPath,
                        resumePartialVideoPath,
                        resumeContinuationOutputPath,
                        resumeMergedVideoPath,
                        resumeMuxedOutputPath,
                        cancellationToken,
                        line => encodeLastError = line).ConfigureAwait(false);
                }
                WriteResumeState(
                    resumeStatePath,
                    new ResumeStateEntry(
                        1,
                        item.SourcePath,
                        item.OutputPath,
                        codec,
                        height,
                        options.UpscalerBackend.ToString(),
                        options.RefinerBackend.ToString(),
                        currentFrames,
                        totalFrames,
                        "Completed",
                        Complete: true,
                        CanResume: false,
                        DateTime.UtcNow));

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodeComplete, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogWroteFile(Path.GetFileName(item.OutputPath)), StageElapsedText: Time(stageWatch.Elapsed)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!isResumeRun)
                {
                    HandleIncompleteOutput(item.OutputPath, options.PreserveIncompleteOutput);
                }
                WriteResumeState(
                    resumeStatePath,
                    new ResumeStateEntry(
                        1,
                        item.SourcePath,
                        item.OutputPath,
                        resumeCodec,
                        resumeTargetHeight,
                        options.UpscalerBackend.ToString(),
                        options.RefinerBackend.ToString(),
                        resumeProcessedFrames,
                        resumeTotalFrames,
                        "Cancelled",
                        Complete: false,
                        CanResume: isResumeRun || options.PreserveIncompleteOutput || File.Exists(item.OutputPath),
                        DateTime.UtcNow));
                item.IsInterrupted = true;
                item.IsBusy = false;
                item.Stage = LocalizedStrings.LogCancelled;
                item.OutputState = LocalizedStrings.LogCancelled;
                item.Detail = string.Empty;
                throw;
            }
            catch
            {
                if (!isResumeRun)
                {
                    HandleIncompleteOutput(item.OutputPath, options.PreserveIncompleteOutput);
                }
                try
                {
                    WriteResumeState(
                        resumeStatePath,
                        new ResumeStateEntry(
                            1,
                            item.SourcePath,
                            item.OutputPath,
                            resumeCodec,
                            resumeTargetHeight,
                            options.UpscalerBackend.ToString(),
                            options.RefinerBackend.ToString(),
                            resumeProcessedFrames,
                            resumeTotalFrames,
                            "Failed",
                            Complete: false,
                            CanResume: isResumeRun || options.PreserveIncompleteOutput || File.Exists(item.OutputPath),
                            DateTime.UtcNow));
                }
                catch
                {
                }
                throw;
            }
            finally
            {
                if (isResumeRun)
                {
                    TryDeleteDirectory(resumeWorkingDirectory);
                }
            }
        }

        report(new PipelineProgress(total, total, items[^1].Title, LocalizedStrings.LogBatchComplete, 100, "100%", Time(overall.Elapsed), "00:00:00", LocalizedStrings.LogBatchComplete, string.Empty, Summary(total, total, overall.Elapsed), LocalizedStrings.LogBatchComplete, StageElapsedText: Time(overall.Elapsed)));
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static int ParseIntInvariant(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static async Task FinalizeResumedOutputAsync(
        string ffmpegPath,
        string sourcePath,
        string subtitlePath,
        bool hasSubtitles,
        string finalOutputPath,
        string partialVideoPath,
        string continuationOutputPath,
        string mergedVideoPath,
        string muxedOutputPath,
        CancellationToken cancellationToken,
        Action<string>? onErrorLine)
    {
        if (!File.Exists(finalOutputPath))
        {
            throw new InvalidOperationException("Resume source segment is missing.");
        }

        if (!File.Exists(continuationOutputPath))
        {
            throw new InvalidOperationException("Resume continuation segment is missing.");
        }

        var workingDirectory = Path.GetDirectoryName(finalOutputPath) ?? Environment.CurrentDirectory;
        await RunToolAsync(
            ffmpegPath,
            $"-hide_banner -y -nostats -loglevel error -i {Q(finalOutputPath)} -map 0:v:0 -c copy {Q(partialVideoPath)}",
            workingDirectory,
            cancellationToken,
            onErrorLine).ConfigureAwait(false);

        var concatListPath = Path.Combine(Path.GetDirectoryName(partialVideoPath) ?? workingDirectory, "concat.txt");
        var concatLines = new[]
        {
            $"file '{partialVideoPath.Replace("\\", "/").Replace("'", "'\\''")}'",
            $"file '{continuationOutputPath.Replace("\\", "/").Replace("'", "'\\''")}'"
        };
        await File.WriteAllLinesAsync(concatListPath, concatLines, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        await RunToolAsync(
            ffmpegPath,
            $"-hide_banner -y -nostats -loglevel error -safe 0 -f concat -i {Q(concatListPath)} -map 0:v:0 -c copy {Q(mergedVideoPath)}",
            workingDirectory,
            cancellationToken,
            onErrorLine).ConfigureAwait(false);

        var muxArgs = new StringBuilder("-hide_banner -y -nostats -loglevel error ");
        muxArgs.Append($"-i {Q(mergedVideoPath)} -i {Q(sourcePath)} ");
        if (hasSubtitles)
        {
            muxArgs.Append($"-i {Q(subtitlePath)} ");
        }

        muxArgs.Append("-map 0:v:0 -map 1:a? -c:v copy -c:a copy ");
        if (hasSubtitles)
        {
            muxArgs.Append("-map 2:s? -c:s copy ");
        }
        else
        {
            muxArgs.Append("-sn ");
        }

        muxArgs.Append(Q(muxedOutputPath));
        await RunToolAsync(
            ffmpegPath,
            muxArgs.ToString(),
            workingDirectory,
            cancellationToken,
            onErrorLine).ConfigureAwait(false);

        File.Move(muxedOutputPath, finalOutputPath, true);
    }

    private static async Task RunToolAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? onErrorLine)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start process: {fileName}");
        }

        using var registration = cancellationToken.Register(() => TryKillTree(process));
        var stderrPump = PumpLinesAsync(process.StandardError, onErrorLine, cancellationToken);
        var stdoutPump = PumpLinesAsync(process.StandardOutput, _ => { }, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stderrPump, stdoutPump).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}: {fileName}");
        }
    }

    private static async Task<SourceMetadata> GetSourceMetadataAsync(string ffmpeg, string sourcePath, CancellationToken ct)
    {
        var duration = 0.0;
        var width = 0;
        var height = 0;
        var fps = 0.0;
        var sizeReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease = StartProcess(
            ffmpeg,
            $"-hide_banner -i {Q(sourcePath)} -f null -",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct,
            line =>
            {
                var trimmed = line.Trim();
                if (duration <= 0)
                {
                    var durationMatch = DurationRegex.Match(trimmed);
                    if (durationMatch.Success)
                    {
                        duration = ParseHms(
                            durationMatch.Groups["hh"].Value,
                            durationMatch.Groups["mm"].Value,
                            durationMatch.Groups["ss"].Value);
                    }
                }

                if (width <= 0 || height <= 0)
                {
                    var streamMatch = VideoSizeRegex.Match(trimmed);
                    if (streamMatch.Success)
                    {
                        width = ParseIntInvariant(streamMatch.Groups["width"].Value);
                        height = ParseIntInvariant(streamMatch.Groups["height"].Value);
                    }
                }

                if (fps <= 0)
                {
                    var fpsMatch = FpsRegex.Match(trimmed);
                    if (fpsMatch.Success)
                    {
                        fps = ParseFraction(fpsMatch.Groups["fps"].Value);
                    }
                }

                if (width > 0 && height > 0)
                {
                    sizeReady.TrySetResult();
                }
            },
            out var stderrPump);

        try
        {
            using var cancellationRegistration = ct.Register(() => sizeReady.TrySetCanceled(ct));
            await sizeReady.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            TryKillTree(lease.Process);
            await stderrPump.ConfigureAwait(false);
            return new SourceMetadata(duration, width, height, fps);
        }
        catch
        {
            TryKillTree(lease.Process);
            await stderrPump.ConfigureAwait(false);
            throw new InvalidOperationException(LocalizedStrings.LogInvalidVideoInfo);
        }
    }

    private static double ParseHms(string hours, string minutes, string seconds)
    {
        var hh = ParseIntInvariant(hours);
        var mm = ParseIntInvariant(minutes);
        var ss = double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds) ? parsedSeconds : 0;
        return (hh * 3600) + (mm * 60) + ss;
    }

    private static string GetSourceMetadataCachePath(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        var signature = $"{Path.GetFullPath(sourcePath).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return Path.Combine(
            Path.GetTempPath(),
            "UltraFrameAI",
            "probe-cache",
            $"{hash}.json");
    }

    internal static string GetResumeStatePath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
        var fileName = $"{Path.GetFileNameWithoutExtension(outputPath)}.resume.json";
        return Path.Combine(directory, fileName);
    }

    internal static bool TryLoadResumeState(string outputPath, out string json)
    {
        json = string.Empty;
        try
        {
            var path = GetResumeStatePath(outputPath);
            if (!File.Exists(path))
            {
                return false;
            }

            json = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteResumeState(string path, ResumeStateEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
        }
    }

    private static bool TryLoadSourceMetadataCache(string sourcePath, out SourceMetadata metadata)
    {
        metadata = new SourceMetadata(0, 0, 0, 0);
        try
        {
            var cachePath = GetSourceMetadataCachePath(sourcePath);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            var json = File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize<SourceMetadataCacheEntry>(json);
            if (entry is null || !entry.Complete || entry.Width <= 0 || entry.Height <= 0)
            {
                return false;
            }

            metadata = new SourceMetadata(entry.Duration, entry.Width, entry.Height, entry.Fps);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveSourceMetadataCache(string sourcePath, SourceMetadata metadata)
    {
        try
        {
            var cachePath = GetSourceMetadataCachePath(sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? Environment.CurrentDirectory);
            var entry = new SourceMetadataCacheEntry(metadata.Duration, metadata.Width, metadata.Height, metadata.Fps, true);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(entry));
        }
        catch
        {
        }
    }

    private static string GetTimestampCachePath(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        var signature = $"{Path.GetFullPath(sourcePath).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return Path.Combine(
            Path.GetTempPath(),
            "UltraFrameAI",
            "timestamp-cache",
            $"{hash}.json");
    }

    private static bool TryLoadTimestampCache(string sourcePath, out double[] timestamps)
    {
        timestamps = Array.Empty<double>();
        try
        {
            var cachePath = GetTimestampCachePath(sourcePath);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            var json = File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize<TimestampCacheEntry>(json);
            if (entry is null || !entry.Complete || entry.Timestamps is null || entry.Timestamps.Length == 0)
            {
                return false;
            }

            timestamps = entry.Timestamps;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveTimestampCache(string sourcePath, IReadOnlyList<double> timestamps)
    {
        try
        {
            var cachePath = GetTimestampCachePath(sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? Environment.CurrentDirectory);
            var entry = new TimestampCacheEntry(timestamps.ToArray(), true);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(entry));
        }
        catch
        {
        }
    }

    private static void DeleteTimestampCache(string sourcePath)
    {
        try
        {
            var cachePath = GetTimestampCachePath(sourcePath);
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
        }
        catch
        {
        }
    }

    public static void CleanupTempCaches(TimeSpan maxAge)
    {
        CleanupTempCacheDirectory(Path.Combine(Path.GetTempPath(), "UltraFrameAI", "probe-cache"), maxAge, typeof(SourceMetadataCacheEntry));
        CleanupTempCacheDirectory(Path.Combine(Path.GetTempPath(), "UltraFrameAI", "timestamp-cache"), maxAge, typeof(TimestampCacheEntry));
    }

    private static void CleanupTempCacheDirectory(string directory, TimeSpan maxAge, Type cacheType)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var writeTime = File.GetLastWriteTimeUtc(file);
                    if (writeTime < cutoff)
                    {
                        File.Delete(file);
                        continue;
                    }

                    var json = File.ReadAllText(file);
                    var complete = cacheType == typeof(SourceMetadataCacheEntry)
                        ? JsonSerializer.Deserialize<SourceMetadataCacheEntry>(json) is { Complete: true }
                        : JsonSerializer.Deserialize<TimestampCacheEntry>(json) is { Complete: true };

                    if (!complete)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static bool LooksLikeProcessError(string line)
    {
        return line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    internal static int EstimateFrameCount(double duration, double fps)
    {
        if (duration <= 0 || fps <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Round(duration * fps, MidpointRounding.AwayFromZero));
    }

    internal static double ParseFraction(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains('/'))
        {
            var parts = trimmed.Split('/', 2);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                den != 0)
            {
                return num / den;
            }
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueAsDouble) ? valueAsDouble : 0;
    }

    private static ProcessLease StartProcess(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? onStderrLine,
        out Task stderrPump)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start process: {fileName}");
        }

        var registration = cancellationToken.Register(() => TryKillTree(process));
        stderrPump = PumpLinesAsync(process.StandardError, onStderrLine, cancellationToken);
        return new ProcessLease(process, stderrPump, registration);
    }

    private static async Task PumpLinesAsync(StreamReader reader, Action<string>? onLine, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                onLine?.Invoke(line);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static async Task<bool> TryReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        if (!await TryReadExactAsync(stream, buffer, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Unexpected end of stream while reading a frame.");
        }
    }

    private static double[] BuildUniformTimestamps(int count, double defaultDuration)
    {
        if (count <= 0)
        {
            return Array.Empty<double>();
        }

        var timestamps = new double[count];
        var step = Math.Max(0.001, defaultDuration);
        for (var i = 0; i < count; i++)
        {
            timestamps[i] = i * step;
        }

        return timestamps;
    }

    private static double[] GetFrameDurationsFromTimestamps(IReadOnlyList<double> timestamps, double defaultDuration)
    {
        if (timestamps.Count == 0)
        {
            return Array.Empty<double>();
        }

        var durations = new double[timestamps.Count];
        if (timestamps.Count == 1)
        {
            durations[0] = Math.Max(0.001, defaultDuration);
            return durations;
        }

        for (var i = 0; i < timestamps.Count - 1; i++)
        {
            var delta = timestamps[i + 1] - timestamps[i];
            if (delta <= 0)
            {
                delta = i > 0 ? durations[i - 1] : Math.Max(0.001, defaultDuration);
            }

            durations[i] = delta;
        }

        durations[^1] = durations[^2];
        return durations;
    }

    private static double[] BuildUniformDurations(int count, double defaultDuration)
    {
        if (count <= 0)
        {
            return Array.Empty<double>();
        }

        var durations = new double[count];
        var duration = Math.Max(0.001, defaultDuration);
        for (var i = 0; i < count; i++)
        {
            durations[i] = duration;
        }

        return durations;
    }

    private static void WriteTimingDebugLog(string logPath, double sourceDuration, IReadOnlyList<double> timestamps, IReadOnlyList<double> durations)
    {
        var timelineDuration = durations.Sum();
        var firstPts = timestamps.Count > 0 ? timestamps[0] : 0.0;
        var lastPts = timestamps.Count > 0 ? timestamps[^1] : 0.0;
        var delta = timelineDuration - sourceDuration;
        var percent = sourceDuration > 0 ? delta / sourceDuration * 100.0 : 0.0;

        var lines = new[]
        {
            $"frame_count={timestamps.Count}",
            $"source_duration_seconds={sourceDuration:0.######}",
            $"first_pts_seconds={firstPts:0.######}",
            $"last_pts_seconds={lastPts:0.######}",
            $"timeline_duration_seconds={timelineDuration:0.######}",
            $"delta_seconds={delta:0.######}",
            $"delta_percent={percent:0.######}"
        };

        File.WriteAllLines(logPath, lines, Encoding.UTF8);
    }

    private static void WritePerfLog(
        string logPath,
        TimeSpan preflightElapsed,
        TimeSpan frameLoopElapsed,
        TimeSpan encodeElapsed,
        TimeSpan decodeReadElapsed,
        TimeSpan upscaleWriteElapsed,
        TimeSpan upscaleReadElapsed,
        TimeSpan frameSendElapsed,
        TimeSpan timestampSubmitElapsed,
        int processedFrames,
        int totalFrames,
        TimeSpan processingElapsed,
        long rawQueueCurrent,
        long rawQueueMax,
        TimeSpan rawQueueWriteWaitElapsed,
        long encodeQueueCurrent,
        long encodeQueueMax,
        TimeSpan encodeQueueWriteWaitElapsed,
        bool final)
    {
        var fps = processingElapsed.TotalSeconds > 0
            ? processedFrames / processingElapsed.TotalSeconds
            : 0.0;

        var lines = new[]
        {
            $"preflight_seconds={preflightElapsed.TotalSeconds:0.######}",
            $"frame_loop_seconds={frameLoopElapsed.TotalSeconds:0.######}",
            $"encode_seconds={encodeElapsed.TotalSeconds:0.######}",
            $"decode_read_seconds={decodeReadElapsed.TotalSeconds:0.######}",
            $"upscale_write_seconds={upscaleWriteElapsed.TotalSeconds:0.######}",
            $"upscale_read_seconds={upscaleReadElapsed.TotalSeconds:0.######}",
            $"frame_send_seconds={frameSendElapsed.TotalSeconds:0.######}",
            $"timestamp_submit_seconds={timestampSubmitElapsed.TotalSeconds:0.######}",
            $"processed_frames={processedFrames}",
            $"total_frames={totalFrames}",
            $"processing_fps={fps:0.######}",
            $"raw_queue_current={rawQueueCurrent}",
            $"raw_queue_max={rawQueueMax}",
            $"raw_queue_write_wait_seconds={rawQueueWriteWaitElapsed.TotalSeconds:0.######}",
            $"encode_queue_current={encodeQueueCurrent}",
            $"encode_queue_max={encodeQueueMax}",
            $"encode_queue_write_wait_seconds={encodeQueueWriteWaitElapsed.TotalSeconds:0.######}",
            $"state={(final ? "final" : "live")}"
        };

        File.WriteAllLines(logPath, lines, Encoding.UTF8);
    }

    private static void TryCopyPerfLog(string sourcePath, string targetPath)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory);
                File.Copy(sourcePath, targetPath, true);
            }
        }
        catch
        {
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void UpdateMax(ref long target, long value)
    {
        while (true)
        {
            var current = Interlocked.Read(ref target);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
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

    private static void HandleIncompleteOutput(string path, bool preserveIncompleteOutput)
    {
        if (preserveIncompleteOutput)
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        TryDelete(path);
    }

    private static string Summary(int current, int total, TimeSpan elapsed) => LocalizedStrings.LogItemSummary(current, total, Time(elapsed));

    private static string Time(TimeSpan value) => value < TimeSpan.Zero ? "--:--:--" : value.ToString(@"hh\:mm\:ss");

    private static string FormatFrameTimestamp(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return string.Empty;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}-{time.Minutes:00}-{time.Seconds:00}.{time.Milliseconds:000}";
    }

}
