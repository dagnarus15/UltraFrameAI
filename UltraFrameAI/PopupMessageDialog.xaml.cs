using System.Windows;

namespace UltraFrameAI;

public partial class PopupMessageDialog : Window
{
    public PopupMessageDialog(string title, string message)
    {
        InitializeComponent();
        DataContext = this;
        DialogTitle = title;
        DialogMessage = message;
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
