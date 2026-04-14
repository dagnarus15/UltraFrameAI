using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace UltraFrameAI;

public partial class StartupBenchmarkWindow : Window, INotifyPropertyChanged
{
    private readonly StartupBenchmarkRequest _request;
    private readonly CancellationTokenSource _cts = new();
    private bool _started;
    private string _currentPhase = string.Empty;
    private string _currentCase = string.Empty;
    private string _currentDetail = string.Empty;
    private string _currentStepText = "0/0";
    private string _currentProgressText = "0%";
    private string _currentElapsedText = "--:--:--";
    private string _currentEtaText = "--:--:--";
    private double _currentProgress;

    public StartupBenchmarkWindow(StartupBenchmarkRequest request)
    {
        _request = request;
        InitializeComponent();
        DataContext = this;
        Loaded += StartupBenchmarkWindow_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StartupBenchmarkReport? Report { get; private set; }

    public string CurrentPhase
    {
        get => _currentPhase;
        private set => SetField(ref _currentPhase, value);
    }

    public string CurrentCase
    {
        get => _currentCase;
        private set => SetField(ref _currentCase, value);
    }

    public string CurrentDetail
    {
        get => _currentDetail;
        private set => SetField(ref _currentDetail, value);
    }

    public string CurrentStepText
    {
        get => _currentStepText;
        private set => SetField(ref _currentStepText, value);
    }

    public string CurrentProgressText
    {
        get => _currentProgressText;
        private set => SetField(ref _currentProgressText, value);
    }

    public string CurrentElapsedText
    {
        get => _currentElapsedText;
        private set => SetField(ref _currentElapsedText, value);
    }

    public string CurrentEtaText
    {
        get => _currentEtaText;
        private set => SetField(ref _currentEtaText, value);
    }

    public double CurrentProgress
    {
        get => _currentProgress;
        private set => SetField(ref _currentProgress, value);
    }

    private async void StartupBenchmarkWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        try
        {
            Report = await BenchmarkRunner.RunStartupBenchmarkAsync(
                _request,
                new Progress<StartupBenchmarkProgressUpdate>(HandleProgress),
                _cts.Token).ConfigureAwait(true);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            var popup = new PopupMessageDialog(
                UltraFrameAI.Resources.LocalizedStrings.StartupBenchmarkProgressTitle,
                ex.Message)
            {
                Owner = this
            };
            popup.ShowDialog();
            DialogResult = false;
        }
        finally
        {
            Close();
        }
    }

    private void HandleProgress(StartupBenchmarkProgressUpdate update)
    {
        CurrentPhase = update.Phase;
        CurrentCase = update.CaseName;
        CurrentDetail = update.Detail;
        CurrentStepText = $"{update.StepIndex}/{update.TotalSteps}";
        CurrentProgressText = update.ProgressText;
        CurrentElapsedText = update.ElapsedText;
        CurrentEtaText = update.EtaText;
        CurrentProgress = update.Progress;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_started && !_cts.IsCancellationRequested && Report is null)
        {
            _cts.Cancel();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
