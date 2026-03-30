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
    [InlineData("video", nameof(LocalizedStrings.ContentModeVideo))]
    [InlineData("faces", nameof(LocalizedStrings.ContentModeFaces))]
    [InlineData("anime", nameof(LocalizedStrings.ContentModeAnime))]
    [InlineData("anime-ultra", nameof(LocalizedStrings.ContentModeAnimeUltra))]
    [InlineData("animeultra", nameof(LocalizedStrings.ContentModeAnimeUltra))]
    public void LocalizedContentMode_MapsExpectedValues(string contentMode, string resourceName)
    {
        var actual = BenchmarkRunner.LocalizedContentMode(contentMode);
        var expected = LocalizedStrings.Get(resourceName);

        Assert.Equal(expected, actual);
    }
}
