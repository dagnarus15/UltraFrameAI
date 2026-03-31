using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class PipelineService
{
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
            var frameDir = string.Empty;
            try
            {
                if (item.SkipRequested)
                {
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogSkippingEncode, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                    continue;
                }

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", "00:00:00", "--:--:--", LocalizedStrings.LogPreparing, item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogStartingItem(item.Title), StageElapsedText: Time(itemWatch.Elapsed)));

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", "00:00:00", "--:--:--", LocalizedStrings.Get("LogProbingSource"), item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.Get("LogProbingSource"), StageElapsedText: Time(itemWatch.Elapsed)));
                var duration = await GetDurationAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.Get("LogReadingVideoInfo"), item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.Get("LogReadingVideoInfo"), StageElapsedText: Time(itemWatch.Elapsed)));
                var videoInfo = await GetVideoInfoAsync(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
                var estimatedFrames = EstimateFrameCount(duration, videoInfo.Fps);
                var codec = options.UseX265 ? "libx265" : "libx264";
                var crf = options.UseX265 ? 18 : 16;
                var height = options.UseX265 ? 2160 : 1080;
                var subtitle = Path.Combine(Path.GetDirectoryName(item.SourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(item.SourcePath) + ".RG Genshiken.ass");

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogPreparing, 0, "0%", Time(itemWatch.Elapsed), "--:--:--", LocalizedStrings.Get("LogCheckingCache"), item.SourcePath, Summary(item.Index, total, overall.Elapsed), LocalizedStrings.Get("LogCheckingCache"), StageElapsedText: Time(itemWatch.Elapsed)));

                var fps = videoInfo.Fps > 0
                    ? videoInfo.Fps
                    : (duration > 0 && estimatedFrames > 0 ? estimatedFrames / duration : 25.0);
                if (videoInfo.Width <= 0 || videoInfo.Height <= 0)
                {
                    throw new InvalidOperationException(LocalizedStrings.LogInvalidVideoInfo);
                }

                var upscaleScale = 2;
                var rawWidth = Math.Max(1, videoInfo.Width);
                var rawHeight = Math.Max(1, videoInfo.Height);
                var upWidth = checked(rawWidth * upscaleScale);
                var upHeight = checked(rawHeight * upscaleScale);

                var frameRoot = Path.Combine(Path.GetTempPath(), "UltraFrameAI");
                frameDir = Path.Combine(frameRoot, $"{item.Index:0000}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(frameDir);

                var sourceTimestamps = await GetFrameTimestampsFromVideo(options.FfprobePath, item.SourcePath, cancellationToken).ConfigureAwait(false);
                if (sourceTimestamps.Length == 0)
                {
                    throw new InvalidOperationException(LocalizedStrings.LogInvalidVideoInfo);
                }

                var timestamps = sourceTimestamps.ToArray();
                var totalFrames = timestamps.Length;
                var defaultFrameDuration = videoInfo.Fps > 0
                    ? 1.0 / videoInfo.Fps
                    : (duration > 0 && totalFrames > 0 ? duration / totalFrames : 0.04);
                var durations = GetFrameDurationsFromTimestamps(timestamps, defaultFrameDuration);
                var timingDebugLog = Path.Combine(
                    options.OutputFolder,
                    $"{Path.GetFileNameWithoutExtension(item.OutputPath)}.timing.debug.log");
                WriteTimingDebugLog(timingDebugLog, duration, timestamps, durations);

                var overwriteAllowed = options.Overwrite || item.ForceOverwrite;
                if (File.Exists(item.OutputPath) && !overwriteAllowed)
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

                var decodeLastError = string.Empty;
                var upscaleLastError = string.Empty;

                using var decode = StartProcess(options.FfmpegPath, decodeArgs.ToString(), Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory, cancellationToken, line => decodeLastError = line, out var decodeStderr);
                using var upscale = StartProcess(options.UpscalerPath, upscaleArgs.ToString(), Path.GetDirectoryName(item.SourcePath) ?? Environment.CurrentDirectory, cancellationToken, line => upscaleLastError = line, out var upscaleStderr);

                var decodeOut = decode.Process.StandardOutput.BaseStream;
                var upscaleIn = upscale.Process.StandardInput.BaseStream;
                var upscaleOut = upscale.Process.StandardOutput.BaseStream;

                var inputFrameBytes = rawWidth * rawHeight * 3;
                var outputFrameBytes = upWidth * upHeight * 3;
                var inputFrame = new byte[inputFrameBytes];
                var outputFrame = new byte[outputFrameBytes];
                var antiFlicker = options.UseAntiFlicker && options.AntiFlickerStrength > 0
                    ? AntiFlickerProcessor.TryCreate(upWidth, upHeight, 3, options.ContentMode, options.AntiFlickerStrength)
                    : null;
                var antiFlickerFrame = antiFlicker is null ? null : new byte[outputFrameBytes];
                var currentFrames = 0;
                var lastTick = Stopwatch.StartNew();
                var stageWatch = Stopwatch.StartNew();
                var etaEstimator = new PipeEtaEstimator(8, options.UseX265 ? 1.3 : 1.0);
                var draining = false;
                var previewEnabled = previewReport is not null;

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

                var skipRequested = false;
                try
                {
                    while (await TryReadExactAsync(decodeOut, inputFrame, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (item.SkipRequested)
                        {
                            skipRequested = true;
                            break;
                        }
                        await upscaleIn.WriteAsync(inputFrame, cancellationToken).ConfigureAwait(false);
                        if (previewEnabled)
                        {
                            previewReport!(new RenderPreviewFrameUpdate(item.Index, true, (byte[])inputFrame.Clone(), rawWidth, rawHeight, rawWidth * 3));
                        }

                        await ReadExactAsync(upscaleOut, outputFrame, cancellationToken).ConfigureAwait(false);
                        var frameBytes = outputFrame;
                        if (antiFlicker is not null && antiFlickerFrame is not null)
                        {
                            if (!antiFlicker.Process(outputFrame, antiFlickerFrame))
                            {
                                antiFlicker.Dispose();
                                antiFlicker = null;
                                antiFlickerFrame = null;
                            }
                            else
                            {
                                frameBytes = antiFlickerFrame;
                            }
                        }
                        if (previewEnabled)
                        {
                            var previewBuffer = frameBytes;
                            previewReport!(new RenderPreviewFrameUpdate(item.Index, false, (byte[])previewBuffer.Clone(), upWidth, upHeight, upWidth * 3));
                        }

                        var framePath = Path.Combine(frameDir, $"frame_{currentFrames:00000000}.png");
                        SaveFrameAsPng(framePath, frameBytes, upWidth, upHeight, upWidth * 3);

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
                    try { antiFlicker?.Dispose(); } catch { }
                    try { upscaleIn.Close(); } catch { }
                }

                if (skipRequested)
                {
                    item.IsInterrupted = true;
                    item.IsBusy = false;
                    item.Stage = LocalizedStrings.LogCancelled;
                    item.OutputState = LocalizedStrings.LogCancelled;
                    item.Detail = string.Empty;
                    report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogSkippingEncode, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogSkippingEncode, StageElapsedText: Time(itemWatch.Elapsed)));
                    continue;
                }

                draining = true;
                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodingVideo, $"{currentFrames}/{totalFrames}", Summary(item.Index, total, overall.Elapsed), null, true, Time(stageWatch.Elapsed)));

                var timelineFile = Path.Combine(frameDir, "timeline.txt");
                if (!WriteFrameConcatList(frameDir, timelineFile, durations))
                {
                    throw new InvalidOperationException(LocalizedStrings.LogInvalidVideoInfo);
                }

                await decode.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await upscale.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(decodeStderr, upscaleStderr).ConfigureAwait(false);

                var decodeExitCode = decode.Process.ExitCode;
                var upscaleExitCode = upscale.Process.ExitCode;
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

                var hasSub = File.Exists(subtitle);
                var outputContainer = string.Equals(options.OutputContainer, "mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "mkv";
                var encodeArgs = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
                encodeArgs.Append($"-f concat -safe 0 -i {Q(timelineFile)} ");
                encodeArgs.Append($"-i {Q(item.SourcePath)} ");
                if (hasSub)
                {
                    encodeArgs.Append($"-i {Q(subtitle)} ");
                }

                encodeArgs.Append($"-map 0:v:0 -map 1:a? -fps_mode vfr -vf {Q($"scale=-2:{height}:flags=lanczos,setsar=1")} -c:v {codec} -preset {options.EncoderPreset} -tune animation -crf {crf} -pix_fmt yuv420p ");
                if (outputContainer == "mp4")
                {
                    encodeArgs.Append("-c:a aac -b:a 320k ");
                    if (hasSub)
                    {
                        encodeArgs.Append("-map 2:s? -c:s mov_text ");
                    }
                    else
                    {
                        encodeArgs.Append("-sn ");
                    }
                    encodeArgs.Append("-movflags +faststart ");
                }
                else
                {
                    encodeArgs.Append("-c:a copy ");
                    if (hasSub)
                    {
                        encodeArgs.Append("-map 2:s? -c:s copy ");
                    }
                    else
                    {
                        encodeArgs.Append("-sn ");
                    }
                }
                encodeArgs.Append(Q(item.OutputPath));

                var encodeLastError = string.Empty;
                using var encode = StartProcess(options.FfmpegPath, encodeArgs.ToString(), Path.GetDirectoryName(item.OutputPath) ?? Environment.CurrentDirectory, cancellationToken, line => encodeLastError = line, out var encodeStderr);
                var encodeUiClock = Stopwatch.StartNew();
                while (!encode.Process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (encodeUiClock.ElapsedMilliseconds >= 1000)
                    {
                        UpdateProgress(heartbeat: true);
                        encodeUiClock.Restart();
                    }

                    await Task.WhenAny(encode.Process.WaitForExitAsync(cancellationToken), Task.Delay(250, cancellationToken)).ConfigureAwait(false);
                }

                await encode.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await encodeStderr.ConfigureAwait(false);
                if (encode.Process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(encodeLastError)
                        ? LocalizedStrings.LogBatchFailed(item.Title)
                        : $"{LocalizedStrings.LogBatchFailed(item.Title)} {encodeLastError}");
                }

                report(new PipelineProgress(item.Index, total, item.Title, LocalizedStrings.LogProcessing, 100, "100%", Time(itemWatch.Elapsed), "00:00:00", LocalizedStrings.LogEncodeComplete, Path.GetFileName(item.OutputPath), Summary(item.Index, total, overall.Elapsed), LocalizedStrings.LogWroteFile(Path.GetFileName(item.OutputPath)), StageElapsedText: Time(stageWatch.Elapsed)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(item.OutputPath);
                item.IsInterrupted = true;
                item.IsBusy = false;
                item.Stage = LocalizedStrings.LogCancelled;
                item.OutputState = LocalizedStrings.LogCancelled;
                item.Detail = string.Empty;
                throw;
            }
            finally
            {
                TryDelete(frameDir);
            }
        }

        report(new PipelineProgress(total, total, items[^1].Title, LocalizedStrings.LogBatchComplete, 100, "100%", Time(overall.Elapsed), "00:00:00", LocalizedStrings.LogBatchComplete, string.Empty, Summary(total, total, overall.Elapsed), LocalizedStrings.LogBatchComplete, StageElapsedText: Time(overall.Elapsed)));
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static int ParseIntInvariant(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static async Task<double> GetDurationAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Q(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        return lines.Count > 0 && double.TryParse(lines[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static async Task<VideoInfo> GetVideoInfoAsync(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,avg_frame_rate -of default=noprint_wrappers=1 {Q(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        var width = 0;
        var height = 0;
        var fps = 0.0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("width=", StringComparison.OrdinalIgnoreCase))
            {
                width = ParseIntInvariant(trimmed["width=".Length..]);
            }
            else if (trimmed.StartsWith("height=", StringComparison.OrdinalIgnoreCase))
            {
                height = ParseIntInvariant(trimmed["height=".Length..]);
            }
            else if (trimmed.StartsWith("avg_frame_rate=", StringComparison.OrdinalIgnoreCase))
            {
                fps = ParseFraction(trimmed["avg_frame_rate=".Length..]);
            }
            else if (trimmed.StartsWith("r_frame_rate=", StringComparison.OrdinalIgnoreCase) && fps <= 0)
            {
                fps = ParseFraction(trimmed["r_frame_rate=".Length..]);
            }
        }

        return new VideoInfo(width, height, fps);
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

    private static void SaveFrameAsPng(string path, byte[] pixels, int width, int height, int stride)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task<double[]> GetFrameTimestampsFromVideo(string ffprobe, string sourcePath, CancellationToken ct)
    {
        var lines = await ProcessRunner.CaptureLinesAsync(
            ffprobe,
            $"-v error -select_streams v:0 -show_entries frame=best_effort_timestamp_time -of csv=p=0 {Q(sourcePath)}",
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            ct).ConfigureAwait(false);

        var timestamps = new List<double>(lines.Count);
        foreach (var lineRaw in lines)
        {
            var token = lineRaw.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var firstToken = token.Split(',')[0].Trim();
            if (double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                timestamps.Add(value);
            }
        }

        return timestamps.ToArray();
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

    private static bool WriteFrameConcatList(string framesPath, string listPath, IReadOnlyList<double> durations)
    {
        var frameFiles = Directory.GetFiles(framesPath, "*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (frameFiles.Length == 0 || frameFiles.Length != durations.Count)
        {
            return false;
        }

        var lines = new List<string>(frameFiles.Length * 2 + 2);
        for (var i = 0; i < frameFiles.Length - 1; i++)
        {
            var name = frameFiles[i].Replace('\\', '/').Replace("'", "'\\''");
            lines.Add($"file '{name}'");
            lines.Add($"duration {durations[i].ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        var lastName = frameFiles[^1].Replace('\\', '/').Replace("'", "'\\''");
        lines.Add($"file '{lastName}'");
        lines.Add($"file '{lastName}'");

        File.WriteAllLines(listPath, lines, Encoding.ASCII);
        return true;
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

    private static string Summary(int current, int total, TimeSpan elapsed) => LocalizedStrings.LogItemSummary(current, total, Time(elapsed));

    private static string Time(TimeSpan value) => value < TimeSpan.Zero ? "--:--:--" : value.ToString(@"hh\:mm\:ss");

}
