using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UltraFrameAI;

internal sealed class ProcessFramePipelineBridge : IFramePipelineBridge
{
    public TimestampStreamBridge CreateTimestampCollector(int capacity) => new(capacity);

    public string BuildDecodeArguments(string sourcePath, bool captureShowInfo)
    {
        var args = new StringBuilder("-hide_banner -y -nostats -loglevel info ");
        args.Append($"-i {Q(sourcePath)} -an -sn -dn ");
        if (captureShowInfo)
        {
            args.Append("-vf showinfo ");
        }

        args.Append("-pix_fmt bgr24 -f rawvideo -vsync 0 pipe:1");
        return args.ToString();
    }

    public string BuildUpscaleArguments(int rawWidth, int rawHeight, int upscaleFrameBudget, UpscalerBackendKind upscalerBackend, string modelDir, string upscalerThreads, int? tileSize, int? gpuId, string? externalArgsTemplate)
    {
        if (upscalerBackend is UpscalerBackendKind.StableSrExternal or UpscalerBackendKind.SupirExternal)
        {
            return BuildExternalUpscaleArguments(rawWidth, rawHeight, upscaleFrameBudget, modelDir, upscalerThreads, tileSize, gpuId, externalArgsTemplate);
        }

        var args = new StringBuilder();
        args.Append($"-p -W {rawWidth} -H {rawHeight} -N {upscaleFrameBudget} -c 3 -i - -o - ");
        args.Append($"-s 2 -m {Q(modelDir)} -n realesr-animevideov3 -j {Q(upscalerThreads)}");
        if (tileSize >= 0) args.Append($" -t {tileSize}");
        if (gpuId.HasValue) args.Append($" -g {gpuId.Value}");
        return args.ToString();
    }

    private static string BuildExternalUpscaleArguments(int rawWidth, int rawHeight, int upscaleFrameBudget, string modelDir, string upscalerThreads, int? tileSize, int? gpuId, string? externalArgsTemplate)
    {
        if (string.IsNullOrWhiteSpace(externalArgsTemplate))
        {
            throw new InvalidOperationException("External upscaler arguments template is empty.");
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{width}"] = rawWidth.ToString(CultureInfo.InvariantCulture),
            ["{height}"] = rawHeight.ToString(CultureInfo.InvariantCulture),
            ["{frameBudget}"] = upscaleFrameBudget.ToString(CultureInfo.InvariantCulture),
            ["{channels}"] = "3",
            ["{scale}"] = "2",
            ["{input}"] = "-",
            ["{output}"] = "-",
            ["{modelDir}"] = modelDir,
            ["{modelDirQ}"] = Q(modelDir),
            ["{threads}"] = upscalerThreads,
            ["{threadsQ}"] = Q(upscalerThreads),
            ["{tileSize}"] = (tileSize ?? -1).ToString(CultureInfo.InvariantCulture),
            ["{gpuId}"] = gpuId?.ToString() ?? "-1"
        };

        var args = externalArgsTemplate;
        foreach (var pair in replacements)
        {
            args = args.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return args;
    }

    public string BuildEncodeArguments(int upWidth, int upHeight, double encodeFps, string sourcePath, string subtitlePath, bool hasSubtitles, string codec, string preset, int crf, string outputContainer, int height, string outputPath)
    {
        var args = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
        args.Append($"-f rawvideo -pix_fmt bgr24 -s {upWidth}x{upHeight} -r {encodeFps.ToString("0.######", CultureInfo.InvariantCulture)} -i pipe:0 ");
        args.Append($"-i {Q(sourcePath)} ");
        if (hasSubtitles)
        {
            args.Append($"-i {Q(subtitlePath)} ");
        }

        args.Append($"-map 0:v:0 -map 1:a? -fps_mode cfr -vf {Q($"scale=-2:{height}:flags=lanczos,setsar=1")} -c:v {codec} -preset {preset} -tune animation -crf {crf} -pix_fmt yuv420p ");
        args.Append("-c:a copy ");
        if (hasSubtitles)
        {
            args.Append("-map 2:s? -c:s copy ");
        }
        else
        {
            args.Append("-sn ");
        }

        args.Append(Q(outputPath));
        return args.ToString();
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
