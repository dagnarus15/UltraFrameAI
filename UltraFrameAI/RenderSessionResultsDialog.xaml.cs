using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
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
                LocalizedStrings.RenderSessionResultsItemFps(item.AverageFpsText),
                item.OutputPath));
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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OpenResultFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RenderSessionResultRow row })
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(row.OutputPath);
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }

    public sealed record RenderSessionResultRow(
        string Title,
        string CompletionText,
        string AverageFpsText,
        string OutputPath);
}
