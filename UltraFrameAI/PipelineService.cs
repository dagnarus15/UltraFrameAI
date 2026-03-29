using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class PipelineService
{
    private static readonly Regex TimestampRegex = new(@"_(?<stamp>\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    private sealed record VideoInfo(int Width, int Height, double Fps);

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
            var tailBlend = draining ? 1.0 : Math.Clamp((progress - 0.78) / 0.22, 0.0, 1.0);
            var tailSeconds = Math.Max(5.0, Math.Min(stageElapsed.TotalSeconds * 0.28 * _tailMultiplier, 45.0 * _tailMultiplier));
            tailSeconds = Math.Max(tailSeconds, secondsPerFrame * 12.0);
            var etaSeconds = frameSeconds + tailBlend * tailSeconds;

            if (draining)
            {
                etaSeconds = Math.Max(etaSeconds * (1.18 * _tailMultiplier), Math.Min(stageElapsed.TotalSeconds * 0.32 * _tailMultiplier, 45.0 * _tailMultiplier));
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
    {
        if (options.UsePipeMode)
        {
            await RunPipeAsync(items, options, report, cancellationToken).ConfigureAwait(false);
            return;
        }

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
            report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 0, "0%", "00:00:00", "--:--:--", LocalizedStrings.LogPreparing, item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogStartingItem(item.Title), StageElapsedText: Time(itemWatch.Elapsed)));
            await RunItemAsync(item, options, report, itemWatch, overall, total, cancellationToken).ConfigureAwait(false);
        }

        report(new PipelineProgress(total, total, items[^1].Title, LocalizedStrings.LogBatchComplete, 100, "100%", Time(overall.Elapsed), "00:00:00", LocalizedStrings.LogBatchComplete, string.Empty, Summary(total, total, overall.Elapsed), LocalizedStrings.LogBatchComplete, StageElapsedText: Time(overall.Elapsed)));
    }

    private async Task RunItemAsync(
        QueueItemViewModel item,
        PipelineOptions options,
        Action<PipelineProgress> report,
        Stopwatch itemWatch,
        Stopwatch overall,
        int totalItems,
        CancellationToken cancellationToken)
    {
        var workDir = item.WorkPath;
        var srcDir = item.SrcPath;
        var upDir = item.UpPath;
        Directory.CreateDirectory(workDir);

        var duration = await GetDurationAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
        var timeBase = await GetTimeBaseAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);

        var srcCount = CountPng(srcDir);
        var upCount = CountPng(upDir);
        var srcMarker = ReadMarker(MarkerPath(srcDir));
        var upMarker = ReadMarker(MarkerPath(upDir));

        var timestamps = TryTimestampsFromNames(upDir) ?? TryTimestampsFromNames(srcDir);
        var upComplete = (upMarker > 0 && upCount == upMarker) || (upCount > 0 && upCount > srcCount);

        if (upComplete)
        {
            if (timestamps is null)
            {
                timestamps = await GetTimestampsFromVideoAsync(options.FfprobePath, item.SourcePath, workDir, cancellationToken).ConfigureAwait(false);
            }

            if (upMarker <= 0)
            {
                WriteMarker(MarkerPath(upDir), upCount);
            }
        }
        else
        {
            timestamps ??= await GetTimestampsFromVideoAsync(options.FfprobePath, item.SourcePath, workDir, cancellationToken).ConfigureAwait(false);

            var totalFrames = timestamps.Count;
            var srcComplete = (srcMarker > 0 && srcCount == srcMarker) || srcCount == totalFrames;
            if (!srcComplete && srcCount > 0)
            {
                var moved = MoveToBak(srcDir);
                if (moved is not null)
                {
                    report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.LogOldSrcMovedAside, Path.GetFileName(moved), Summary(item.Index, totalItems, overall.Elapsed), moved));
                }
                Directory.CreateDirectory(srcDir);
            }

            if (!srcComplete)
            {
                report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogExtractingFrames, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.LogExtractingFrames, item.SourcePath, Summary(item.Index, totalItems, overall.Elapsed), null, true));
                await ExtractAsync(options, item.SourcePath, srcDir, totalFrames, item, totalItems, itemWatch, overall, report, cancellationToken).ConfigureAwait(false);
                srcCount = CountPng(srcDir);
                RenameToTimestampNames(srcDir, timestamps);
                WriteMarker(MarkerPath(srcDir), srcCount);
                RemoveLegacyMarker(srcDir);
            }

            upCount = CountPng(upDir);
            upMarker = ReadMarker(MarkerPath(upDir));
            var upReady = (upMarker > 0 && upCount == upMarker) || (upCount > srcCount);
            if (!upReady && upCount > 0)
            {
                var moved = MoveToBak(upDir);
                if (moved is not null)
                {
                    report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.LogOldUpMovedAside, Path.GetFileName(moved), Summary(item.Index, totalItems, overall.Elapsed), moved));
                }
                Directory.CreateDirectory(upDir);
            }

            if (!upReady)
            {
                report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogUpscalingFrames, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.LogUpscalingFrames, item.SourcePath, Summary(item.Index, totalItems, overall.Elapsed), null, true));
                await UpscaleAsync(options, srcDir, upDir, totalFrames, item, totalItems, itemWatch, overall, report, cancellationToken).ConfigureAwait(false);
                WriteMarker(MarkerPath(upDir), CountPng(upDir));
            }
        }

        timestamps ??= TryTimestampsFromNames(srcDir) ?? await GetTimestampsFromVideoAsync(options.FfprobePath, item.SourcePath, workDir, cancellationToken).ConfigureAwait(false);
        if (timestamps.Count == 0)
        {
            throw new InvalidOperationException(LocalizedStrings.LogUnableToResolveTimestamps);
        }

        var durations = BuildDurations(timestamps, timeBase);
        var timeline = Path.Combine(workDir, "timeline.txt");
        BuildConcatList(upDir, timeline, durations);

        if (File.Exists(item.OutputPath) && !options.Overwrite)
        {
            report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogEncodeComplete, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogOutputExists, Path.GetFileName(item.OutputPath), Summary(item.Index, totalItems, overall.Elapsed), LocalizedStrings.LogSkippingEncode));
        }
        else
        {
            report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogEncodingVideo, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.LogEncodingVideo, Path.GetFileName(item.OutputPath), Summary(item.Index, totalItems, overall.Elapsed), null, true));
            await EncodeAsync(options, timeline, item.SourcePath, item.OutputPath, duration, item, totalItems, itemWatch, overall, report, cancellationToken).ConfigureAwait(false);
        }

        if (!options.KeepTemp)
        {
            TryDelete(workDir);
        }

        report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogItemComplete, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogItemComplete, Path.GetFileName(item.OutputPath), Summary(item.Index, totalItems, overall.Elapsed), LocalizedStrings.LogFinishedItem(item.Title), StageElapsedText: Time(itemWatch.Elapsed)));
    }

    private async Task RunPipeAsync(
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        Action<PipelineProgress> report,
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
            report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", "00:00:00", "--:--:--", LocalizedStrings.LogPreparing, item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogStartingItem(item.Title), StageElapsedText: Time(itemWatch.Elapsed)));

            var duration = await GetDurationAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
            var videoInfo = await GetVideoInfoAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
            var totalFrames = await GetFrameCountAsync(options.FfprobePath, item.SourcePath, cancellationToken, duration, videoInfo.Fps).ConfigureAwait(false);
            var codec = options.UseX265 ? "libx265" : "libx264";
            var crf = options.UseX265 ? 18 : 16;
            var height = options.UseX265 ? 2160 : 1080;
            var fps = videoInfo.Fps > 0 ? videoInfo.Fps : (duration > 0 && totalFrames > 0 ? totalFrames / duration : 25.0);
            if (videoInfo.Width <= 0 || videoInfo.Height <= 0)
            {
                throw new InvalidOperationException(LocalizedStrings.LogUnableToResolveTimestamps);
            }

            var upscaleScale = 2;
            var rawWidth = Math.Max(1, videoInfo.Width);
            var rawHeight = Math.Max(1, videoInfo.Height);
            var upWidth = checked(rawWidth * upscaleScale);
            var upHeight = checked(rawHeight * upscaleScale);

            if (File.Exists(item.OutputPath) && !options.Overwrite)
            {
                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogOutputExists, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                continue;
            }

            var decodeArgs = new StringBuilder("-hide_banner -y -nostats -loglevel error ");
            decodeArgs.Append($"-i {Q(item.SourcePath)} -an -sn -dn -pix_fmt bgr24 -f rawvideo -vsync 0 pipe:1");

            var upscaleArgs = new StringBuilder();
            upscaleArgs.Append($"-p -W {rawWidth} -H {rawHeight} -N {totalFrames} -c 3 -i - -o - ");
            upscaleArgs.Append($"-s {upscaleScale} -m {Q(options.ModelDir)} -n realesr-animevideov3 -j {Q(options.UpscalerThreads)}");
            if (options.TileSize >= 0) upscaleArgs.Append($" -t {options.TileSize}");
            if (options.GpuId.HasValue) upscaleArgs.Append($" -g {options.GpuId.Value}");

            var encodeArgs = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
            encodeArgs.Append($"-f rawvideo -pix_fmt bgr24 -s {upWidth}x{upHeight} -r {fps.ToString("0.######", CultureInfo.InvariantCulture)} -i pipe:0 ");
            encodeArgs.Append($"-i {Q(item.SourcePath)} ");
            encodeArgs.Append($"-map 0:v:0 -map 1:a? -fps_mode vfr -vf {Q($"scale=-2:{height}:flags=lanczos,setsar=1")} -c:v {codec} -preset slower -tune animation -crf {crf} -pix_fmt yuv420p -c:a copy -sn ");
            encodeArgs.Append(Q(item.OutputPath));

            var decodeLastError = string.Empty;
            var upscaleLastError = string.Empty;
            var encodeLastError = string.Empty;

            using var decode = StartProcess(options.FfmpegPath, decodeArgs.ToString(), Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory, cancellationToken, line => decodeLastError = line, out var decodeStderr);
            using var upscale = StartProcess(options.UpscalerPath, upscaleArgs.ToString(), Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory, cancellationToken, line => upscaleLastError = line, out var upscaleStderr);
            using var encode = StartProcess(options.FfmpegPath, encodeArgs.ToString(), Path.GetDirectoryName(item.OutputPath) ?? Environment.CurrentDirectory, cancellationToken, line => encodeLastError = line, out var encodeStderr);

            var decodeOut = decode.Process.StandardOutput.BaseStream;
            var upscaleIn = upscale.Process.StandardInput.BaseStream;
            var upscaleOut = upscale.Process.StandardOutput.BaseStream;
            var encodeIn = encode.Process.StandardInput.BaseStream;

            var inputFrameBytes = rawWidth * rawHeight * 3;
            var outputFrameBytes = upWidth * upHeight * 3;
            var inputFrame = new byte[inputFrameBytes];
            var outputFrame = new byte[outputFrameBytes];
            var currentFrames = 0;
            var lastTick = Stopwatch.StartNew();
            var stageWatch = Stopwatch.StartNew();
            var etaEstimator = new PipeEtaEstimator(8, options.UseX265 ? 1.3 : 1.0);
            var draining = false;

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
                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, pct, $"{pct:0.0}%", Time(itemWatch.Elapsed), etaEstimator.Estimate(currentFrames, totalFrames, itemWatch.Elapsed, stageWatch.Elapsed, draining), phase, currentText, Summary(item.Index, total, overall.Elapsed), null, heartbeat, Time(stageWatch.Elapsed)));
            }

            UpdateProgress(heartbeat: true);

            try
            {
                while (await TryReadExactAsync(decodeOut, inputFrame, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await upscaleIn.WriteAsync(inputFrame, cancellationToken).ConfigureAwait(false);

                    await ReadExactAsync(upscaleOut, outputFrame, cancellationToken).ConfigureAwait(false);
                    await encodeIn.WriteAsync(outputFrame, cancellationToken).ConfigureAwait(false);

                    currentFrames++;
                    if (lastTick.ElapsedMilliseconds >= 1000)
                    {
                        UpdateProgress(heartbeat: true);
                        lastTick.Restart();
                    }
                }
            }
            finally
            {
                try { upscaleIn.Close(); } catch { }
                try { encodeIn.Close(); } catch { }
            }

            draining = true;
            report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodingVideo, $"{currentFrames}/{totalFrames}", Summary(item.Index, total, overall.Elapsed), null, true, Time(stageWatch.Elapsed)));

            var drainTick = Stopwatch.StartNew();
            while (!encode.Process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (drainTick.ElapsedMilliseconds >= 1000)
                {
                    UpdateProgress(heartbeat: true);
                    drainTick.Restart();
                }

                await Task.WhenAny(encode.Process.WaitForExitAsync(cancellationToken), Task.Delay(250, cancellationToken)).ConfigureAwait(false);
            }

            await decode.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await upscale.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await encode.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(decodeStderr, upscaleStderr, encodeStderr).ConfigureAwait(false);

            if (decode.Process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(decodeLastError)
                    ? LocalizedStrings.LogBatchFailed(item.Title)
                    : $"{LocalizedStrings.LogBatchFailed(item.Title)} {decodeLastError}");
            }

            if (upscale.Process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(upscaleLastError)
                    ? LocalizedStrings.LogBatchFailed(item.Title)
                    : $"{LocalizedStrings.LogBatchFailed(item.Title)} {upscaleLastError}");
            }

            if (encode.Process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(encodeLastError)
                    ? LocalizedStrings.LogBatchFailed(item.Title)
                    : $"{LocalizedStrings.LogBatchFailed(item.Title)} {encodeLastError}");
            }

            report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodeComplete, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogWroteFile(Path.GetFileName(item.OutputPath)), StageElapsedText: Time(stageWatch.Elapsed)));
        }

        report(new PipelineProgress(total, total, items[^1].Title, LocalizedStrings.LogBatchComplete, 100, "100%", Time(overall.Elapsed), "00:00:00", LocalizedStrings.LogBatchComplete, string.Empty, Summary(total, total, overall.Elapsed), LocalizedStrings.LogBatchComplete, StageElapsedText: Time(overall.Elapsed)));
    }

    private async Task ExtractAsync(PipelineOptions options, string sourcePath, string srcDir, int totalFrames, QueueItemViewModel item, int totalItems, Stopwatch itemWatch, Stopwatch overall, Action<PipelineProgress> report, CancellationToken ct)
    {
        var outPattern = Path.Combine(srcDir, "%08d.png");
        var args = new StringBuilder("-hide_banner -y ");
        if (options.FfmpegThreads > 0) args.Append($"-threads {options.FfmpegThreads} ");
        args.Append($"-i {Q(sourcePath)} -fps_mode passthrough -start_number 0 -progress pipe:1 -nostats {Q(outPattern)}");

        var current = 0;
        var status = LocalizedStrings.LogExtractingFrames;
        var runner = ProcessRunner.RunAsync(options.FfmpegPath, args.ToString(), srcDir,
            line =>
            {
                if (line.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line["frame=".Length..].Trim(), out var n))
                {
                    current = n;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    status = line;
                }
            },
            line => { if (!string.IsNullOrWhiteSpace(line)) status = line; },
            ct);

        var lastTick = Stopwatch.StartNew();
        var stageWatch = Stopwatch.StartNew();
        var etaEstimator = new PipeEtaEstimator(6);
        while (!runner.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            if (lastTick.ElapsedMilliseconds >= 1000)
            {
                var pct = totalFrames <= 0 ? 0 : Math.Min(100, current * 100.0 / totalFrames);
                report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogExtractingFrames, pct, $"{pct:0.0}%", Time(itemWatch.Elapsed), etaEstimator.Estimate(current, totalFrames, itemWatch.Elapsed, stageWatch.Elapsed), LocalizedStrings.LogExtractingFrames, status, Summary(item.Index, totalItems, overall.Elapsed), null, true, Time(stageWatch.Elapsed)));
                lastTick.Restart();
            }
            await Task.WhenAny(runner, Task.Delay(250, ct)).ConfigureAwait(false);
        }

        await runner.ConfigureAwait(false);
        report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogExtractionComplete, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogExtractionComplete, $"{CountPng(srcDir)} frames", Summary(item.Index, totalItems, overall.Elapsed), LocalizedStrings.LogExtractedFrames(CountPng(srcDir)), StageElapsedText: Time(stageWatch.Elapsed)));
    }

    private async Task UpscaleAsync(PipelineOptions options, string srcDir, string upDir, int totalFrames, QueueItemViewModel item, int totalItems, Stopwatch itemWatch, Stopwatch overall, Action<PipelineProgress> report, CancellationToken ct)
    {
        var args = new StringBuilder();
        args.Append($"-i {Q(srcDir)} -o {Q(upDir)} -n realesr-animevideov3 -s 2 -m {Q(options.ModelDir)} -f png -j {Q(options.UpscalerThreads)}");
        if (options.TileSize >= 0) args.Append($" -t {options.TileSize}");
        if (options.GpuId.HasValue) args.Append($" -g {options.GpuId.Value}");

        var runner = ProcessRunner.RunAsync(options.UpscalerPath, args.ToString(), upDir, null, null, ct);
        var lastTick = Stopwatch.StartNew();
        var stageWatch = Stopwatch.StartNew();
        var etaEstimator = new PipeEtaEstimator(6);
        var lastCount = -1;
        while (!runner.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            if (lastTick.ElapsedMilliseconds >= 1000)
            {
                var count = CountPng(upDir);
                if (count != lastCount)
                {
                    var pct = totalFrames <= 0 ? 0 : Math.Min(100, count * 100.0 / totalFrames);
                    report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogUpscalingFrames, pct, $"{pct:0.0}%", Time(itemWatch.Elapsed), etaEstimator.Estimate(count, totalFrames, itemWatch.Elapsed, stageWatch.Elapsed), LocalizedStrings.LogUpscalingFrames, $"{count}/{totalFrames}", Summary(item.Index, totalItems, overall.Elapsed), null, true, Time(stageWatch.Elapsed)));
                    lastCount = count;
                }
                lastTick.Restart();
            }
            await Task.WhenAny(runner, Task.Delay(250, ct)).ConfigureAwait(false);
        }

        await runner.ConfigureAwait(false);
        report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogUpscaleComplete, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogUpscaleComplete, $"{CountPng(upDir)} frames", Summary(item.Index, totalItems, overall.Elapsed), LocalizedStrings.LogUpscaledFrames(CountPng(upDir)), StageElapsedText: Time(stageWatch.Elapsed)));
    }

    private async Task EncodeAsync(PipelineOptions options, string timeline, string sourcePath, string outputPath, double duration, QueueItemViewModel item, int totalItems, Stopwatch itemWatch, Stopwatch overall, Action<PipelineProgress> report, CancellationToken ct)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var codec = options.UseX265 ? "libx265" : "libx264";
        var crf = options.UseX265 ? 18 : 16;
        var height = options.UseX265 ? 2160 : 1080;
        var args = new StringBuilder("-hide_banner -y -nostats -progress pipe:1 ");
        if (options.FfmpegThreads > 0) args.Append($"-threads {options.FfmpegThreads} ");
        args.Append($"-f concat -safe 0 -i {Q(timeline)} -i {Q(sourcePath)} ");
        args.Append($"-map 0:v:0 -map 1:a? -fps_mode vfr -vf {Q($"scale=-2:{height}:flags=lanczos,setsar=1")} -c:v {codec} -preset slower -tune animation -crf {crf} -pix_fmt yuv420p -c:a copy ");
        args.Append("-sn ");
        args.Append(Q(outputPath));

        var current = 0.0;
        var status = LocalizedStrings.LogEncodingVideo;
        var runner = ProcessRunner.RunAsync(options.FfmpegPath, args.ToString(), Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            line =>
            {
                if (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase))
                {
                    current = ParseTime(line["out_time=".Length..]);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    status = line;
                }
            },
            line => { if (!string.IsNullOrWhiteSpace(line)) status = line; },
            ct);

        var lastTick = Stopwatch.StartNew();
        var stageWatch = Stopwatch.StartNew();
        var etaEstimator = new PipeEtaEstimator(6);
        while (!runner.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            if (lastTick.ElapsedMilliseconds >= 1000)
            {
                var pct = duration <= 0 ? 0 : Math.Min(100, current * 100.0 / duration);
                report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogEncodingVideo, pct, $"{pct:0.0}%", Time(itemWatch.Elapsed), etaEstimator.Estimate(current, duration, itemWatch.Elapsed, stageWatch.Elapsed), LocalizedStrings.LogEncodingVideo, status, Summary(item.Index, totalItems, overall.Elapsed), null, true, Time(stageWatch.Elapsed)));
                lastTick.Restart();
            }
            await Task.WhenAny(runner, Task.Delay(250, ct)).ConfigureAwait(false);
        }

        await runner.ConfigureAwait(false);
        report(new PipelineProgress(item.Index, totalItems, item.Title, LocalizedStrings.LogEncodeComplete, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodeComplete, Path.GetFileName(outputPath), Summary(item.Index, totalItems, overall.Elapsed), LocalizedStrings.LogWroteFile(Path.GetFileName(outputPath)), StageElapsedText: Time(stageWatch.Elapsed)));
    }

    private async Task<List<double>> GetTimestampsFromVideoAsync(string ffprobe, string sourcePath, string workDir, CancellationToken ct)
    {
        var args = $"-v error -select_streams v:0 -show_entries frame=best_effort_timestamp_time -of csv=p=0 {Q(sourcePath)}";
        var lines = await ProcessRunner.CaptureLinesAsync(ffprobe, args, workDir, ct).ConfigureAwait(false);
        var result = new List<double>(lines.Count);
        foreach (var raw in lines)
        {
            var token = raw.Split(',')[0].Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                result.Add(value);
            }
        }
        return result;
    }

    private static async Task<int> GetFrameCountAsync(string ffprobe, string sourcePath, CancellationToken ct, double fallbackDuration = 0, double fallbackFps = 0)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames -of default=noprint_wrappers=1:nokey=1 {Q(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        if (lines.Count > 0 && int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        var fps = fallbackFps > 0 ? fallbackFps : 25.0;
        if (fallbackDuration > 0)
        {
            return Math.Max(1, (int)Math.Round(fallbackDuration * fps));
        }

        return 0;
    }

    private static async Task<VideoInfo> GetVideoInfoAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate,r_frame_rate -of csv=p=0 {Q(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        if (lines.Count == 0)
        {
            return new VideoInfo(0, 0, 0);
        }

        var tokens = lines[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return new VideoInfo(0, 0, 0);
        }

        var width = ParseIntInvariant(tokens[0]);
        var height = ParseIntInvariant(tokens[1]);
        var fps = 0.0;
        if (tokens.Length >= 3)
        {
            fps = ParseFraction(tokens[2]);
        }
        if (fps <= 0 && tokens.Length >= 4)
        {
            fps = ParseFraction(tokens[3]);
        }

        return new VideoInfo(width, height, fps);
    }

    private static List<double>? TryTimestampsFromNames(string path)
    {
        if (!Directory.Exists(path)) return null;
        var files = Directory.GetFiles(path, "*.png").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return null;
        var stamps = new List<double>(files.Length);
        foreach (var file in files)
        {
            var m = TimestampRegex.Match(Path.GetFileNameWithoutExtension(file));
            if (!m.Success) return null;
            if (!double.TryParse(m.Groups["stamp"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            stamps.Add(value);
        }
        return stamps;
    }

    private static List<double> BuildDurations(IReadOnlyList<double> stamps, double defaultDuration)
    {
        var durations = new List<double>(stamps.Count);
        if (stamps.Count == 0) return durations;
        if (stamps.Count == 1)
        {
            durations.Add(Math.Max(0.001, defaultDuration));
            return durations;
        }
        for (var i = 0; i < stamps.Count - 1; i++)
        {
            var delta = stamps[i + 1] - stamps[i];
            durations.Add(delta > 0 ? delta : Math.Max(0.001, defaultDuration));
        }
        durations.Add(Math.Max(0.001, defaultDuration));
        return durations;
    }

    private void BuildConcatList(string framesPath, string listPath, IReadOnlyList<double> durations)
    {
        var files = Directory.GetFiles(framesPath, "*.png").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0 || files.Length != durations.Count) throw new InvalidOperationException(LocalizedStrings.LogUnableToBuildTimeline);
        var sb = new StringBuilder();
        for (var i = 0; i < files.Length - 1; i++)
        {
            sb.AppendLine($"file '{files[i].Replace("\\", "/").Replace("'", "'\\''")}'");
            sb.AppendLine($"duration {durations[i].ToString("0.######", CultureInfo.InvariantCulture)}");
        }
        sb.AppendLine($"file '{files[^1].Replace("\\", "/").Replace("'", "'\\''")}'");
        File.WriteAllText(listPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static async Task<double> GetDurationAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(ffprobe, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Q(sourcePath)}", Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, ct).ConfigureAwait(false);
        return lines.Count > 0 && double.TryParse(lines[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static async Task<double> GetTimeBaseAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(ffprobe, $"-v error -select_streams v:0 -show_entries stream=time_base -of default=noprint_wrappers=1:nokey=1 {Q(sourcePath)}", Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, ct).ConfigureAwait(false);
        if (lines.Count == 0) return 0.001;
        var parts = lines[0].Trim().Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0)
        {
            return a / b;
        }
        return 0.001;
    }

    private void RenameToTimestampNames(string framesPath, IReadOnlyList<double> stamps)
    {
        var files = Directory.GetFiles(framesPath, "*.png").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        var count = Math.Min(files.Length, stamps.Count);
        if (count == 0) throw new InvalidOperationException(LocalizedStrings.LogNoFramesExtracted);
        var temp = new List<(string Source, string Temp, string Final)>(count);
        for (var i = 0; i < count; i++)
        {
            var stamp = stamps[i].ToString("0.000000", CultureInfo.InvariantCulture);
            temp.Add((files[i], Path.Combine(framesPath, $"__rename_tmp_{i:00000000}.png"), Path.Combine(framesPath, $"frame_{i:00000000}_{stamp}.png")));
        }
        foreach (var item in temp)
        {
            if (File.Exists(item.Temp)) File.Delete(item.Temp);
            if (File.Exists(item.Final)) File.Delete(item.Final);
            File.Move(item.Source, item.Temp);
        }
        foreach (var item in temp) File.Move(item.Temp, item.Final);
    }

    private static int CountPng(string path) => Directory.Exists(path) ? Directory.GetFiles(path, "*.png").Length : 0;

    private static string MarkerPath(string folderPath) => Path.Combine(Path.GetDirectoryName(folderPath) ?? string.Empty, Path.GetFileName(folderPath) + ".framecount.txt");

    private static int ReadMarker(string path)
    {
        var candidates = new[] { path, Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "framecount.txt") };
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var raw = File.ReadLines(candidate).FirstOrDefault();
            if (raw is null) continue;
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        }
        return -1;
    }

    private static void WriteMarker(string path, int count) => File.WriteAllText(path, count.ToString(CultureInfo.InvariantCulture), Encoding.ASCII);

    private static void RemoveLegacyMarker(string path)
    {
        var legacy = Path.Combine(path, "framecount.txt");
        if (File.Exists(legacy)) File.Delete(legacy);
    }

    private static string? MoveToBak(string path)
    {
        if (!Directory.Exists(path)) return null;
        var baseTarget = path + "_bak";
        var target = baseTarget;
        var index = 1;
        while (Directory.Exists(target))
        {
            target = baseTarget + index++;
        }
        Directory.Move(path, target);
        return target;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static int ParseIntInvariant(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double ParseFraction(string value)
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

    private static async Task<bool> TryReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Unexpected end of stream while reading a frame.");
            }

            offset += read;
        }

        return true;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        if (!await TryReadExactAsync(stream, buffer, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Unexpected end of stream while reading a frame.");
        }
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

    private static string Summary(int current, int total, TimeSpan elapsed) => LocalizedStrings.LogItemSummary(current, total, Time(elapsed));

    private static string Time(TimeSpan value) => value < TimeSpan.Zero ? "--:--:--" : value.ToString(@"hh\:mm\:ss");

    private static string Eta(double current, double total, TimeSpan elapsed)
    {
        if (current <= 0 || total <= 0 || current >= total) return "--:--:--";
        var rate = elapsed.TotalSeconds / Math.Max(0.001, current);
        return Time(TimeSpan.FromSeconds(Math.Max(0, rate * (total - current))));
    }

    private static double ParseTime(string value)
    {
        var m = Regex.Match(value.Trim(), @"(?<h>\d+):(?<m>\d+):(?<s>\d+)(?:\.(?<f>\d+))?");
        if (!m.Success) return 0;
        var h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var mnt = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var s = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
        var frac = m.Groups["f"].Success ? double.Parse("0." + m.Groups["f"].Value, CultureInfo.InvariantCulture) : 0.0;
        return h * 3600 + mnt * 60 + s + frac;
    }

}
