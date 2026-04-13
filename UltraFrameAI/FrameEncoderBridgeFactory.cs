namespace UltraFrameAI;

internal static class FrameEncoderBridgeFactory
{
    public static IFrameEncoderBridge CreateDefault()
        => new ProcessFrameEncoderBridge();
}
