using System.ComponentModel;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class PausedProcessExitedDialog : Window
{
    private bool _allowClose;

    public PausedProcessExitedDialog(string fileName, string processName)
    {
        InitializeComponent();
        FileName = fileName;
        ProcessName = processName;
        MessageText = LocalizedStrings.Get("PausedProcessExitedMessage").Replace("\\n", Environment.NewLine);
        DataContext = this;
    }

    public string FileName { get; }

    public string ProcessName { get; }

    public string MessageText { get; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
    }
}
