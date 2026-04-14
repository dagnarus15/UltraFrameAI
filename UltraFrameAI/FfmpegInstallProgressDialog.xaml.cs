using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class FfmpegInstallProgressDialog : Window, INotifyPropertyChanged
{
    private string _statusText;
    private double _progressValue;
    private bool _isIndeterminate;

    public FfmpegInstallProgressDialog()
    {
        InitializeComponent();
        DataContext = this;
        DialogTitle = LocalizedStrings.Get("FfmpegInstallProgressTitle");
        _statusText = LocalizedStrings.Get("FfmpegInstallProgressStarting");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DialogTitle { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set
        {
            if (Math.Abs(_progressValue - value) < 0.001)
            {
                return;
            }

            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (_isIndeterminate == value)
            {
                return;
            }

            _isIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public void UpdateProgress(double percent, string statusText, bool isIndeterminate = false)
    {
        IsIndeterminate = isIndeterminate;
        ProgressValue = Math.Max(0, Math.Min(100, percent));
        StatusText = statusText;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
