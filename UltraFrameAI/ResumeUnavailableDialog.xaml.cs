using System.ComponentModel;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class ResumeUnavailableDialog : Window
{
    private bool _allowClose;

    public ResumeUnavailableDialog(string fileName)
    {
        InitializeComponent();
        FileName = fileName;
        MessageText = LocalizedStrings.Get("ResumeUnavailableMessage").Replace("\\n", Environment.NewLine);
        DataContext = this;
    }

    public string FileName { get; }

    public string MessageText { get; }

    public OutputConflictDecision Decision { get; private set; } = OutputConflictDecision.Cancel;

    private void Replace_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Replace);

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Skip);

    private void Finish(OutputConflictDecision decision)
    {
        Decision = decision;
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
