using System.IO;

namespace UltraFrameAI;

internal interface IFrameEncoderBridge
{
    IFrameEncoderSession CreateSession(
        FrameEncoderSessionConfig config,
        CancellationToken cancellationToken,
        Action<string>? onStderr);

    string BuildEncoderArguments(
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
        string outputPath);

    Stream CreateFrameInputStream();
}
