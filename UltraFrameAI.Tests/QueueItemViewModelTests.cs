using UltraFrameAI;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class QueueItemViewModelTests
{
    [Fact]
    public void StatusTransitions_RespectSkippedAndInterruptedStates()
    {
        var originalLanguage = LocalizedStrings.CurrentLanguage;
        LocalizedStrings.SetLanguage(UiLanguage.English);

        try
        {
            var item = new QueueItemViewModel
            {
                Title = "episode.mkv",
                SourcePath = "source.mkv",
                OutputPath = "output.mkv"
            };

            Assert.Equal(LocalizedStrings.QueueStatusNew, item.StatusLabel);

            item.Progress = 100;
            Assert.True(item.IsCompleted);
            Assert.Equal(LocalizedStrings.QueueStatusCompleted, item.StatusLabel);

            item.IsInterrupted = true;
            Assert.False(item.IsCompleted);
            Assert.Equal(LocalizedStrings.QueueStatusInterrupted, item.StatusLabel);

            item.IsInterrupted = false;
            item.IsSkipped = true;
            Assert.False(item.IsCompleted);
            Assert.Equal(LocalizedStrings.QueueStatusInterrupted, item.StatusLabel);

            item.ResetUiState();
            Assert.Equal(0, item.Progress);
            Assert.False(item.IsInterrupted);
            Assert.False(item.IsSkipped);
            Assert.Equal(LocalizedStrings.QueueStatusNew, item.StatusLabel);
        }
        finally
        {
            LocalizedStrings.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public void PropertyChanged_RaisesForDerivedStatus()
    {
        var item = new QueueItemViewModel();
        var changes = new List<string?>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        item.Progress = 100;
        item.IsInterrupted = true;

        Assert.Contains(nameof(QueueItemViewModel.IsCompleted), changes);
        Assert.Contains(nameof(QueueItemViewModel.StatusLabel), changes);
    }
}
