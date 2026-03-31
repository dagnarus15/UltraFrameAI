namespace UltraFrameAI;

internal sealed record FrameEncoderSessionConfig(
    int UpWidth,
    int UpHeight,
    double EncodeFps,
    string SourcePath,
    string SubtitlePath,
    bool HasSubtitles,
    string Codec,
    string Preset,
    int Crf,
    string OutputContainer,
    int Height,
    string OutputPath,
    string FfmpegPath);
