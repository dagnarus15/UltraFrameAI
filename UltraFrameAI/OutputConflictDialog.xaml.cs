using System.ComponentModel;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class OutputConflictDialog : Window
{
    private bool _allowClose;

    public OutputConflictDialog(OutputConflictRequest request)
    {
        InitializeComponent();
        FileName = request.OutputPath;
        MessageText = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            LocalizedStrings.Get("OutputConflictMessage"),
            request.OutputPath).Replace("\\n", Environment.NewLine);
        DataContext = this;
    }

    public string FileName { get; }

    public string MessageText { get; }

    public OutputConflictDecision Decision { get; private set; } = OutputConflictDecision.Cancel;

    private void Resume_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Resume);

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Skip);

    private void Replace_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Replace);

    private void ResumeAll_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.ResumeAll);

    private void SkipAll_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.SkipAll);

    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.ReplaceAll);

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
