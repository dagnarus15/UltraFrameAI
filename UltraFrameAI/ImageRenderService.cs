using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UltraFrameAI;

public sealed record ImageRenderProgress(
    int Completed,
    int Total,
    string CurrentFile,
    double Progress,
    string StatusText);

public sealed class ImageRenderService
{
    public async Task RunAsync(
        IReadOnlyList<QueueItemViewModel> items,
        PipelineOptions options,
        Action<ImageRenderProgress> report,
        Func<bool> shouldStopAfterCurrent,
        CancellationToken cancellationToken)
    {
        var total = items.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[i];
            var itemWatch = Stopwatch.StartNew();
            var completedBefore = i;
            report(new ImageRenderProgress(
                completedBefore,
                total,
                item.Title,
                Percent(completedBefore, total),
                $"Processing {item.Title}"));

            Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath) ?? options.OutputFolder);

            var temporaryOutput = Path.Combine(
                Path.GetDirectoryName(item.OutputPath) ?? options.OutputFolder,
                $".{Path.GetFileNameWithoutExtension(item.OutputPath)}.{Guid.NewGuid():N}.png");

            try
            {
                var workingDirectory = string.IsNullOrWhiteSpace(options.UpscalerWorkingDirectory)
                    ? (Path.GetDirectoryName(options.UpscalerPath) ?? Environment.CurrentDirectory)
                    : options.UpscalerWorkingDirectory;
                var args = BuildUpscaleArguments(item.SourcePath, temporaryOutput, options);
                var exitCode = await ProcessRunner.RunAsync(
                    options.UpscalerPath,
                    args,
                    workingDirectory,
                    null,
                    _ => { },
                    cancellationToken).ConfigureAwait(true);

                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Image upscaler exited with code {exitCode}.");
                }

                ResizeAndSave(temporaryOutput, item.OutputPath, options.TargetHeight);
                itemWatch.Stop();
                item.ElapsedText = FormatDuration(itemWatch.Elapsed);
                item.Progress = 100;
                item.ProgressText = "100%";
                item.OutputState = "Complete";
                item.IsBusy = false;
            }
            finally
            {
                TryDelete(temporaryOutput);
            }

            var completed = i + 1;
            report(new ImageRenderProgress(
                completed,
                total,
                item.Title,
                Percent(completed, total),
                $"Completed {item.Title}"));

            if (shouldStopAfterCurrent())
            {
                break;
            }
        }
    }

    private static string BuildUpscaleArguments(string sourcePath, string outputPath, PipelineOptions options)
    {
        var args = new List<string>
        {
            "-i",
            Quote(sourcePath),
            "-o",
            Quote(outputPath),
            "-s",
            "2",
            "-m",
            Quote(options.ModelDir),
            "-n",
            "realesr-animevideov3",
            "-j",
            Quote(options.UpscalerThreads)
        };

        if (options.TileSize >= 0)
        {
            args.Add("-t");
            args.Add(options.TileSize.ToString(CultureInfo.InvariantCulture));
        }

        if (options.GpuId.HasValue)
        {
            args.Add("-g");
            args.Add(options.GpuId.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-f");
        args.Add("png");
        return string.Join(" ", args);
    }

    private static void ResizeAndSave(string sourcePath, string outputPath, int targetHeight)
    {
        using var inputStream = File.OpenRead(sourcePath);
        var source = BitmapFrame.Create(inputStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource bitmap = source;

        if (targetHeight > 0 && source.PixelHeight != targetHeight)
        {
            var scale = targetHeight / (double)source.PixelHeight;
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            bitmap = transformed;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
        using var outputStream = File.Create(outputPath);
        BitmapEncoder encoder = Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(outputStream);
    }

    private static double Percent(int completed, int total)
        => total <= 0 ? 0 : Math.Clamp(completed * 100.0 / total, 0, 100);

    private static string FormatDuration(TimeSpan value)
        => value < TimeSpan.Zero ? "--:--:--" : value.ToString(@"hh\:mm\:ss");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
