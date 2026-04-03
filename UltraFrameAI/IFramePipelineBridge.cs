namespace UltraFrameAI;

internal interface IFramePipelineBridge
{
    TimestampStreamBridge CreateTimestampCollector(int capacity);

    string BuildDecodeArguments(string sourcePath, bool captureShowInfo, int startFrame = 0);

    string BuildUpscaleArguments(int rawWidth, int rawHeight, int upscaleFrameBudget, UpscalerBackendKind upscalerBackend, string modelDir, string upscalerThreads, int? tileSize, int? gpuId, string? externalArgsTemplate);

    string BuildEncodeArguments(int upWidth, int upHeight, double encodeFps, string sourcePath, string subtitlePath, bool hasSubtitles, string codec, string preset, int crf, string outputContainer, int height, string outputPath);
}
