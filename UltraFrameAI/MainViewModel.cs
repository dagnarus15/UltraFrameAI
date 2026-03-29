using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PipelineService _pipeline;
    private readonly RelayCommand _browseRootCommand;
    private readonly RelayCommand _browseOutputCommand;
    private readonly RelayCommand _openOutputCommand;
    private readonly AsyncRelayCommand _resetRootCommand;
    private readonly RelayCommand _setLanguageCommand;
    private readonly RelayCommand _removeItemCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly ObservableCollection<string> _logLines = UiCollections.CreateLogCollection();
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _lastRootFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "last-root-folder.txt");
    private readonly string _recentRootFoldersPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "recent-root-folders.txt");

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _scanCts;
    private bool _isBusy;
    private string _rootFolder = Directory.GetCurrentDirectory();
    private string _outputFolder = string.Empty;
    private string _selectedCodec = "x264";
    private string _selectedTarget = "1080p";
    private string _ffmpegThreadsText = "0";
    private string _upscalerThreadsText = "4:4:4";
    private string _tileSizeText = "1024";
    private bool _overwrite;
    private bool _keepTemp;
    private string _statusSummary = string.Empty;
    private string _lastHeartbeat = string.Empty;
    private string _currentStage = string.Empty;
    private string _currentItemTitle = string.Empty;
    private string _currentItemDetail = string.Empty;
    private string _currentStatusLine = string.Empty;
    private string _elapsedText = "--:--:--";
    private string _etaText = "--:--:--";
    private string _stageDurationText = "--:--:--";
    private string _queueSummary = string.Empty;
    private string _currentFileName = string.Empty;
    private bool _isScanOverlayVisible;
    private double _scanProgress;
    private string _scanStatusText = string.Empty;
    private string _scanFolderText = string.Empty;
    private string _scanCurrentFileText = string.Empty;
    private string _scanFoundText = string.Empty;
    private string _scanDetailText = string.Empty;
    private int _scanFoundCount;
    private bool _isDropTargetActive;
    private bool _canDeleteSelected;
    private bool _canDeleteAll;
    private bool _isDeleteConfirmVisible;
    private double _overallProgress;
    private QueueItemViewModel? _selectedItem;
    private UiLanguage _currentLanguage;

    public MainViewModel()
    {
        _pipeline = new PipelineService();
        Items = new ObservableCollection<QueueItemViewModel>();
        RecentRootFolders = new ObservableCollection<RecentFolderItem>();
        CodecOptions = new[] { "x264", "x265" };
        TargetOptions = new[] { "1080p", "2160p" };

        _browseRootCommand = new RelayCommand(BrowseRoot);
        _browseOutputCommand = new RelayCommand(BrowseOutput);
        _openOutputCommand = new RelayCommand(OpenOutputFolder);
        _resetRootCommand = new AsyncRelayCommand(ResetToLastFolderAsync, () => !IsBusy);
        _setLanguageCommand = new RelayCommand(SetLanguage);
        _removeItemCommand = new RelayCommand(RemoveItem, CanRemoveItem);
        _cancelCommand = new RelayCommand(CancelRun, () => IsBusy);
        _scanCommand = new AsyncRelayCommand(() => ScanAsync(showOverlay: true), () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);

        LocalizedStrings.LanguageChanged += (_, _) => RefreshLocalizedText();
        _currentLanguage = LocalizedStrings.CurrentLanguage;
        LoadRecentRootFolders();
        RootFolder = LoadPersistedRootFolder(_repoRoot);
        RememberRecentFolder(RootFolder, persist: true);
        OutputFolder = Path.Combine(RootFolder, "x264_1080p");
        RefreshLocalizedText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<QueueItemViewModel> Items { get; }

    public ObservableCollection<RecentFolderItem> RecentRootFolders { get; }

    public ObservableCollection<string> LogLines => _logLines;

    public IEnumerable<string> CodecOptions { get; }

    public IEnumerable<string> TargetOptions { get; }

    public ICommand BrowseRootCommand => _browseRootCommand;

    public ICommand BrowseOutputCommand => _browseOutputCommand;

    public ICommand OpenOutputCommand => _openOutputCommand;

    public ICommand ResetRootCommand => _resetRootCommand;

    public ICommand SetLanguageCommand => _setLanguageCommand;

    public ICommand RemoveItemCommand => _removeItemCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand ScanCommand => _scanCommand;

    public ICommand StartCommand => _startCommand;


    public QueueItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                UpdateSelectionDetails();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _scanCommand.RaiseCanExecuteChanged();
                _startCommand.RaiseCanExecuteChanged();
                _cancelCommand.RaiseCanExecuteChanged();
                _resetRootCommand.RaiseCanExecuteChanged();
                _removeItemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RootFolder
    {
        get => _rootFolder;
        set
        {
            if (SetField(ref _rootFolder, value))
            {
                OutputFolder = Path.Combine(RootFolder, SelectedCodec == "x265" ? "x265_2160p" : "x264_1080p");
                UpdateQueueSummary();
                SavePersistedRootFolder(value);
                RefreshRecentFolderSelection();
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => SetField(ref _outputFolder, value);
    }

    public string SelectedCodec
    {
        get => _selectedCodec;
        set
        {
            if (SetField(ref _selectedCodec, value))
            {
                if (value == "x265")
                {
                    _selectedTarget = "2160p";
                    OnPropertyChanged(nameof(SelectedTarget));
                    OutputFolder = Path.Combine(RootFolder, "x265_2160p");
                }
                else
                {
                    _selectedTarget = "1080p";
                    OnPropertyChanged(nameof(SelectedTarget));
                    OutputFolder = Path.Combine(RootFolder, "x264_1080p");
                }
            }
        }
    }

    public string SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetField(ref _selectedTarget, value))
            {
                if (value == "2160p")
                {
                    _selectedCodec = "x265";
                    OnPropertyChanged(nameof(SelectedCodec));
                    OutputFolder = Path.Combine(RootFolder, "x265_2160p");
                }
                else
                {
                    _selectedCodec = "x264";
                    OnPropertyChanged(nameof(SelectedCodec));
                    OutputFolder = Path.Combine(RootFolder, "x264_1080p");
                }
            }
        }
    }

    public string FfmpegThreadsText
    {
        get => _ffmpegThreadsText;
        set => SetField(ref _ffmpegThreadsText, value);
    }

    public string UpscalerThreadsText
    {
        get => _upscalerThreadsText;
        set => SetField(ref _upscalerThreadsText, value);
    }

    public string TileSizeText
    {
        get => _tileSizeText;
        set => SetField(ref _tileSizeText, value);
    }

    public bool Overwrite
    {
        get => _overwrite;
        set => SetField(ref _overwrite, value);
    }

    public bool KeepTemp
    {
        get => _keepTemp;
        set => SetField(ref _keepTemp, value);
    }

    public string StatusSummary
    {
        get => _statusSummary;
        private set => SetField(ref _statusSummary, value);
    }

    public string LastHeartbeat
    {
        get => _lastHeartbeat;
        private set => SetField(ref _lastHeartbeat, value);
    }

    public string CurrentStage
    {
        get => _currentStage;
        private set => SetField(ref _currentStage, value);
    }

    public string CurrentItemTitle
    {
        get => _currentItemTitle;
        private set => SetField(ref _currentItemTitle, value);
    }

    public string CurrentItemDetail
    {
        get => _currentItemDetail;
        private set => SetField(ref _currentItemDetail, value);
    }

    public string CurrentStatusLine
    {
        get => _currentStatusLine;
        private set => SetField(ref _currentStatusLine, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetField(ref _elapsedText, value);
    }

    public string EtaText
    {
        get => _etaText;
        private set => SetField(ref _etaText, value);
    }

    public string StageDurationText
    {
        get => _stageDurationText;
        private set => SetField(ref _stageDurationText, value);
    }

    public string QueueSummary
    {
        get => _queueSummary;
        private set => SetField(ref _queueSummary, value);
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        private set => SetField(ref _currentFileName, value);
    }

    public bool IsScanOverlayVisible
    {
        get => _isScanOverlayVisible;
        private set => SetField(ref _isScanOverlayVisible, value);
    }

    public double ScanProgress
    {
        get => _scanProgress;
        private set => SetField(ref _scanProgress, value);
    }

    public string ScanStatusText
    {
        get => _scanStatusText;
        private set => SetField(ref _scanStatusText, value);
    }

    public string ScanFolderText
    {
        get => _scanFolderText;
        private set => SetField(ref _scanFolderText, value);
    }

    public string ScanCurrentFileText
    {
        get => _scanCurrentFileText;
        private set => SetField(ref _scanCurrentFileText, value);
    }

    public string ScanFoundText
    {
        get => _scanFoundText;
        private set => SetField(ref _scanFoundText, value);
    }

    public string ScanDetailText
    {
        get => _scanDetailText;
        private set => SetField(ref _scanDetailText, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetField(ref _overallProgress, value);
    }

    public string CurrentLanguageFlagPath => CurrentLanguage switch
    {
        UiLanguage.Russian => "pack://application:,,,/images/flag-ru.png",
        UiLanguage.German => "pack://application:,,,/images/flag-de.png",
        _ => "pack://application:,,,/images/flag-en.png"
    };

    public UiLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (SetField(ref _currentLanguage, value))
            {
                LocalizedStrings.SetLanguage(value);
                OnPropertyChanged(nameof(CurrentLanguageFlagPath));
            }
        }
    }

    public bool IsDropTargetActive
    {
        get => _isDropTargetActive;
        private set => SetField(ref _isDropTargetActive, value);
    }

    public bool CanDeleteSelected
    {
        get => _canDeleteSelected;
        private set => SetField(ref _canDeleteSelected, value);
    }

    public bool CanDeleteAll
    {
        get => _canDeleteAll;
        private set => SetField(ref _canDeleteAll, value);
    }

    public bool IsDeleteConfirmVisible
    {
        get => _isDeleteConfirmVisible;
        private set => SetField(ref _isDeleteConfirmVisible, value);
    }

    public async Task InitializeAsync()
    {
        RememberRecentFolder(RootFolder, persist: true);
        await ScanAsync(showOverlay: false).ConfigureAwait(true);
    }

    public async Task LoadRootFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        RootFolder = folder;
        OutputFolder = Path.Combine(RootFolder, SelectedCodec == "x265" ? "x265_2160p" : "x264_1080p");
        RememberRecentFolder(RootFolder, persist: true);
        await ScanAsync(showOverlay: true).ConfigureAwait(true);
    }

    public async Task ResetToLastFolderAsync()
    {
        await LoadRootFolderAsync(LoadPersistedRootFolder(_repoRoot)).ConfigureAwait(true);
    }

    private void SetLanguage(object? parameter)
    {
        if (parameter is UiLanguage language)
        {
            CurrentLanguage = language;
        }
        else if (parameter is string text && Enum.TryParse(text, true, out UiLanguage parsed))
        {
            CurrentLanguage = parsed;
        }
    }

    private bool CanRemoveItem(object? parameter) => !IsBusy && parameter is QueueItemViewModel;

    private void RemoveItem(object? parameter)
    {
        if (parameter is QueueItemViewModel item)
        {
            RemoveItems(new[] { item });
        }
    }

    public void SetDropTargetActive(bool active)
    {
        IsDropTargetActive = active;
    }

    public void SetDeleteSelectedEnabled(bool enabled)
    {
        CanDeleteSelected = enabled;
    }

    public void SetDeleteAllEnabled(bool enabled)
    {
        CanDeleteAll = enabled;
    }

    public void ShowDeleteAllConfirmation()
    {
        if (IsBusy || Items.Count == 0)
        {
            return;
        }

        IsDeleteConfirmVisible = true;
    }

    public void CancelDeleteConfirmation()
    {
        IsDeleteConfirmVisible = false;
    }

    public void ConfirmDeleteAll()
    {
        if (!IsDeleteConfirmVisible)
        {
            return;
        }

        var allItems = Items.ToArray();
        RemoveItems(allItems);
    }

    private async Task ScanAsync(bool showOverlay = true)
    {
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            if (showOverlay)
            {
                BeginScanOverlay(folder: string.IsNullOrWhiteSpace(RootFolder) ? _repoRoot : RootFolder);
            }
            Items.Clear();
            SetDeleteAllEnabled(false);
            Log(LocalizedStrings.LogScanningFiles);

            var folder = string.IsNullOrWhiteSpace(RootFolder) ? _repoRoot : RootFolder;
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException(LocalizedStrings.LogRootNotFound(folder));
            }

            var videos = await FindVideoFilesAsync(folder, showOverlay ? ReportScanProgress : null, _scanCts.Token).ConfigureAwait(true);

            var outputFolder = OutputFolder;
            Directory.CreateDirectory(outputFolder);
            var total = videos.Length;
            for (var i = 0; i < total; i++)
            {
                var video = videos[i];
                var baseName = Path.GetFileNameWithoutExtension(video);
                var videoDir = Path.GetDirectoryName(video) ?? folder;
                var relativeDir = Path.GetRelativePath(folder, videoDir);
                if (relativeDir == ".")
                {
                    relativeDir = string.Empty;
                }

                var relativeFile = string.IsNullOrWhiteSpace(relativeDir)
                    ? baseName
                    : Path.Combine(relativeDir, baseName);
                var displayTitle = string.IsNullOrWhiteSpace(relativeDir)
                    ? Path.GetFileName(video)
                    : Path.Combine(relativeDir, Path.GetFileName(video));

                var workName = "_work_" + SanitizePath(relativeFile);
                var workDir = Path.Combine(folder, workName);
                var srcDir = Path.Combine(workDir, "src");
                var upDir = Path.Combine(workDir, "up");
                var outputDir = string.IsNullOrWhiteSpace(relativeDir)
                    ? outputFolder
                    : Path.Combine(outputFolder, relativeDir);
                var suffix = SelectedCodec == "x265" ? "_2160p_x265.mkv" : "_1080p_x264.mkv";

                Items.Add(new QueueItemViewModel()
                {
                    Index = i + 1,
                    Title = displayTitle,
                    SourcePath = video,
                    OutputPath = Path.Combine(outputDir, baseName + suffix),
                    WorkPath = workDir,
                });
            }

            UpdateQueueSummary();
            SetDeleteAllEnabled(Items.Count > 0);
            Log(LocalizedStrings.LogFoundVideoFiles(total));
            if (Items.Count > 0)
            {
                SelectedItem = Items[0];
                UpdateSelectionDetails();
            }
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogScanFailed(ex.Message));
            System.Windows.MessageBox.Show(ex.Message, LocalizedStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndScanOverlay();
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private async Task<string[]> FindVideoFilesAsync(string folder, Action<int, int, int, string?>? progress, CancellationToken ct)
    {
        var ffprobePath = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe");
        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : Path.GetFullPath(OutputFolder);
        var candidates = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldSkipScanCandidate(path, outputFolder))
            .Where(IsLikelyVideoFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matches = new List<string>();
        var lastTick = Stopwatch.StartNew();
        var checkedCount = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ProcessRunner.CaptureLinesAsync(
                ffprobePath,
                $"-v error -select_streams v:0 -show_entries stream=index -of csv=p=0 {Quote(candidate)}",
                Path.GetDirectoryName(candidate) ?? folder,
                ct).ConfigureAwait(true);

            checkedCount++;
            if (result.Count > 0)
            {
                matches.Add(candidate);
            }

            if (progress is not null && (checkedCount == candidates.Length || lastTick.ElapsedMilliseconds >= 250))
            {
                progress(checkedCount, candidates.Length, matches.Count, Path.GetFileName(candidate));
                lastTick.Restart();
            }
        }

        progress?.Invoke(candidates.Length, candidates.Length, matches.Count, matches.Count > 0 ? Path.GetFileName(matches[^1]) : string.Empty);
        return matches.ToArray();
    }

    private async Task StartAsync()
    {
        if (Items.Count == 0)
        {
            await ScanAsync(showOverlay: true).ConfigureAwait(true);
            if (Items.Count == 0)
            {
                return;
            }
        }

        try
        {
            IsBusy = true;
            RememberRecentFolder(RootFolder, persist: true);
            _runCts = new CancellationTokenSource();
            ResetItemUi();
            Log(LocalizedStrings.LogStartingBatch);

            var options = BuildOptions();
            await _pipeline.RunAsync(Items, options, HandleProgress, _runCts.Token).ConfigureAwait(true);
            Log(LocalizedStrings.LogBatchFinished);
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogBatchFailed(ex.Message));
            System.Windows.MessageBox.Show(LocalizedStrings.LogBatchFailed(ex.Message), LocalizedStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsBusy = false;
        }
    }

    private PipelineOptions BuildOptions()
    {
        return new PipelineOptions
        {
            RootFolder = RootFolder,
            OutputFolder = OutputFolder,
            Overwrite = Overwrite,
            KeepTemp = KeepTemp,
            UseX265 = SelectedCodec == "x265",
            FfmpegThreads = ParseInt(FfmpegThreadsText, 0),
            UpscalerThreads = string.IsNullOrWhiteSpace(UpscalerThreadsText) ? "4:4:4" : UpscalerThreadsText,
            TileSize = ParseInt(TileSizeText, 1024),
            GpuId = null,
            FfmpegPath = FindFile("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe"),
            FfprobePath = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe"),
            UpscalerPath = FindFile("realesrgan-ncnn-vulkan.exe", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "realesrgan-ncnn-vulkan.exe")),
            ModelDir = FindDirectory("models", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "models")),
            UsePipeMode = true
        };
    }

    private void HandleProgress(PipelineProgress progress)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var index = progress.ItemIndex > 0 ? progress.ItemIndex - 1 : -1;
            if (index >= 0 && index < Items.Count)
            {
                var item = Items[index];
                item.Stage = progress.Stage;
                item.Progress = progress.Progress;
                item.ProgressText = progress.ProgressText;
                item.ElapsedText = progress.ElapsedText;
                item.EtaText = progress.EtaText;
                item.OutputState = progress.CurrentStatus;
                item.Detail = progress.CurrentDetail;
            }

            CurrentStage = progress.Stage;
            OverallProgress = progress.Progress;
            ElapsedText = progress.ElapsedText;
            EtaText = progress.EtaText;
            CurrentItemTitle = progress.ItemTitle;
            CurrentItemDetail = progress.CurrentDetail;
            CurrentStatusLine = progress.CurrentStatus;
            CurrentFileName = progress.ItemTitle;
            StatusSummary = progress.Summary;
            StageDurationText = progress.StageElapsedText;
            LastHeartbeat = progress.IsHeartbeat ? $"{DateTime.Now:HH:mm:ss} {progress.CurrentStatus}" : StatusSummary;

            if (!string.IsNullOrWhiteSpace(progress.LogLine))
            {
                Log(progress.LogLine);
            }
        });
    }

    private void BrowseRoot()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizedStrings.LogSelectVideoFolder,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = RootFolder
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _ = LoadRootFolderAsync(dialog.SelectedPath);
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizedStrings.LogSelectOutputFolder,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = OutputFolder
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private void OpenOutputFolder()
    {
        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
        }

        Process.Start(new ProcessStartInfo("explorer.exe", Quote(OutputFolder)) { UseShellExecute = true });
    }

    private void CancelRun()
    {
        _runCts?.Cancel();
        _scanCts?.Cancel();
    }

    private void UpdateQueueSummary()
    {
        QueueSummary = LocalizedStrings.LogItemCount(Items.Count);
    }

    private void UpdateSelectionDetails()
    {
        if (SelectedItem is null)
        {
            CurrentItemTitle = LocalizedStrings.LogNoItemSelected;
            CurrentItemDetail = LocalizedStrings.LogPickFolderHint;
            CurrentFileName = string.Empty;
            return;
        }

        CurrentItemTitle = SelectedItem.Title;
        CurrentItemDetail = SelectedItem.SourcePath;
        CurrentFileName = Path.GetFileName(SelectedItem.SourcePath);
    }

    private void BeginScanOverlay(string folder)
    {
        IsScanOverlayVisible = true;
        ScanProgress = 0;
        ScanStatusText = LocalizedStrings.ScanningFolder;
        ScanCurrentFileText = "-";
        _scanFoundCount = 0;
        ScanFoundText = LocalizedStrings.LogFoundVideoFiles(0);
        ScanFolderText = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ScanDetailText = folder;
    }

    private void ReportScanProgress(int checkedCount, int totalCount, int foundCount, string? currentFile)
    {
        ScanProgress = totalCount <= 0 ? 0 : Math.Min(100, checkedCount * 100.0 / totalCount);
        ScanStatusText = LocalizedStrings.ScanFolderProgress(checkedCount, totalCount);
        ScanCurrentFileText = string.IsNullOrWhiteSpace(currentFile) ? "-" : currentFile;
        _scanFoundCount = foundCount;
        ScanFoundText = LocalizedStrings.LogFoundVideoFiles(foundCount);
    }

    private void EndScanOverlay()
    {
        IsScanOverlayVisible = false;
    }

    private void ResetItemUi()
    {
        foreach (var item in Items)
        {
            item.ResetUiState();
        }
    }

    public void RemoveItems(IEnumerable<QueueItemViewModel> items)
    {
        if (IsBusy)
        {
            return;
        }

        var removed = items
            .Where(item => item is not null)
            .Distinct()
            .ToArray();
        if (removed.Length == 0)
        {
            return;
        }

        var selected = SelectedItem;
        var selectedIndex = selected is null ? -1 : Items.IndexOf(selected);
        var removedSelected = selected is not null && removed.Contains(selected);

        foreach (var item in removed)
        {
            TryDelete(item.WorkPath);
            Items.Remove(item);
        }

        RenumberItems();
        UpdateQueueSummary();
        SetDeleteAllEnabled(Items.Count > 0);

        if (Items.Count == 0)
        {
            SelectedItem = null;
        }
        else if (removedSelected)
        {
            var nextIndex = selectedIndex < 0 ? 0 : Math.Min(selectedIndex, Items.Count - 1);
            SelectedItem = Items[nextIndex];
        }
        else if (selected is not null && !Items.Contains(selected))
        {
            SelectedItem = Items[0];
        }

        UpdateSelectionDetails();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private void RenumberItems()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
        }
    }

    private void RefreshLocalizedText()
    {
        _currentLanguage = LocalizedStrings.CurrentLanguage;
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(CurrentLanguageFlagPath));

        if (Items.Count == 0)
        {
            StatusSummary = LocalizedStrings.LogReady;
            LastHeartbeat = LocalizedStrings.LogIdle;
            CurrentStage = LocalizedStrings.LogIdle;
            CurrentItemTitle = LocalizedStrings.LogNoItemSelected;
            CurrentItemDetail = LocalizedStrings.LogPickFolderHint;
            CurrentStatusLine = LocalizedStrings.LogWaitingForInput;
            QueueSummary = LocalizedStrings.LogItemCount(0);
            StageDurationText = "--:--:--";
            SetDeleteAllEnabled(false);
            CurrentFileName = string.Empty;
        }
        else
        {
            UpdateQueueSummary();
            UpdateSelectionDetails();
        }

        if (IsScanOverlayVisible)
        {
            ScanStatusText = LocalizedStrings.ScanningFolder;
            ScanFoundText = LocalizedStrings.LogFoundVideoFiles(_scanFoundCount);
        }
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _logLines.Add($"{DateTime.Now:HH:mm:ss} {message}");
            while (_logLines.Count > 400)
            {
                _logLines.RemoveAt(0);
            }
        });
    }

    private static int ParseInt(string? text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    private static string FindFile(string fileName, string fallback)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            fileName,
            fallback
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return fileName;
    }

    private static string FindDirectory(string directoryName, string fallback)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, directoryName),
            Path.Combine(Directory.GetCurrentDirectory(), directoryName),
            directoryName,
            fallback
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return directoryName;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "realesrgan-ncnn-vulkan-20220424")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private string LoadPersistedRootFolder(string fallback)
    {
        try
        {
            if (File.Exists(_lastRootFolderPath))
            {
                var persisted = File.ReadAllText(_lastRootFolderPath).Trim();
                if (!string.IsNullOrWhiteSpace(persisted) && Directory.Exists(persisted))
                {
                    return persisted;
                }
            }
        }
        catch
        {
        }

        return fallback;
    }

    private void LoadRecentRootFolders()
    {
        try
        {
            if (!File.Exists(_recentRootFoldersPath))
            {
                return;
            }

            foreach (var raw in File.ReadAllLines(_recentRootFoldersPath))
            {
                var folder = NormalizeFolderPath(raw);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                if (RecentRootFolders.Any(item => string.Equals(item.FolderPath, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                RecentRootFolders.Add(new RecentFolderItem(folder));
                if (RecentRootFolders.Count >= 10)
                {
                    break;
                }
            }
        }
        catch
        {
        }

        RefreshRecentFolderSelection();
    }

    private void RememberRecentFolder(string folder, bool persist)
    {
        var normalized = NormalizeFolderPath(folder);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
        {
            return;
        }

        if (RecentRootFolders.FirstOrDefault(item => string.Equals(item.FolderPath, normalized, StringComparison.OrdinalIgnoreCase)) is RecentFolderItem existing)
        {
            RecentRootFolders.Remove(existing);
        }

        RecentRootFolders.Insert(0, new RecentFolderItem(normalized));
        while (RecentRootFolders.Count > 10)
        {
            RecentRootFolders.RemoveAt(RecentRootFolders.Count - 1);
        }

        RefreshRecentFolderSelection();

        if (persist)
        {
            SaveRecentRootFolders();
        }
    }

    private void RefreshRecentFolderSelection()
    {
        foreach (var item in RecentRootFolders)
        {
            item.IsCurrent = string.Equals(item.FolderPath, RootFolder, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SavePersistedRootFolder(string folder)
    {
        try
        {
            var directory = Path.GetDirectoryName(_lastRootFolderPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_lastRootFolderPath, folder);
        }
        catch
        {
        }
    }

    private void SaveRecentRootFolders()
    {
        try
        {
            var directory = Path.GetDirectoryName(_recentRootFoldersPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(_recentRootFoldersPath, RecentRootFolders.Select(item => item.FolderPath));
        }
        catch
        {
        }
    }

    private static string NormalizeFolderPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(folder.Trim());
        }
        catch
        {
            return folder.Trim();
        }
    }

    private static bool ShouldSkipScanCandidate(string filePath, string? outputFolder)
    {
        var full = Path.GetFullPath(filePath);
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            var normalizedOutput = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(normalizedOutput + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, normalizedOutput, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var current = Path.GetDirectoryName(full);
        while (!string.IsNullOrWhiteSpace(current))
        {
            var leaf = Path.GetFileName(current);
            if (leaf.StartsWith("_work_", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("realesrgan-ncnn-vulkan", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool IsLikelyVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        switch (extension.ToLowerInvariant())
        {
            case ".mp4":
            case ".mkv":
            case ".mka":
            case ".mov":
            case ".avi":
            case ".wmv":
            case ".webm":
            case ".flv":
            case ".f4v":
            case ".m4v":
            case ".mpg":
            case ".mpeg":
            case ".mpe":
            case ".ts":
            case ".mts":
            case ".m2ts":
            case ".3gp":
            case ".3g2":
            case ".vob":
            case ".ogv":
            case ".ogm":
            case ".rm":
            case ".rmvb":
            case ".mxf":
            case ".asf":
            case ".divx":
            case ".dv":
            case ".dvr-ms":
            case ".wtv":
            case ".ivf":
            case ".amv":
            case ".yuv":
            case ".h264":
            case ".h265":
            case ".hevc":
            case ".m2v":
            case ".mpv":
            case ".vid":
                return true;
            default:
                return false;
        }
    }

    private static string SanitizePath(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Replace(Path.DirectorySeparatorChar, '_');
        name = name.Replace(Path.AltDirectorySeparatorChar, '_');

        return name;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
