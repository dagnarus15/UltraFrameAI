using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using UltraFrameAI.Resources;

using IoPath = System.IO.Path;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace UltraFrameAI;

public enum BenchmarkChartMode
{
    Runtime,
    QualityTime
}

public enum BenchmarkRankingSortMode
{
    Metric,
    Name
}

public partial class BenchmarkWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<BenchmarkStepItem> _steps = new();
    private readonly ObservableCollection<BenchmarkCaseResult> _results = new();
    private readonly ObservableCollection<LogEntryViewModel> _logLines = UiCollections.CreateLogCollection();
    private readonly ObservableCollection<BenchmarkLegendItem> _legendItems = new();
    private readonly ObservableCollection<BenchmarkLegendItem> _rankingLegendItems = new();
    private readonly Dictionary<string, BenchmarkStepItem> _stepLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WpfBrush> _groupBrushes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codec/Preset"] = BrushFromHex("#4DA3FF"),
        ["Upscaler threads"] = BrushFromHex("#4CD18C"),
        ["Tile size"] = BrushFromHex("#F5B14C"),
    };

    private CancellationTokenSource? _benchmarkCts;
    private BenchmarkChartsWindow? _chartsWindow;
    private bool _isRunning;
    private bool _closingAfterCancel;
    private int _totalSteps;
    private int _completedSteps;
    private string _sourcePath = string.Empty;
    private string _outputFolder = string.Empty;
    private string _sampleSecondsText = "20";
    private string _currentStepText = "0/0";
    private string _currentCaseText = string.Empty;
    private string _currentStatus = string.Empty;
    private string _currentDetail = string.Empty;
    private string _currentElapsedText = "--:--:--";
    private string _currentEtaText = "--:--:--";
    private string _currentProgressText = "0%";
    private double _currentProgress;
    private string _stepSummary = string.Empty;
    private string _statusSummary = string.Empty;
    private string _bestOverallText = LocalizedStrings.BenchmarkNotAvailable;
    private string _bestQualityTimeText = LocalizedStrings.BenchmarkNotAvailable;
    private string _chartModeLabel = LocalizedStrings.BenchmarkRuntimeMode;
    private BenchmarkChartMode _chartMode = BenchmarkChartMode.Runtime;
    private BenchmarkRankingSortMode _rankingSortMode = BenchmarkRankingSortMode.Metric;

    public BenchmarkWindow(string? initialSourcePath = null, string? initialOutputFolder = null)
    {
        InitializeComponent();
        DataContext = this;
        SourcePath = initialSourcePath is { } path && File.Exists(path) ? IoPath.GetFullPath(path) : string.Empty;
        var benchmarkBasePath = !string.IsNullOrWhiteSpace(SourcePath)
            ? SourcePath
            : initialOutputFolder;
        OutputFolder = GetBenchmarkOutputFolder(benchmarkBasePath);

        _results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            RefreshWinnerSummary();
            RenderCharts();
        };
        _legendItems.Add(new BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendCodecPreset, _groupBrushes["Codec/Preset"]));
        _legendItems.Add(new BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendThreads, _groupBrushes["Upscaler threads"]));
        _legendItems.Add(new BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendTileSize, _groupBrushes["Tile size"]));
        _rankingLegendItems.Add(new BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendFastest, WpfBrushes.WhiteSmoke));
        _rankingLegendItems.Add(new BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendBestQualityTime, WpfBrushes.Gold));
        RefreshChartModeLabel();
        RefreshWinnerSummary();
        _steps.CollectionChanged += (_, _) => RefreshStepSummary();
        SizeChanged += (_, _) => RenderCharts();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            SourcePathTextBox.Focus();
            SourcePathTextBox.SelectAll();
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BenchmarkStepItem> Steps => _steps;

    public ObservableCollection<BenchmarkCaseResult> Results => _results;

    public ObservableCollection<LogEntryViewModel> LogLines => _logLines;

    public ObservableCollection<BenchmarkLegendItem> LegendItems => _legendItems;

    public ObservableCollection<BenchmarkLegendItem> RankingLegendItems => _rankingLegendItems;

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetField(ref _sourcePath, value))
            {
                OnPropertyChanged(nameof(HasSource));
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => SetField(ref _outputFolder, value);
    }

    public string SampleSecondsText
    {
        get => _sampleSecondsText;
        set => SetField(ref _sampleSecondsText, value);
    }

    public string CurrentStepText
    {
        get => _currentStepText;
        private set => SetField(ref _currentStepText, value);
    }

    public string CurrentCaseText
    {
        get => _currentCaseText;
        private set => SetField(ref _currentCaseText, value);
    }

    public string CurrentStatus
    {
        get => _currentStatus;
        private set => SetField(ref _currentStatus, value);
    }

    public string CurrentDetail
    {
        get => _currentDetail;
        private set => SetField(ref _currentDetail, value);
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

    public string CurrentProgressText
    {
        get => _currentProgressText;
        private set => SetField(ref _currentProgressText, value);
    }

    public double CurrentProgress
    {
        get => _currentProgress;
        private set => SetField(ref _currentProgress, value);
    }

    public string StepSummary
    {
        get => _stepSummary;
        private set => SetField(ref _stepSummary, value);
    }

    public string StatusSummary
    {
        get => _statusSummary;
        private set => SetField(ref _statusSummary, value);
    }

    public string BestOverallText
    {
        get => _bestOverallText;
        private set => SetField(ref _bestOverallText, value);
    }

    public string BestQualityTimeText
    {
        get => _bestQualityTimeText;
        private set => SetField(ref _bestQualityTimeText, value);
    }

    public string ChartModeLabel
    {
        get => _chartModeLabel;
        private set => SetField(ref _chartModeLabel, value);
    }

    public BenchmarkChartMode ChartMode
    {
        get => _chartMode;
        set
        {
            if (SetField(ref _chartMode, value))
            {
                RefreshChartModeLabel();
                OnPropertyChanged(nameof(IsRuntimeChart));
                OnPropertyChanged(nameof(IsQualityTimeChart));
                RenderCharts();
            }
        }
    }

    public bool IsRuntimeChart => ChartMode == BenchmarkChartMode.Runtime;

    public bool IsQualityTimeChart => ChartMode == BenchmarkChartMode.QualityTime;

    public BenchmarkRankingSortMode RankingSortMode
    {
        get => _rankingSortMode;
        set
        {
            if (SetField(ref _rankingSortMode, value))
            {
                OnPropertyChanged(nameof(IsMetricRanking));
                OnPropertyChanged(nameof(IsNameRanking));
                RenderCharts();
            }
        }
    }

    public bool IsMetricRanking => RankingSortMode == BenchmarkRankingSortMode.Metric;

    public bool IsNameRanking => RankingSortMode == BenchmarkRankingSortMode.Name;

    public int TotalSteps
    {
        get => _totalSteps;
        private set => SetField(ref _totalSteps, value);
    }

    public int CompletedSteps
    {
        get => _completedSteps;
        private set => SetField(ref _completedSteps, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(HasSource));
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);

    public bool HasResults => Results.Count > 0;

    private void BrowseSourceFile_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = LocalizedStrings.BrowseVideoFileDialogTitle,
            InitialDirectory = GetInputDirectory(SourcePath),
            Filter = LocalizedStrings.BenchmarkVideoFilesFilter,
            Multiselect = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            SourcePath = dialog.FileName;
            OutputFolder = GetBenchmarkOutputFolder(SourcePath);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = LocalizedStrings.LogSelectOutputFolder,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = GetInputDirectory(OutputFolder)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private async void StartBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        {
            AppendLog(LocalizedStrings.BenchmarkChooseSourceFileFirst);
            return;
        }

        if (!int.TryParse(SampleSecondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleSeconds) || sampleSeconds <= 0)
        {
            sampleSeconds = 20;
            SampleSecondsText = "20";
        }

        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder)
            ? GetBenchmarkOutputFolder(SourcePath)
            : IoPath.GetFullPath(OutputFolder);

        OutputFolder = outputFolder;
        Directory.CreateDirectory(outputFolder);

        IsRunning = true;
        _closingAfterCancel = false;
        _benchmarkCts?.Dispose();
        _benchmarkCts = new CancellationTokenSource();

        _steps.Clear();
        _results.Clear();
        _logLines.Clear();
        TotalSteps = 0;
        CompletedSteps = 0;
        CurrentStepText = "0/0";
        CurrentCaseText = string.Empty;
        CurrentStatus = LocalizedStrings.BenchmarkStatusStarting;
        CurrentDetail = string.Empty;
        CurrentElapsedText = "--:--:--";
        CurrentEtaText = "--:--:--";
        CurrentProgress = 0;
        CurrentProgressText = "0%";
        StepSummary = string.Empty;
        StatusSummary = LocalizedStrings.BenchmarkStatusStarting;
        RenderCharts();
        _chartsWindow?.RefreshCharts();
        AppendLog(LocalizedStrings.BenchmarkStatusStarted);

        try
        {
            var report = await BenchmarkRunner.RunInteractiveAsync(
                new BenchmarkRequest(SourcePath, outputFolder, sampleSeconds),
                new Progress<BenchmarkProgressUpdate>(HandleBenchmarkProgress),
                _benchmarkCts.Token).ConfigureAwait(true);

            OutputFolder = report.OutputRoot;
            AppendLog(string.Format(CultureInfo.InvariantCulture, "{0} {1}", LocalizedStrings.BenchmarkStatusFinished, report.OutputRoot));
            StatusSummary = LocalizedStrings.BenchmarkStatusFinished;
            RenderCharts();
        }
        catch (OperationCanceledException)
        {
            AppendLog(LocalizedStrings.BenchmarkStatusCancelledUser);
            StatusSummary = LocalizedStrings.BenchmarkStatusCancelled;
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", LocalizedStrings.BenchmarkStatusFailed, ex.Message));
            StatusSummary = LocalizedStrings.BenchmarkStatusFailed;
        }
        finally
        {
            _benchmarkCts.Dispose();
            _benchmarkCts = null;
            IsRunning = false;
            RefreshStepSummary();
            if (_closingAfterCancel)
            {
                Close();
            }
        }
    }

    private void StopBenchmark_Click(object sender, RoutedEventArgs e)
    {
        _benchmarkCts?.Cancel();
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
        }

        Process.Start(new ProcessStartInfo("explorer.exe", Quote(OutputFolder)) { UseShellExecute = true });
    }

    private void OpenCharts_Click(object sender, RoutedEventArgs e)
    {
        if (_chartsWindow is { IsLoaded: true })
        {
            _chartsWindow.Activate();
            return;
        }

        var chartsWindow = new BenchmarkChartsWindow(Results)
        {
            Left = Left + 48,
            Top = Top + 32
        };
        chartsWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_chartsWindow, chartsWindow))
            {
                _chartsWindow = null;
            }
        };
        _chartsWindow = chartsWindow;
        chartsWindow.Show();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!IsRunning)
        {
            _chartsWindow?.Close();
            return;
        }

        e.Cancel = true;
        _closingAfterCancel = true;
        _benchmarkCts?.Cancel();
    }

    private void HandleBenchmarkProgress(BenchmarkProgressUpdate update)
    {
        TotalSteps = update.TotalSteps;
        CurrentStepText = update.TotalSteps > 0 ? $"[{update.StepIndex}/{update.TotalSteps}]" : "[--]";
        if (!string.IsNullOrWhiteSpace(update.Group) || !string.IsNullOrWhiteSpace(update.CaseName))
        {
            CurrentCaseText = string.IsNullOrWhiteSpace(update.Group)
                ? update.CaseName
                : $"{GetLocalizedGroupName(update.Group)} / {update.CaseName}";
        }

        CurrentStatus = string.IsNullOrWhiteSpace(update.CurrentStatus) ? CurrentStatus : update.CurrentStatus;
        CurrentDetail = update.CurrentDetail;
        CurrentElapsedText = update.ElapsedText;
        CurrentEtaText = update.EtaText;
        CurrentProgress = update.Progress;
        CurrentProgressText = update.ProgressText;

        BenchmarkStepItem? step = null;
        if (update.Kind is BenchmarkProgressKind.CaseStarted or BenchmarkProgressKind.CaseProgress or BenchmarkProgressKind.CaseCompleted &&
            (!string.IsNullOrWhiteSpace(update.Group) || !string.IsNullOrWhiteSpace(update.CaseName)))
        {
            step = GetOrCreateStep(update.StepIndex, update.TotalSteps, update.Group, update.CaseName);
            step.Progress = update.Progress;
            step.ProgressText = update.ProgressText;
            step.ElapsedText = update.ElapsedText;
            step.EtaText = update.EtaText;
            step.Detail = update.CurrentDetail;
            step.IsCurrent = update.Kind != BenchmarkProgressKind.CaseCompleted;
        }

        switch (update.Kind)
        {
            case BenchmarkProgressKind.BenchmarkStarted:
                AppendLog(LocalizedStrings.BenchmarkStatusStartingDetailed);
                RefreshStepSummary();
                _chartsWindow?.RefreshCharts();
                break;
            case BenchmarkProgressKind.CaseStarted:
                if (step is not null)
                {
                    step.IsCurrent = true;
                    step.IsCompleted = false;
                    step.Detail = update.CurrentStatus;
                }
                AppendLog(string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkCaseStarted, update.StepIndex, update.TotalSteps, GetLocalizedGroupName(update.Group), update.CaseName));
                break;
            case BenchmarkProgressKind.CaseProgress:
                break;
            case BenchmarkProgressKind.CaseCompleted:
                if (step is not null)
                {
                    step.IsCurrent = false;
                    step.IsCompleted = true;
                    step.Progress = 100;
                    step.ProgressText = "100%";
                }

                if (update.Result is not null)
                {
                    CompletedSteps++;
                    if (!Results.Any(result => string.Equals(result.Group, update.Result.Group, StringComparison.OrdinalIgnoreCase) && string.Equals(result.Name, update.Result.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Results.Add(update.Result);
                    }

                    AppendLog(string.Format(CultureInfo.InvariantCulture, LocalizedStrings.BenchmarkCaseCompleted, update.StepIndex, update.TotalSteps, GetLocalizedGroupName(update.Group), update.CaseName, update.Result.Elapsed.TotalSeconds));
                    RenderCharts();
                    _chartsWindow?.RefreshCharts();
                }
                RefreshStepSummary();
                break;
            case BenchmarkProgressKind.BenchmarkCompleted:
                StatusSummary = LocalizedStrings.BenchmarkStatusFinished;
                break;
        }
    }

    private BenchmarkStepItem GetOrCreateStep(int index, int total, string group, string name)
    {
        var key = $"{index}:{group}:{name}";
        if (_stepLookup.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var item = new BenchmarkStepItem
        {
            Index = index,
            Total = total,
            Group = GetLocalizedGroupName(group),
            Name = name,
            ProgressText = "0%",
            ElapsedText = "--:--:--",
            EtaText = "--:--:--",
            Detail = string.Empty
        };
        _stepLookup[key] = item;
        Steps.Add(item);
        RefreshStepSummary();
        return item;
    }

    private void RefreshStepSummary()
    {
        StepSummary = TotalSteps > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", CompletedSteps, TotalSteps)
            : string.Empty;
        OnPropertyChanged(nameof(HasResults));
        RefreshWinnerSummary();
    }

    private void RefreshWinnerSummary()
    {
        var bestOverall = BenchmarkRunner.BuildWeightedRecommendationText(Results);
        var successful = Results.Where(result => result.Success).ToList();
        var bestQuality = successful.OrderByDescending(result => result.QualityScore / Math.Max(0.1, result.Elapsed.TotalSeconds)).FirstOrDefault();

        BestOverallText = bestOverall;
        BestQualityTimeText = bestQuality is null
            ? LocalizedStrings.BenchmarkNotAvailable
            : $"{GetLocalizedGroupName(bestQuality.Group)} / {bestQuality.Name} - {(bestQuality.QualityScore / Math.Max(0.1, bestQuality.Elapsed.TotalSeconds)):0.###}";
    }

    private void RefreshChartModeLabel()
    {
        ChartModeLabel = ChartMode == BenchmarkChartMode.Runtime
            ? LocalizedStrings.BenchmarkRuntimeMode
            : LocalizedStrings.BenchmarkQualityTimeMode;
    }

    private void SetRuntimeChart_Click(object sender, RoutedEventArgs e)
    {
        ChartMode = BenchmarkChartMode.Runtime;
    }

    private void SetQualityChart_Click(object sender, RoutedEventArgs e)
    {
        ChartMode = BenchmarkChartMode.QualityTime;
    }

    private void SetMetricRanking_Click(object sender, RoutedEventArgs e)
    {
        RankingSortMode = BenchmarkRankingSortMode.Metric;
    }

    private void SetNameRanking_Click(object sender, RoutedEventArgs e)
    {
        RankingSortMode = BenchmarkRankingSortMode.Name;
    }

    private void AppendLog(string message)
    {
        LogLines.Add(new LogEntryViewModel(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message));
    }

    private void RenderCharts()
    {
        // Charts moved to BenchmarkChartsWindow.
    }

    private static string GetLocalizedGroupName(string group)
        => group switch
        {
            "Codec/Preset" => LocalizedStrings.BenchmarkLegendCodecPreset,
            "Upscaler threads" => LocalizedStrings.BenchmarkLegendThreads,
            "Tile size" => LocalizedStrings.BenchmarkLegendTileSize,
            _ => group
        };

    private static WpfBrush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)(new BrushConverter().ConvertFromString(hex) ?? WpfBrushes.White);
        brush.Freeze();
        return brush;
    }

    private static string GetBenchmarkOutputFolder(string? inputPath)
    {
        var inputDir = GetInputDirectory(inputPath);
        return IoPath.Combine(string.IsNullOrWhiteSpace(inputDir) ? Environment.CurrentDirectory : inputDir, "UltraFrameAI-benchmark");
    }

    private static string GetInputDirectory(string? path)
    {
        if (Directory.Exists(path))
        {
            return IoPath.GetFullPath(path);
        }

        if (File.Exists(path))
        {
            return IoPath.GetDirectoryName(IoPath.GetFullPath(path)) ?? Environment.CurrentDirectory;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            var directory = IoPath.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return IoPath.GetFullPath(directory);
            }
        }

        return Environment.CurrentDirectory;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class BenchmarkStepItem : INotifyPropertyChanged
    {
        private string _group = string.Empty;
        private string _name = string.Empty;
        private string _progressText = "0%";
        private string _elapsedText = "--:--:--";
        private string _etaText = "--:--:--";
        private string _detail = string.Empty;
        private bool _isCurrent;
        private bool _isCompleted;
        private double _progress;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; init; }

        public int Total { get; set; }

        public string Group
        {
            get => _group;
            set => SetField(ref _group, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetField(ref _progressText, value);
        }

        public string ElapsedText
        {
            get => _elapsedText;
            set => SetField(ref _elapsedText, value);
        }

        public string EtaText
        {
            get => _etaText;
            set => SetField(ref _etaText, value);
        }

        public string Detail
        {
            get => _detail;
            set => SetField(ref _detail, value);
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetField(ref _isCurrent, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetField(ref _isCompleted, value);
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

    public sealed class BenchmarkLegendItem
    {
        public BenchmarkLegendItem(string name, WpfBrush brush)
        {
            Name = name;
            Brush = brush;
        }

        public string Name { get; }

        public WpfBrush Brush { get; }
    }
}
