using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class BenchmarkRunnerTests
{
    [Theory]
    [InlineData(0, 20, 0, 20)]
    [InlineData(10, 20, 0, 10)]
    [InlineData(40, 20, 10, 20)]
    [InlineData(100, 20, 40, 20)]
    public void ChooseSampleWindow_UsesCenteredWindowForLongerVideos(double sourceDuration, int requestedSeconds, double expectedStart, int expectedLength)
    {
        var window = BenchmarkRunner.ChooseSampleWindow(sourceDuration, requestedSeconds);

        Assert.InRange(window.Start, expectedStart - 0.001, expectedStart + 0.001);
        Assert.Equal(expectedLength, window.Length);
    }

    [Theory]
    [InlineData("slower", nameof(LocalizedStrings.BenchmarkShortCodecSlow))]
    [InlineData("medium", nameof(LocalizedStrings.BenchmarkShortCodecMed))]
    [InlineData("veryfast", nameof(LocalizedStrings.BenchmarkShortCodecVFast))]
    public void LocalizedPresetForCase_MapsExpectedValues(string preset, string resourceName)
    {
        var actual = BenchmarkRunner.LocalizedPresetForCase(preset);
        var expected = LocalizedStrings.Get(resourceName);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("GPU", "StartupBenchmarkPhaseGpu")]
    [InlineData("Tile 1024", "StartupBenchmarkPhaseTile")]
    [InlineData("Preset medium", "StartupBenchmarkPhasePreset")]
    public void LocalizeStartupBenchmarkPhase_MapsExpectedKeys(string phase, string resourceName)
    {
        var actual = BenchmarkRunner.LocalizeStartupBenchmarkPhase(phase);
        var expected = phase switch
        {
            "GPU" => LocalizedStrings.Get(resourceName),
            "Tile 1024" => LocalizedStrings.Format(resourceName, "1024"),
            "Preset medium" => LocalizedStrings.Format(resourceName, BenchmarkRunner.LocalizedPresetForCase("medium")),
            _ => throw new InvalidOperationException("Unexpected test phase.")
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LocalizeStartupBenchmarkPhase_LocalizesThreadRuns()
    {
        var actual = BenchmarkRunner.LocalizeStartupBenchmarkPhase("Threads 8:8:8 run 2");
        var expected = LocalizedStrings.Format("StartupBenchmarkPhaseThreadsRun", "8:8:8", "2");

        Assert.Equal(expected, actual);
    }
}
