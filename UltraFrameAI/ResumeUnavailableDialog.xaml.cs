using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class ResumeUnavailableDialog : Window, INotifyPropertyChanged
{
    private bool _allowClose;
    private bool _isReasonExpanded;

    public ResumeUnavailableDialog(string fileName, string reasonText)
    {
        InitializeComponent();
        FileName = fileName;
        MessageText = LocalizedStrings.Get("ResumeUnavailableMessage").Replace("\\n", Environment.NewLine);
        ReasonText = reasonText;
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; }

    public string MessageText { get; }

    public string ReasonText { get; }

    public bool IsReasonExpanded
    {
        get => _isReasonExpanded;
        set
        {
            if (_isReasonExpanded == value)
            {
                return;
            }

            _isReasonExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReasonVisible));
        }
    }

    public Visibility IsReasonVisible => IsReasonExpanded ? Visibility.Visible : Visibility.Collapsed;

    public OutputConflictDecision Decision { get; private set; } = OutputConflictDecision.Cancel;

    private void Replace_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Replace);

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish(OutputConflictDecision.Skip);

    private void ReasonToggle_Click(object sender, RoutedEventArgs e) => IsReasonExpanded = !IsReasonExpanded;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
