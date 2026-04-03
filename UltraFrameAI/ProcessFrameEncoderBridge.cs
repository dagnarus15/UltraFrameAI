using System.Globalization;
using System.Text;

namespace UltraFrameAI;

internal sealed class ProcessFrameEncoderBridge : IFrameEncoderBridge
{
    public IFrameEncoderSession CreateSession(
        FrameEncoderSessionConfig config,
        CancellationToken cancellationToken,
        Action<string>? onStderr)
    {
        var args = BuildEncoderArguments(
            config.UpWidth,
            config.UpHeight,
            config.EncodeFps,
            config.SourcePath,
            config.SubtitlePath,
            config.HasSubtitles,
            config.Codec,
            config.Preset,
            config.Crf,
            config.OutputContainer,
            config.Height,
            config.OutputPath);

        var workingDirectory = Path.GetDirectoryName(config.OutputPath) ?? Environment.CurrentDirectory;
        return new ProcessFrameEncoderSession(config.FfmpegPath, args, workingDirectory, cancellationToken, onStderr);
    }

    public Stream CreateFrameInputStream() => Stream.Null;

    public string BuildEncoderArguments(
        int upWidth,
        int upHeight,
        double encodeFps,
        string sourcePath,
        string subtitlePath,
        bool hasSubtitles,
        string codec,
        string preset,
        int crf,
        string outputContainer,
        int height,
        string outputPath)
    {
        var args = new StringBuilder("-hide_banner -y -nostats -loglevel error -progress pipe:2 ");
        args.Append($"-f rawvideo -pix_fmt bgr24 -s {upWidth}x{upHeight} -r {encodeFps.ToString("0.######", CultureInfo.InvariantCulture)} -i pipe:0 ");
        var hasSourceMedia = !string.IsNullOrWhiteSpace(sourcePath);
        if (hasSourceMedia)
        {
            args.Append($"-i {Q(sourcePath)} ");
        }

        if (hasSubtitles && hasSourceMedia)
        {
            args.Append($"-i {Q(subtitlePath)} ");
        }

        args.Append($"-map 0:v:0 ");
        if (hasSourceMedia)
        {
            args.Append("-map 1:a? ");
        }

        args.Append($"-fps_mode cfr -vf {Q($"scale=-2:{height}:flags=lanczos,setsar=1")} -c:v {codec} -preset {preset} -tune animation -crf {crf} -pix_fmt yuv420p ");
        if (hasSourceMedia)
        {
            args.Append("-c:a copy ");
        }
        else
        {
            args.Append("-an ");
        }

        if (hasSubtitles && hasSourceMedia)
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
