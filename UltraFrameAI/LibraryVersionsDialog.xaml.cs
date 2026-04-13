using System.Collections.ObjectModel;
using System.Windows;

namespace UltraFrameAI;

public partial class LibraryVersionsDialog : Window
{
    public LibraryVersionsDialog(IEnumerable<HelpVersionEntry> versions)
    {
        InitializeComponent();
        WindowCaptionColorManager.Attach(this);
        foreach (var version in versions)
        {
            Versions.Add(version);
        }

        DataContext = this;
    }

    public ObservableCollection<HelpVersionEntry> Versions { get; } = new();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
