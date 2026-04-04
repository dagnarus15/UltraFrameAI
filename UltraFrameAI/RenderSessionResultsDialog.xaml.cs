using System.Collections.ObjectModel;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class RenderSessionResultsDialog : Window
{
    public RenderSessionResultsDialog(RenderSessionResults results)
    {
        InitializeComponent();
        SummaryText = LocalizedStrings.RenderSessionResultsSummary(results.TotalElapsedText);
        foreach (var item in results.Items)
        {
            Items.Add(new RenderSessionResultRow(
                item.Title,
                LocalizedStrings.RenderSessionResultsItemComplete(item.ElapsedText),
                LocalizedStrings.RenderSessionResultsItemFps(item.AverageFpsText)));
        }

        DataContext = this;
    }

    public string SummaryText { get; }

    public ObservableCollection<RenderSessionResultRow> Items { get; } = new();

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HelpCenterDialog(HelpCenterTab.Support)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    public sealed record RenderSessionResultRow(
        string Title,
        string CompletionText,
        string AverageFpsText);
}
