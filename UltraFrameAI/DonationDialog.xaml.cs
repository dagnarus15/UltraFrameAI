using System.Collections.ObjectModel;
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
