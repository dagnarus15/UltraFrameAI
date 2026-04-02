using Xunit;

namespace UltraFrameAI.Tests;

public sealed class AntiFlickerNativeTests
{
    [Fact]
    public void AntiFlickerProcessor_Softens_Small_Flicker()
    {
        var processor = AntiFlickerProcessor.TryCreate(64, 64, 3, AntiFlickerMode.LumaStabilizer, "Anime", 75);
        Assert.NotNull(processor);

        using var _ = processor;

        var background = BuildSolidFrame(64, 64, 48, 48, 48);
        var flicker = BuildSolidFrame(64, 64, 48, 48, 48);
        DrawSquare(flicker, 64, 64, 24, 24, 16, 16, 190, 190, 190);

        var firstOutput = new byte[background.Length];
        var secondOutput = new byte[background.Length];

        Assert.True(processor!.Process(background, firstOutput));
        Assert.True(processor.Process(flicker, secondOutput));

        var inputAverage = AverageLuma(flicker, 64, 24, 24, 16, 16);
        var outputAverage = AverageLuma(secondOutput, 64, 24, 24, 16, 16);
        var baseAverage = AverageLuma(background, 64, 24, 24, 16, 16);

        Assert.True(outputAverage < inputAverage, $"input={inputAverage:F2} output={outputAverage:F2} base={baseAverage:F2}");
        Assert.True(outputAverage > baseAverage, $"input={inputAverage:F2} output={outputAverage:F2} base={baseAverage:F2}");
    }

    private static byte[] BuildSolidFrame(int width, int height, byte b, byte g, byte r)
    {
        var frame = new byte[width * height * 3];
        for (var i = 0; i < frame.Length; i += 3)
        {
            frame[i] = b;
            frame[i + 1] = g;
            frame[i + 2] = r;
        }

        return frame;
    }

    private static void DrawSquare(byte[] frame, int width, int height, int x, int y, int w, int h, byte b, byte g, byte r)
    {
        for (var yy = y; yy < Math.Min(height, y + h); yy++)
        {
            for (var xx = x; xx < Math.Min(width, x + w); xx++)
            {
                var index = (yy * width + xx) * 3;
                frame[index] = b;
                frame[index + 1] = g;
                frame[index + 2] = r;
            }
        }
    }

    private static double AverageLuma(byte[] frame, int width, int x, int y, int w, int h)
    {
        var sum = 0.0;
        var count = 0;
        for (var yy = y; yy < Math.Min(frame.Length / (width * 3), y + h); yy++)
        {
            for (var xx = x; xx < Math.Min(width, x + w); xx++)
            {
                var index = (yy * width + xx) * 3;
                var b = frame[index];
                var g = frame[index + 1];
                var r = frame[index + 2];
                sum += (29 * b + 150 * g + 77 * r) / 256.0;
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }
}
