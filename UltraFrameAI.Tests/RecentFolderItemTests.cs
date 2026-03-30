using UltraFrameAI;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class RecentFolderItemTests
{
    [Fact]
    public void DisplayName_UsesLastFolderSegment()
    {
        var item = new RecentFolderItem(@"C:\Media\Shows\Akagi");

        Assert.Equal("Akagi", item.DisplayName);
    }

    [Fact]
    public void IsCurrent_RaisesPropertyChanged()
    {
        var item = new RecentFolderItem(@"C:\Media\Shows\Akagi");
        var changed = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecentFolderItem.IsCurrent))
            {
                changed = true;
            }
        };

        item.IsCurrent = true;

        Assert.True(changed);
    }
}
