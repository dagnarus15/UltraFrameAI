using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace UltraFrameAI;

public partial class DonationDialog : Window
{
    public DonationDialog()
    {
        InitializeComponent();
        foreach (var entry in DonationSupportInfo.GetEntries())
        {
            Entries.Add(entry);
        }

        DataContext = this;
    }

    public ObservableCollection<DonationSupportEntry> Entries { get; } = new();

    private void DonationLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
