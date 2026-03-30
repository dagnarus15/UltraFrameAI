using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class LocalizedStringsTests
{
    [Fact]
    public void SetLanguage_ChangesValuesAndRaisesEvent()
    {
        var original = LocalizedStrings.CurrentLanguage;
        var raised = 0;

        void Handler(object? sender, EventArgs e) => raised++;

        LocalizedStrings.LanguageChanged += Handler;
        try
        {
            var first = original == UiLanguage.English ? UiLanguage.Russian : UiLanguage.English;
            var second = first == UiLanguage.Russian ? UiLanguage.German : UiLanguage.Russian;

            LocalizedStrings.SetLanguage(first);
            var firstValue = LocalizedStrings.QueueStatusCompleted;

            LocalizedStrings.SetLanguage(second);
            var secondValue = LocalizedStrings.QueueStatusCompleted;

            Assert.NotEqual(firstValue, secondValue);
            Assert.True(raised >= 2);
        }
        finally
        {
            LocalizedStrings.LanguageChanged -= Handler;
            LocalizedStrings.SetLanguage(original);
        }
    }
}
