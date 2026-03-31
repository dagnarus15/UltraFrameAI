namespace UltraFrameAI;

internal static class FrameEncoderBridgeFactory
{
    public static IFrameEncoderBridge CreateDefault(bool preferNativeEncoder)
        => preferNativeEncoder && NativeFrameEncoderBridge.IsAvailable()
            ? new NativeFrameEncoderBridge()
            : new ProcessFrameEncoderBridge();
}
