using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private sealed record RecentRootFoldersCacheEntry(List<string> Folders, bool Complete);
    private sealed record AntiFlickerProfilesCacheEntry(Dictionary<string, AntiFlickerPresetState> Presets, bool Complete);
    private sealed record NativeEncoderBackendCacheEntry(bool Enabled, bool Complete);
    private sealed record AppSettingsCacheEntry(
        string RootFolder,
        string OutputFolder,
        string SelectedCodec,
        string SelectedTarget,
        string SelectedContainer,
        string EncoderPreset,
        string FfmpegThreadsText,
        string UpscalerThreadsText,
        string TileSizeText,
        bool Overwrite,
        string SelectedContentMode,
        string CurrentLanguage,
        bool? UseAntiFlicker,
        bool PreserveIncompleteOutput,
        bool UseNativeEncoderBackend,
        bool? RepairBrokenTimestamps,
        bool Complete);

    private readonly PipelineService _pipeline;
    private readonly RelayCommand _browseRootFolderCommand;
    private readonly RelayCommand _browseRootFileCommand;
    private readonly RelayCommand _browseOutputCommand;
    private readonly RelayCommand _openOutputCommand;
    private readonly AsyncRelayCommand _resetRootCommand;
    private readonly RelayCommand _setLanguageCommand;
    private readonly RelayCommand _setContentModeCommand;
    private readonly RelayCommand _removeItemCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _skipCurrentCommand;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly AsyncRelayCommand _startSelectedCommand;
    private readonly ObservableCollection<LogEntryViewModel> _logLines = UiCollections.CreateLogCollection();
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _lastRootFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "last-root-folder.txt");
    private readonly string _recentRootFoldersPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "recent-root-folders.txt");
    private readonly string _antiFlickerProfilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "anti-flicker-presets.json");
    private readonly string _nativeEncoderBackendPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "native-encoder-backend.json");
    private readonly string _appSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "app-settings.json");

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _scanCts;
    private readonly Dictionary<string, AntiFlickerPresetState> _antiFlickerPresets;
    private readonly HashSet<QueueItemViewModel> _attachedQueueItems = new();
    private bool _isBusy;
    private string _rootFolder = Directory.GetCurrentDirectory();
    private string _outputFolder = string.Empty;
    private string _selectedCodec = "x264";
    private string _selectedTarget = "1080p";
    private string _selectedContainer = "mkv";
    private string _encoderPreset = "slower";
    private string _ffmpegThreadsText = "0";
    private string _upscalerThreadsText = "4:4:4";
    private string _tileSizeText = "1024";
    private bool _overwrite;
    private bool _useAntiFlicker = true;
    private bool _useNativeEncoderBackend;
    private bool _preserveIncompleteOutput;
    private bool _repairBrokenTimestamps = true;
    private double _antiFlickerStrength = 65;
    private string _selectedContentMode = "Anime";
    private bool _suppressAntiFlickerPresetPersistence;
    private bool _suppressAppSettingsPersistence;
    private string _statusSummary = string.Empty;
    private string _lastHeartbeat = string.Empty;
    private string _currentStage = string.Empty;
    private string _currentItemTitle = string.Empty;
    private string _currentItemDetail = string.Empty;
    private string _currentStatusLine = string.Empty;
    private string _currentStageDisplayText = string.Empty;
    private string _elapsedText = "--:--:--";
    private string _etaText = "--:--:--";
    private string _stageDurationText = "--:--:--";
    private string _queueSummary = string.Empty;
    private string _currentFileName = string.Empty;
    private ImageSource? _renderPreviewOriginalImage;
    private ImageSource? _renderPreviewResultImage;
    private readonly object _renderPreviewSync = new();
    private RenderPreviewFrameUpdate? _pendingRenderPreviewOriginal;
    private RenderPreviewFrameUpdate? _pendingRenderPreviewResult;
    private bool _renderPreviewUpdateQueued;
    private double _renderPreviewZoom = 1.0;
    private double _renderPreviewPanX;
    private double _renderPreviewPanY;
    private int _currentItemIndex = -1;
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
    private bool _canStartAll;
    private bool _canStartSelected;
    private bool _isDeleteConfirmVisible;
    private double _overallProgress;
    private QueueItemViewModel? _selectedItem;
    private UiLanguage _currentLanguage;
    private readonly Dictionary<int, (int current, int total)> _sessionFrameProgress = new();
    private OutputConflictDecision? _sessionOutputDecision;
    private bool _isRenderMode;
    private string _lastRunProcessedFiles = "--";
    private string _lastRunElapsed = "--:--:--";
    private string _lastRunFps = "--";

    public MainViewModel()
    {
        _pipeline = new PipelineService();
        Items = new ObservableCollection<QueueItemViewModel>();
        CurrentRenderItems = new ObservableCollection<QueueItemViewModel>();
        CurrentRenderItems.CollectionChanged += CurrentRenderItems_CollectionChanged;
        RecentRootFolders = new ObservableCollection<RecentFolderItem>();
        CodecOptions = new[] { "x264", "x265" };
        TargetOptions = new[] { "1080p", "2160p" };
        ContainerOptions = new[] { "mkv" };
        EncoderPresetOptions = new[] { "fast", "medium", "slower" };

        _browseRootFolderCommand = new RelayCommand(BrowseRootFolder);
        _browseRootFileCommand = new RelayCommand(BrowseRootFile);
        _browseOutputCommand = new RelayCommand(BrowseOutput);
        _openOutputCommand = new RelayCommand(OpenOutputFolder);
        _resetRootCommand = new AsyncRelayCommand(ResetToLastFolderAsync, () => !IsBusy);
        _setLanguageCommand = new RelayCommand(SetLanguage);
        _setContentModeCommand = new RelayCommand(SetContentMode);
        _removeItemCommand = new RelayCommand(RemoveItem, CanRemoveItem);
        _cancelCommand = new RelayCommand(CancelRun, () => IsBusy);
        _skipCurrentCommand = new RelayCommand(SkipCurrentItem, () => IsBusy);
        _scanCommand = new AsyncRelayCommand(() => ScanAsync(showOverlay: true), () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
        _startSelectedCommand = new AsyncRelayCommand(StartSelectedAsync, () => !IsBusy);

        LocalizedStrings.LanguageChanged += (_, _) => RefreshLocalizedText();
        _currentLanguage = LocalizedStrings.CurrentLanguage;
        _antiFlickerPresets = LoadAntiFlickerPresets();
        _useNativeEncoderBackend = LoadNativeEncoderBackendPreference();
        LoadRecentRootFolders();
        _suppressAppSettingsPersistence = true;
        try
        {
            RootFolder = LoadPersistedRootFolder(_repoRoot);
            OutputFolder = GetDefaultOutputFolder(RootFolder);
            ApplyAntiFlickerPreset(_selectedContentMode, persist: false);
            LoadAppSettings();
        }
        finally
        {
            _suppressAppSettingsPersistence = false;
        }
        RememberRecentFolder(RootFolder, persist: true);
        RefreshLocalizedText();
        PersistAppSettings();
        _ = Task.Run(() => PipelineService.CleanupTempCaches(TimeSpan.FromDays(14)));
        LastRunProcessedFiles = "--";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? QueueStateChanged;
    public event Func<OutputConflictRequest, Task<OutputConflictDecision>>? OutputConflictRequested;

    public ObservableCollection<QueueItemViewModel> Items { get; }

    public ObservableCollection<QueueItemViewModel> CurrentRenderItems { get; }

    public string CurrentRenderItemsCountText => LocalizedStrings.LogItemCount(CurrentRenderItems.Count);

    public ObservableCollection<RecentFolderItem> RecentRootFolders { get; }

    public ObservableCollection<LogEntryViewModel> LogLines => _logLines;

    public IEnumerable<string> CodecOptions { get; }

    public IEnumerable<string> TargetOptions { get; }

    public IEnumerable<string> ContainerOptions { get; }

    public IEnumerable<string> EncoderPresetOptions { get; }

    public ICommand BrowseRootFolderCommand => _browseRootFolderCommand;

    public ICommand BrowseRootFileCommand => _browseRootFileCommand;

    public ICommand BrowseOutputCommand => _browseOutputCommand;

    public ICommand OpenOutputCommand => _openOutputCommand;

    public ICommand ResetRootCommand => _resetRootCommand;

    public ICommand SetLanguageCommand => _setLanguageCommand;

    public ICommand SetContentModeCommand => _setContentModeCommand;

    public ICommand RemoveItemCommand => _removeItemCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand SkipCurrentCommand => _skipCurrentCommand;

    public ICommand ScanCommand => _scanCommand;

    public ICommand StartCommand => _startCommand;

    public ICommand StartSelectedCommand => _startSelectedCommand;

    public bool CanStartAll
    {
        get => _canStartAll;
        private set => SetField(ref _canStartAll, value);
    }

    public bool CanStartSelected
    {
        get => _canStartSelected;
        private set => SetField(ref _canStartSelected, value);
    }


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
                _skipCurrentCommand.RaiseCanExecuteChanged();
                _resetRootCommand.RaiseCanExecuteChanged();
                _removeItemCommand.RaiseCanExecuteChanged();
                UpdateActionStates();
                OnQueueStateChanged();
            }
        }
    }

    public bool IsRenderMode
    {
        get => _isRenderMode;
        private set => SetField(ref _isRenderMode, value);
    }

    public string RootFolder
    {
        get => _rootFolder;
        set
        {
            if (SetField(ref _rootFolder, value))
            {
                OutputFolder = GetDefaultOutputFolder(value);
                UpdateQueueSummary();
                SavePersistedRootFolder(value);
                RefreshRecentFolderSelection();
                PersistAppSettings();
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetField(ref _outputFolder, value))
            {
                PersistAppSettings();
            }
        }
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
                    OutputFolder = GetDefaultOutputFolder(RootFolder);
                }
                else
                {
                    _selectedTarget = "1080p";
                    OnPropertyChanged(nameof(SelectedTarget));
                    OutputFolder = GetDefaultOutputFolder(RootFolder);
                }

                PersistAppSettings();
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
                    OutputFolder = GetDefaultOutputFolder(RootFolder);
                }
                else
                {
                    _selectedCodec = "x264";
                    OnPropertyChanged(nameof(SelectedCodec));
                    OutputFolder = GetDefaultOutputFolder(RootFolder);
                }

                PersistAppSettings();
            }
        }
    }

    public string SelectedContainer
    {
        get => _selectedContainer;
        set
        {
            if (SetField(ref _selectedContainer, value))
            {
                PersistAppSettings();
            }
        }
    }

    public string EncoderPreset
    {
        get => _encoderPreset;
        set
        {
            var normalized = NormalizeEncoderPreset(value);
            if (SetField(ref _encoderPreset, normalized))
            {
                PersistAppSettings();
            }
        }
    }

    public string FfmpegThreadsText
    {
        get => _ffmpegThreadsText;
        set
        {
            if (SetField(ref _ffmpegThreadsText, value))
            {
                PersistAppSettings();
            }
        }
    }

    public string UpscalerThreadsText
    {
        get => _upscalerThreadsText;
        set
        {
            if (SetField(ref _upscalerThreadsText, value))
            {
                PersistAppSettings();
            }
        }
    }

    public string TileSizeText
    {
        get => _tileSizeText;
        set
        {
            if (SetField(ref _tileSizeText, value))
            {
                PersistAppSettings();
            }
        }
    }

    public bool Overwrite
    {
        get => _overwrite;
        set
        {
            if (SetField(ref _overwrite, value))
            {
                PersistAppSettings();
            }
        }
    }

    public bool UseAntiFlicker
    {
        get => _useAntiFlicker;
        set
        {
            if (SetField(ref _useAntiFlicker, value))
            {
                PersistCurrentAntiFlickerPreset();
                PersistAppSettings();
            }
        }
    }

    public bool UseNativeEncoderBackend
    {
        get => _useNativeEncoderBackend;
        set
        {
            if (SetField(ref _useNativeEncoderBackend, value))
            {
                PersistNativeEncoderBackendPreference();
                PersistAppSettings();
            }
        }
    }

    public bool PreserveIncompleteOutput
    {
        get => _preserveIncompleteOutput;
        set
        {
            if (SetField(ref _preserveIncompleteOutput, value))
            {
                PersistAppSettings();
            }
        }
    }

    public bool RepairBrokenTimestamps
    {
        get => _repairBrokenTimestamps;
        set
        {
            if (SetField(ref _repairBrokenTimestamps, value))
            {
                PersistAppSettings();
            }
        }
    }

    public double AntiFlickerStrength
    {
        get => _antiFlickerStrength;
        set
        {
            if (SetField(ref _antiFlickerStrength, Math.Clamp(value, 0, 100)))
            {
                PersistCurrentAntiFlickerPreset();
            }
        }
    }

    public string SelectedContentMode
    {
        get => _selectedContentMode;
        set
        {
            var normalized = NormalizeContentMode(value);
            if (string.Equals(_selectedContentMode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SetField(ref _selectedContentMode, normalized))
            {
                ApplyAntiFlickerPreset(normalized, persist: false);
                PersistAllAntiFlickerPresets();
                PersistAppSettings();
            }
        }
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

    public string CurrentStageDisplayText
    {
        get => _currentStageDisplayText;
        private set => SetField(ref _currentStageDisplayText, value);
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

    public ImageSource? RenderPreviewOriginalImage
    {
        get => _renderPreviewOriginalImage;
        private set => SetField(ref _renderPreviewOriginalImage, value);
    }

    public ImageSource? RenderPreviewResultImage
    {
        get => _renderPreviewResultImage;
        private set => SetField(ref _renderPreviewResultImage, value);
    }

    public double RenderPreviewZoom
    {
        get => _renderPreviewZoom;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetField(ref _renderPreviewZoom, clamped))
            {
                if (Math.Abs(clamped - 1.0) < double.Epsilon)
                {
                    RenderPreviewPanX = 0;
                    RenderPreviewPanY = 0;
                }

                OnPropertyChanged(nameof(RenderPreviewZoomText));
                OnPropertyChanged(nameof(RenderPreviewCursor));
            }
        }
    }

    public string RenderPreviewZoomText => $"{RenderPreviewZoom * 100:0}%";

    public System.Windows.Input.Cursor RenderPreviewCursor => RenderPreviewZoom > 1.0
        ? System.Windows.Input.Cursors.SizeAll
        : System.Windows.Input.Cursors.Hand;

    public double RenderPreviewPanX
    {
        get => _renderPreviewPanX;
        set => SetField(ref _renderPreviewPanX, value);
    }

    public double RenderPreviewPanY
    {
        get => _renderPreviewPanY;
        set => SetField(ref _renderPreviewPanY, value);
    }

    public string LastRunProcessedFiles
    {
        get => _lastRunProcessedFiles;
        private set => SetField(ref _lastRunProcessedFiles, value);
    }

    public string LastRunElapsed
    {
        get => _lastRunElapsed;
        private set => SetField(ref _lastRunElapsed, value);
    }

    public string LastRunFps
    {
        get => _lastRunFps;
        private set => SetField(ref _lastRunFps, value);
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
                PersistAppSettings();
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

    public async Task LoadRootFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;

        RootFolder = path;
        OutputFolder = GetDefaultOutputFolder(path);
        RememberRecentFolder(RootFolder, persist: true);
        if (File.Exists(path))
        {
            await LoadSingleFileAsync(path).ConfigureAwait(true);
            return;
        }

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

    private void SetContentMode(object? parameter)
    {
        if (parameter is string mode && !string.IsNullOrWhiteSpace(mode))
        {
            SelectedContentMode = mode;
        }
    }

    private bool CanRemoveItem(object? parameter) => !IsBusy && parameter is QueueItemViewModel item && !item.IsBusy;

    private void RemoveItem(object? parameter)
    {
        if (parameter is QueueItemViewModel item && !item.IsBusy)
        {
            RemoveItems(new[] { item });
        }
    }

    public void SetDropTargetActive(bool active)
    {
        IsDropTargetActive = active;
    }

    public void UpdateActionStates()
    {
        var anySelected = Items.Any(item => item.IsChecked);
        var anyDeletable = Items.Any(item => !item.IsBusy);

        CanDeleteSelected = anySelected;
        CanDeleteAll = anyDeletable && !IsBusy;
        CanStartAll = !IsBusy && anyDeletable;
        CanStartSelected = !IsBusy && anySelected;
        _startCommand.RaiseCanExecuteChanged();
        _startSelectedCommand.RaiseCanExecuteChanged();
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

        var deletableItems = Items.Where(item => !item.IsBusy).ToArray();
        RemoveItems(deletableItems);
    }

    private async Task ScanAsync(bool showOverlay = true)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            var inputPath = string.IsNullOrWhiteSpace(RootFolder) ? _repoRoot : RootFolder;
            if (showOverlay)
            {
                BeginScanOverlay(inputPath);
            }
            Items.Clear();
            UpdateActionStates();
            Log(LocalizedStrings.LogScanningFiles);

            var scanRoot = GetScanRoot(inputPath);
            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException(LocalizedStrings.LogRootNotFound(inputPath));
            }

            await Task.Yield();
            var videos = await FindVideoFilesAsync(inputPath, showOverlay ? ReportScanProgress : null, _scanCts.Token).ConfigureAwait(true);

            var outputFolder = OutputFolder;
            Directory.CreateDirectory(outputFolder);
            var total = videos.Length;
            for (var i = 0; i < total; i++)
            {
                var video = videos[i];
                var baseName = Path.GetFileNameWithoutExtension(video);
                var videoDir = Path.GetDirectoryName(video) ?? scanRoot;
                var relativeDir = Path.GetRelativePath(scanRoot, videoDir);
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

                var outputDir = string.IsNullOrWhiteSpace(relativeDir)
                    ? outputFolder
                    : Path.Combine(outputFolder, relativeDir);
                var suffix = GetOutputSuffix();

                var item = new QueueItemViewModel()
                {
                    Index = i + 1,
                    Title = displayTitle,
                    SourcePath = video,
                    OutputPath = Path.Combine(outputDir, baseName + suffix),
                };
                AttachQueueItem(item);
                Items.Add(item);
            }

            UpdateQueueSummary();
            UpdateActionStates();
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
            PostToUi(() => System.Windows.MessageBox.Show(ex.Message, LocalizedStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            EndScanOverlay();
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private async Task<string[]> FindVideoFilesAsync(string inputPath, Action<int, int, int, string?>? progress, CancellationToken ct)
    {
        var ffprobePath = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe");
        var outputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : Path.GetFullPath(OutputFolder);
        var scanRoot = GetScanRoot(inputPath);
        var candidates = await Task.Run(() =>
        {
            if (File.Exists(inputPath))
            {
                return new[] { inputPath };
            }

            var list = new List<string>();
            foreach (var path in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipScanCandidate(path, outputFolder) || !IsLikelyVideoFile(path))
                {
                    continue;
                }

                list.Add(path);
            }

            return list
                .OrderBy(path => GetFolderDepth(path, scanRoot))
                .ThenBy(path => GetRelativeDirectory(path, scanRoot), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, ct).ConfigureAwait(false);

        var matches = new List<string>();
        var lastTick = Stopwatch.StartNew();
        var checkedCount = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ProcessRunner.CaptureLinesAsync(
                ffprobePath,
                $"-v error -select_streams v:0 -show_entries stream=index -of csv=p=0 {Quote(candidate)}",
                Path.GetDirectoryName(candidate) ?? GetScanRoot(inputPath),
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

    private static int GetFolderDepth(string path, string scanRoot)
    {
        var directory = Path.GetDirectoryName(path) ?? scanRoot;
        var relative = Path.GetRelativePath(scanRoot, directory);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return 0;
        }

        return relative.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar) + 1;
    }

    private static string GetRelativeDirectory(string path, string scanRoot)
    {
        var directory = Path.GetDirectoryName(path) ?? scanRoot;
        var relative = Path.GetRelativePath(scanRoot, directory);
        return relative == "." ? string.Empty : relative;
    }

    private Task StartAsync() => StartPipelineAsync(Items.Where(item => !item.IsBusy).ToArray(), LocalizedStrings.LogStartingBatch);

    private async Task StartSelectedAsync()
    {
        var selectedItems = Items.Where(item => item.IsChecked && !item.IsBusy).ToArray();
        if (selectedItems.Length == 0)
        {
            Log(LocalizedStrings.LogNoItemSelected);
            return;
        }

        await StartPipelineAsync(selectedItems, LocalizedStrings.LogStartingSelectedBatch).ConfigureAwait(true);
    }

    private async Task StartPipelineAsync(IReadOnlyList<QueueItemViewModel> runItems, string startMessage)
    {
        if (runItems.Count == 0)
        {
            CurrentRenderItems.Clear();
            Log(LocalizedStrings.LogNoItemsFound);
            return;
        }

        try
        {
            IsBusy = true;
            IsRenderMode = true;
            await Task.Yield();
            RememberRecentFolder(RootFolder, persist: true);
            _runCts = new CancellationTokenSource();
            ResetItemUi(runItems);
            CurrentRenderItems.Clear();
            foreach (var item in runItems)
            {
                CurrentRenderItems.Add(item);
            }
            ClearRenderPreviewPaths();
            Log(startMessage);
            _sessionOutputDecision = null;
            _sessionFrameProgress.Clear();
            var sessionWatch = Stopwatch.StartNew();

            var options = BuildOptions();
            var effectiveItems = await PrepareRunListAsync(runItems, options, _runCts.Token).ConfigureAwait(true);
            if (effectiveItems.Count > 0)
            {
                await _pipeline.RunAsync(effectiveItems, options, HandleProgress, HandleRenderPreviewFrame, _runCts.Token).ConfigureAwait(true);
            }
            Log(LocalizedStrings.LogBatchFinished);
            sessionWatch.Stop();
            UpdateLastRunSummary(sessionWatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogBatchFailed(ex.Message));
            PostToUi(() => System.Windows.MessageBox.Show(LocalizedStrings.LogBatchFailed(ex.Message), LocalizedStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsBusy = false;
            IsRenderMode = false;
            ClearRenderPreviewPaths();
            CurrentRenderItems.Clear();
        }
    }

    public void NotifyQueueSelectionChanged()
    {
        UpdateActionStates();
        _removeItemCommand.RaiseCanExecuteChanged();
        OnQueueStateChanged();
    }

    private PipelineOptions BuildOptions()
    {
        return new PipelineOptions
        {
            RootFolder = RootFolder,
            OutputFolder = OutputFolder,
            Overwrite = Overwrite,
            UseX265 = SelectedCodec == "x265",
            OutputContainer = SelectedContainer,
            FfmpegThreads = ParseInt(FfmpegThreadsText, 0),
            UpscalerThreads = string.IsNullOrWhiteSpace(UpscalerThreadsText) ? "4:4:4" : UpscalerThreadsText,
            TileSize = ParseInt(TileSizeText, 1024),
            GpuId = null,
            FfmpegPath = FindFile("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe"),
            FfprobePath = FindFile("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe"),
            UpscalerPath = FindFile("realesrgan-ncnn-vulkan.exe", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "realesrgan-ncnn-vulkan.exe")),
            ModelDir = FindDirectory("models", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "models")),
            UseAntiFlicker = UseAntiFlicker,
            ContentMode = SelectedContentMode,
            AntiFlickerStrength = AntiFlickerStrength,
            EncoderPreset = EncoderPreset,
            UseNativeEncoderBackend = UseNativeEncoderBackend && NativeFrameEncoderBridge.IsAvailable(),
            PreserveIncompleteOutput = PreserveIncompleteOutput,
            RepairBrokenTimestamps = RepairBrokenTimestamps
        };
    }

    private void HandleProgress(PipelineProgress progress)
    {
        PostToUi(() =>
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
                item.IsBusy = progress.Progress > 0 && progress.Progress < 100;
                item.OutputState = progress.CurrentStatus;
                item.Detail = progress.CurrentDetail;
                _removeItemCommand.RaiseCanExecuteChanged();
            }

            CurrentStage = progress.Stage;
            OverallProgress = progress.Progress;
            ElapsedText = progress.ElapsedText;
            EtaText = progress.EtaText;
            CurrentItemTitle = progress.ItemTitle;
            CurrentItemDetail = progress.CurrentDetail;
            CurrentStatusLine = progress.CurrentStatus;
            _currentItemIndex = progress.ItemIndex;
            UpdateCurrentStageDisplayText();
            CurrentFileName = progress.ItemTitle;
            StatusSummary = progress.Summary;
            StageDurationText = progress.StageElapsedText;
            LastHeartbeat = progress.IsHeartbeat ? $"{DateTime.Now:HH:mm:ss} {progress.CurrentStatus}" : StatusSummary;
            UpdateRenderPreviewIndicators(progress.EtaText, progress.CurrentStatus);

            TrackFrameProgress(progress);

            if (!string.IsNullOrWhiteSpace(progress.LogLine))
            {
                Log(progress.LogLine);
            }

            OnQueueStateChanged();
        });
    }

    private void SkipCurrentItem()
    {
        if (!IsBusy)
        {
            return;
        }

        var current = GetCurrentItem();
        if (current is null)
        {
            return;
        }

        current.SkipRequested = true;
        current.IsInterrupted = true;
        Log(LocalizedStrings.LogSkippingEncode);
    }

    private QueueItemViewModel? GetCurrentItem()
    {
        if (_currentItemIndex > 0)
        {
            var byIndex = Items.FirstOrDefault(item => item.Index == _currentItemIndex);
            if (byIndex is not null)
            {
                return byIndex;
            }
        }

        if (string.IsNullOrWhiteSpace(CurrentItemTitle))
        {
            return null;
        }

        return Items.FirstOrDefault(item => string.Equals(item.Title, CurrentItemTitle, StringComparison.OrdinalIgnoreCase));
    }

    private void TrackFrameProgress(PipelineProgress progress)
    {
        if (progress.ItemIndex <= 0)
        {
            return;
        }

        if (!TryParseFrameProgress(progress.CurrentDetail, out var current, out var total))
        {
            return;
        }

        _sessionFrameProgress[progress.ItemIndex] = (current, total);
    }

    private static bool TryParseFrameProgress(string text, out int current, out int total)
    {
        current = 0;
        total = 0;
        var parts = text.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        return int.TryParse(parts[0], out current) && int.TryParse(parts[1], out total) && total > 0;
    }

    private void UpdateLastRunSummary(TimeSpan elapsed)
    {
        LastRunElapsed = elapsed.ToString(@"hh\:mm\:ss");
        var totalFrames = _sessionFrameProgress.Values.Sum(v => v.total);
        var processedFrames = _sessionFrameProgress.Values.Sum(v => Math.Min(v.current, v.total));
        var processedFiles = _sessionFrameProgress.Count;
        if (totalFrames <= 0 || elapsed.TotalSeconds <= 0)
        {
            LastRunFps = "--";
        }
        else
        {
            var fps = processedFrames / elapsed.TotalSeconds;
            LastRunFps = fps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        LastRunProcessedFiles = processedFiles > 0
            ? processedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "0";
    }

    private async Task<List<QueueItemViewModel>> PrepareRunListAsync(IReadOnlyList<QueueItemViewModel> items, PipelineOptions options, CancellationToken ct)
    {
        var list = new List<QueueItemViewModel>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            item.SkipRequested = false;
            item.ForceOverwrite = false;
            item.IsSkipped = false;
            item.IsInterrupted = false;

            var overwriteAllowed = options.Overwrite;
            if (File.Exists(item.OutputPath) && !overwriteAllowed)
            {
                var decision = await ResolveOutputConflictAsync(item).ConfigureAwait(true);
                switch (decision)
                {
                    case OutputConflictDecision.ReplaceAll:
                        _sessionOutputDecision = OutputConflictDecision.ReplaceAll;
                        item.ForceOverwrite = true;
                        list.Add(item);
                        continue;
                    case OutputConflictDecision.Replace:
                        item.ForceOverwrite = true;
                        list.Add(item);
                        continue;
                    case OutputConflictDecision.SkipAll:
                        _sessionOutputDecision = OutputConflictDecision.SkipAll;
                        MarkItemSkipped(item);
                        continue;
                    case OutputConflictDecision.Skip:
                        MarkItemSkipped(item);
                        continue;
                    case OutputConflictDecision.Cancel:
                        throw new OperationCanceledException();
                }
            }

            list.Add(item);
        }

        return list;
    }

    private async Task<OutputConflictDecision> ResolveOutputConflictAsync(QueueItemViewModel item)
    {
        if (_sessionOutputDecision == OutputConflictDecision.SkipAll ||
            _sessionOutputDecision == OutputConflictDecision.ReplaceAll)
        {
            return _sessionOutputDecision.Value;
        }

        var handler = OutputConflictRequested;
        if (handler is null)
        {
            return OutputConflictDecision.Skip;
        }

        var request = new OutputConflictRequest(item, item.SourcePath, item.OutputPath);
        var delegates = handler.GetInvocationList();
        if (delegates.Length == 0)
        {
            return OutputConflictDecision.Skip;
        }

        var result = await ((Func<OutputConflictRequest, Task<OutputConflictDecision>>)delegates[0]).Invoke(request).ConfigureAwait(true);
        if (result is OutputConflictDecision.SkipAll or OutputConflictDecision.ReplaceAll)
        {
            _sessionOutputDecision = result;
        }

        return result;
    }

    private void MarkItemSkipped(QueueItemViewModel item)
    {
        item.IsSkipped = true;
        item.IsInterrupted = true;
        item.Stage = LocalizedStrings.LogSkippingEncode;
        item.Progress = 100;
        item.ProgressText = "100%";
        item.OutputState = LocalizedStrings.LogOutputExists;
        item.Detail = Path.GetFileName(item.OutputPath);
    }

    private void BrowseRootFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizedStrings.LogSelectVideoFolder,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = GetInputDirectory(RootFolder)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _ = LoadRootFolderAsync(dialog.SelectedPath);
        }
    }

    private void BrowseRootFile()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = LocalizedStrings.BrowseVideoFileDialogTitle,
            InitialDirectory = GetScanRoot(RootFolder),
            Filter = LocalizedStrings.BrowseVideoFilesFilter,
            Multiselect = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            _ = LoadRootFolderAsync(dialog.FileName);
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
            CurrentItemDetail = string.Empty;
            CurrentFileName = string.Empty;
            ClearRenderPreviewPaths();
            return;
        }

        CurrentItemTitle = SelectedItem.Title;
        CurrentItemDetail = SelectedItem.SourcePath;
        CurrentFileName = Path.GetFileName(SelectedItem.SourcePath);
        ClearRenderPreviewPaths();
    }

    public void HandleRenderPreviewFrame(RenderPreviewFrameUpdate frame)
    {
        var schedule = false;
        lock (_renderPreviewSync)
        {
            if (_currentItemIndex > 0 && frame.ItemIndex != _currentItemIndex)
            {
                frame.Payload.Dispose();
                return;
            }

            var pending = frame.IsOriginal ? _pendingRenderPreviewOriginal : _pendingRenderPreviewResult;
            pending?.Payload.Dispose();
            if (frame.IsOriginal)
            {
                _pendingRenderPreviewOriginal = frame;
            }
            else
            {
                _pendingRenderPreviewResult = frame;
            }
            if (_renderPreviewUpdateQueued)
            {
                return;
            }

            _renderPreviewUpdateQueued = true;
            schedule = true;
        }

        if (schedule)
        {
            PostToUi(ApplyPendingRenderPreviewFrames);
        }
    }

    private void ApplyPendingRenderPreviewFrames()
    {
        RenderPreviewFrameUpdate? original;
        RenderPreviewFrameUpdate? result;

        lock (_renderPreviewSync)
        {
            original = _pendingRenderPreviewOriginal;
            result = _pendingRenderPreviewResult;
            _pendingRenderPreviewOriginal = null;
            _pendingRenderPreviewResult = null;
        }

        try
        {
            ApplyRenderPreviewFrame(original);
            ApplyRenderPreviewFrame(result);
            UpdateRenderPreviewIndicators();
        }
        finally
        {
            var reschedule = false;
            lock (_renderPreviewSync)
            {
                _renderPreviewUpdateQueued = false;
                if (_pendingRenderPreviewOriginal is not null || _pendingRenderPreviewResult is not null)
                {
                    _renderPreviewUpdateQueued = true;
                    reschedule = true;
                }
            }

            original?.Payload.Dispose();
            result?.Payload.Dispose();

            if (reschedule)
            {
                PostToUi(ApplyPendingRenderPreviewFrames);
            }
        }
    }

    private void ApplyRenderPreviewFrame(RenderPreviewFrameUpdate? frame)
    {
        if (frame is null)
        {
            return;
        }

        var buffer = frame.Payload.Buffer;
        if (frame.Width <= 0 || frame.Height <= 0 || buffer is null || buffer.Length == 0)
        {
            return;
        }

        try
        {
            var pixels = frame.Payload.Span.ToArray();
            var bitmap = BitmapSource.Create(
                frame.Width,
                frame.Height,
                96,
                96,
                PixelFormats.Bgr24,
                null,
                pixels,
                frame.Stride);
            bitmap.Freeze();
            if (frame.IsOriginal)
            {
                RenderPreviewOriginalImage = bitmap;
            }
            else
            {
                RenderPreviewResultImage = bitmap;
            }
        }
        catch
        {
        }
    }


    private void ClearRenderPreviewPaths(bool resetTransforms = true)
    {
        RenderPreviewOriginalImage = null;
        RenderPreviewResultImage = null;
        _currentItemIndex = -1;
        if (resetTransforms)
        {
            RenderPreviewZoom = 1.0;
            RenderPreviewPanX = 0;
            RenderPreviewPanY = 0;
        }
    }

    private void UpdateRenderPreviewIndicators(string? etaText = null, string? currentStatus = null)
    {
    }

    private void BeginScanOverlay(string folder)
    {
        IsScanOverlayVisible = true;
        ScanProgress = 0;
        ScanStatusText = LocalizedStrings.ScanningFolder;
        ScanCurrentFileText = "-";
        _scanFoundCount = 0;
        ScanFoundText = LocalizedStrings.LogFoundVideoFiles(0);
        var folderLabel = GetInputDirectory(folder);
        ScanFolderText = Path.GetFileName(folderLabel.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ScanDetailText = folder;
    }

    private void ReportScanProgress(int checkedCount, int totalCount, int foundCount, string? currentFile)
    {
        void Apply()
        {
            ScanProgress = totalCount <= 0 ? 0 : Math.Min(100, checkedCount * 100.0 / totalCount);
            ScanStatusText = LocalizedStrings.ScanFolderProgress(checkedCount, totalCount);
            ScanCurrentFileText = string.IsNullOrWhiteSpace(currentFile) ? "-" : currentFile;
            _scanFoundCount = foundCount;
            ScanFoundText = LocalizedStrings.LogFoundVideoFiles(foundCount);
        }

        PostToUi(Apply);
    }

    private void EndScanOverlay()
    {
        IsScanOverlayVisible = false;
    }

    private void ResetItemUi(IEnumerable<QueueItemViewModel> items)
    {
        foreach (var item in items)
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
            .Where(item => item is not null && !item.IsBusy)
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
            Items.Remove(item);
        }

        RenumberItems();
        UpdateQueueSummary();
        UpdateActionStates();
        OnQueueStateChanged();

        if (Items.Count == 0)
        {
            SelectedItem = null;
            ClearRenderPreviewPaths();
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
        OnQueueStateChanged();
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
            CurrentItemDetail = string.Empty;
            CurrentStatusLine = LocalizedStrings.LogWaitingForInput;
            UpdateCurrentStageDisplayText();
            QueueSummary = LocalizedStrings.LogItemCount(0);
            StageDurationText = "--:--:--";
            UpdateActionStates();
            CurrentFileName = string.Empty;
        }
        else
        {
            UpdateQueueSummary();
            UpdateSelectionDetails();
            UpdateActionStates();
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

        PostToUi(() =>
        {
            _logLines.Add(new LogEntryViewModel(DateTime.Now.ToString("HH:mm:ss"), message));
            while (_logLines.Count > 400)
            {
                _logLines.RemoveAt(0);
            }
        });
    }

    private void OnQueueStateChanged()
    {
        QueueStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CurrentRenderItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentRenderItemsCountText));
    }

    private void AttachQueueItem(QueueItemViewModel item)
    {
        if (_attachedQueueItems.Add(item))
        {
            item.PropertyChanged += QueueItem_PropertyChanged;
        }
    }

    private void UpdateCurrentStageDisplayText()
    {
        var stage = CurrentStage?.Trim() ?? string.Empty;
        var phase = CurrentStatusLine?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stage))
        {
            CurrentStageDisplayText = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase;
            return;
        }

        if (string.IsNullOrWhiteSpace(phase) || string.Equals(stage, phase, StringComparison.OrdinalIgnoreCase))
        {
            CurrentStageDisplayText = stage;
            return;
        }

        CurrentStageDisplayText = $"{stage} ({phase})";
    }

    private void QueueItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not QueueItemViewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(QueueItemViewModel.IsChecked) or nameof(QueueItemViewModel.IsBusy))
        {
            PostToUi(() =>
            {
                UpdateActionStates();
                _removeItemCommand.RaiseCanExecuteChanged();
                OnQueueStateChanged();
            });
        }
    }

    private static void PostToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
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
                if (!string.IsNullOrWhiteSpace(persisted) && (Directory.Exists(persisted) || File.Exists(persisted)))
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

    private void LoadAppSettings()
    {
        var restoreSuppression = !_suppressAppSettingsPersistence;
        if (restoreSuppression)
        {
            _suppressAppSettingsPersistence = true;
        }

        try
        {
            if (!File.Exists(_appSettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(_appSettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettingsCacheEntry>(json);
            if (loaded is null || !loaded.Complete)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(loaded.RootFolder) && (Directory.Exists(loaded.RootFolder) || File.Exists(loaded.RootFolder)))
            {
                RootFolder = loaded.RootFolder;
            }

            var target = loaded.SelectedTarget == "2160p" ? "2160p" : "1080p";
            var codec = loaded.SelectedCodec == "x265" ? "x265" : "x264";
            const string container = "mkv";

            SelectedTarget = target;
            SelectedCodec = codec;
            SelectedContainer = container;
            EncoderPreset = NormalizeEncoderPreset(loaded.EncoderPreset);

            if (!string.IsNullOrWhiteSpace(loaded.FfmpegThreadsText))
            {
                FfmpegThreadsText = loaded.FfmpegThreadsText;
            }

            if (!string.IsNullOrWhiteSpace(loaded.UpscalerThreadsText))
            {
                UpscalerThreadsText = loaded.UpscalerThreadsText;
            }

            if (!string.IsNullOrWhiteSpace(loaded.TileSizeText))
            {
                TileSizeText = loaded.TileSizeText;
            }

            Overwrite = loaded.Overwrite;

            if (!string.IsNullOrWhiteSpace(loaded.CurrentLanguage) && Enum.TryParse(loaded.CurrentLanguage, true, out UiLanguage parsedLanguage))
            {
                CurrentLanguage = parsedLanguage;
            }

            if (!string.IsNullOrWhiteSpace(loaded.SelectedContentMode))
            {
                SelectedContentMode = loaded.SelectedContentMode;
            }

            if (loaded.UseAntiFlicker is bool useAntiFlicker)
            {
                UseAntiFlicker = useAntiFlicker;
            }

            PreserveIncompleteOutput = loaded.PreserveIncompleteOutput;
            UseNativeEncoderBackend = loaded.UseNativeEncoderBackend;
            RepairBrokenTimestamps = loaded.RepairBrokenTimestamps ?? true;

            if (!string.IsNullOrWhiteSpace(loaded.OutputFolder))
            {
                OutputFolder = loaded.OutputFolder;
            }
        }
        catch
        {
        }
        finally
        {
            if (restoreSuppression)
            {
                _suppressAppSettingsPersistence = false;
                PersistAppSettings();
            }
        }
    }

    private async Task LoadSingleFileAsync(string filePath)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            Items.Clear();

            await Task.Yield();
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(LocalizedStrings.LogRootNotFound(filePath), filePath);
            }

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var scanRoot = GetScanRoot(filePath);
            var suffix = GetOutputSuffix();
            var outputFolder = OutputFolder;
            Directory.CreateDirectory(outputFolder);

        var outputPath = Path.Combine(outputFolder, baseName + suffix);

        var item = new QueueItemViewModel
        {
            Index = 1,
            Title = Path.GetFileName(filePath),
            SourcePath = filePath,
            OutputPath = outputPath
        };
        AttachQueueItem(item);
        Items.Add(item);

            UpdateQueueSummary();
            UpdateActionStates();
            Log(LocalizedStrings.LogFoundVideoFiles(1));
            SelectedItem = Items[0];
            ClearRenderPreviewPaths();
            UpdateSelectionDetails();
            OnQueueStateChanged();
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogScanFailed(ex.Message));
            PostToUi(() => System.Windows.MessageBox.Show(ex.Message, LocalizedStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void LoadRecentRootFolders()
    {
        try
        {
            if (!File.Exists(_recentRootFoldersPath))
            {
                return;
            }

            var json = File.ReadAllText(_recentRootFoldersPath);
            var loaded = JsonSerializer.Deserialize<RecentRootFoldersCacheEntry>(json);
            if (loaded is not null && loaded.Complete)
            {
                foreach (var raw in loaded.Folders)
                {
                    AddRecentRootFolder(raw);
                }

                RefreshRecentFolderSelection();
                return;
            }

            foreach (var raw in File.ReadAllLines(_recentRootFoldersPath))
            {
                AddRecentRootFolder(raw);
            }
        }
        catch
        {
        }

        RefreshRecentFolderSelection();
    }

    private void AddRecentRootFolder(string raw)
    {
        var inputPath = NormalizeInputPath(raw);
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
        {
            return;
        }

        if (RecentRootFolders.Any(item => string.Equals(item.FolderPath, inputPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RecentRootFolders.Add(new RecentFolderItem(inputPath));
        if (RecentRootFolders.Count >= 10)
        {
            return;
        }
    }

    private void RememberRecentFolder(string folder, bool persist)
    {
        var normalized = NormalizeInputPath(folder);
        if (string.IsNullOrWhiteSpace(normalized) || (!Directory.Exists(normalized) && !File.Exists(normalized)))
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
        var current = NormalizeInputPath(RootFolder);
        foreach (var item in RecentRootFolders)
        {
            item.IsCurrent = string.Equals(NormalizeInputPath(item.FolderPath), current, StringComparison.OrdinalIgnoreCase);
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

            WriteAtomicText(_lastRootFolderPath, folder);
        }
        catch
        {
        }
    }

    private void PersistAppSettings()
    {
        if (_suppressAppSettingsPersistence)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_appSettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new AppSettingsCacheEntry(
                RootFolder,
                OutputFolder,
                SelectedCodec,
                SelectedTarget,
                SelectedContainer,
                EncoderPreset,
                FfmpegThreadsText,
                UpscalerThreadsText,
                TileSizeText,
                Overwrite,
                SelectedContentMode,
                CurrentLanguage.ToString(),
                UseAntiFlicker,
                PreserveIncompleteOutput,
                UseNativeEncoderBackend,
                RepairBrokenTimestamps,
                true);

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomicText(_appSettingsPath, json);
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

            var entry = new RecentRootFoldersCacheEntry(RecentRootFolders.Select(item => item.FolderPath).ToList(), true);
            WriteAtomicText(_recentRootFoldersPath, JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private string GetDefaultOutputFolder(string inputPath)
    {
        var baseDirectory = GetInputDirectory(inputPath);
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            baseDirectory = _repoRoot;
        }

        return Path.Combine(baseDirectory, $"{SelectedCodec}_{SelectedTarget}");
    }

    private string GetOutputSuffix()
    {
        var resolution = SelectedTarget == "2160p" ? "2160p" : "1080p";
        var codec = SelectedCodec == "x265" ? "x265" : "x264";
        var extension = ".mkv";
        return $"_{resolution}_{codec}{extension}";
    }

    private static string GetInputDirectory(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Directory.GetCurrentDirectory();
        }

        if (File.Exists(inputPath))
        {
            return Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        }

        if (Directory.Exists(inputPath))
        {
            return inputPath;
        }

        try
        {
            var full = Path.GetFullPath(inputPath);
            return Path.GetDirectoryName(full) ?? full;
        }
        catch
        {
            return Directory.GetCurrentDirectory();
        }
    }

    private static string GetScanRoot(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        }

        if (Directory.Exists(inputPath))
        {
            return inputPath;
        }

        return GetInputDirectory(inputPath);
    }

    private static string NormalizeEncoderPreset(string? preset)
    {
        return preset?.Trim().ToLowerInvariant() switch
        {
            "fast" => "fast",
            "medium" => "medium",
            "slower" => "slower",
            _ => "slower"
        };
    }

    private void ApplyAntiFlickerPreset(string mode, bool persist)
    {
        var normalized = NormalizeContentMode(mode);
        var preset = GetAntiFlickerPreset(normalized);

        _suppressAntiFlickerPresetPersistence = true;
        try
        {
            UseAntiFlicker = preset.Enabled;
            AntiFlickerStrength = preset.Strength;
        }
        finally
        {
            _suppressAntiFlickerPresetPersistence = false;
        }

        if (persist)
        {
            PersistCurrentAntiFlickerPreset();
        }
    }

    private void PersistCurrentAntiFlickerPreset()
    {
        if (_suppressAntiFlickerPresetPersistence)
        {
            return;
        }

        var normalized = NormalizeContentMode(_selectedContentMode);
        _antiFlickerPresets[normalized] = new AntiFlickerPresetState
        {
            Enabled = UseAntiFlicker,
            Strength = AntiFlickerStrength
        };
        PersistAllAntiFlickerPresets();
    }

    private void PersistAllAntiFlickerPresets()
    {
        try
        {
            var directory = Path.GetDirectoryName(_antiFlickerProfilesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new AntiFlickerProfilesCacheEntry(new Dictionary<string, AntiFlickerPresetState>(_antiFlickerPresets), true);
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomicText(_antiFlickerProfilesPath, json);
        }
        catch
        {
        }
    }

    private bool LoadNativeEncoderBackendPreference()
    {
        try
        {
            if (!File.Exists(_nativeEncoderBackendPath))
            {
                return false;
            }

            var json = File.ReadAllText(_nativeEncoderBackendPath);
            var entry = JsonSerializer.Deserialize<NativeEncoderBackendCacheEntry>(json);
            return entry is not null && entry.Complete && entry.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private void PersistNativeEncoderBackendPreference()
    {
        try
        {
            var directory = Path.GetDirectoryName(_nativeEncoderBackendPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new NativeEncoderBackendCacheEntry(UseNativeEncoderBackend, true);
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomicText(_nativeEncoderBackendPath, json);
        }
        catch
        {
        }
    }

    private static void WriteAtomicText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, true);
    }

    private Dictionary<string, AntiFlickerPresetState> LoadAntiFlickerPresets()
    {
        var presets = CreateDefaultAntiFlickerPresets();
        try
        {
            if (File.Exists(_antiFlickerProfilesPath))
            {
                var json = File.ReadAllText(_antiFlickerProfilesPath);
                var loaded = JsonSerializer.Deserialize<AntiFlickerProfilesCacheEntry>(json);
                if (loaded is not null && loaded.Complete)
                {
                    ApplyLoadedAntiFlickerPresets(presets, loaded.Presets);
                }
                else
                {
                    var legacyLoaded = JsonSerializer.Deserialize<Dictionary<string, AntiFlickerPresetState>>(json);
                    if (legacyLoaded is not null)
                    {
                        ApplyLoadedAntiFlickerPresets(presets, legacyLoaded);
                    }
                }
            }
        }
        catch
        {
        }

        return presets;
    }

    private static void ApplyLoadedAntiFlickerPresets(
        Dictionary<string, AntiFlickerPresetState> presets,
        IReadOnlyDictionary<string, AntiFlickerPresetState> loaded)
    {
        foreach (var pair in loaded)
        {
            var mode = NormalizeContentMode(pair.Key);
            if (string.IsNullOrWhiteSpace(mode))
            {
                continue;
            }

            presets[mode] = new AntiFlickerPresetState
            {
                Enabled = pair.Value.Enabled,
                Strength = Math.Clamp(pair.Value.Strength, 0, 100)
            };
        }
    }

    private static Dictionary<string, AntiFlickerPresetState> CreateDefaultAntiFlickerPresets()
    {
        return new Dictionary<string, AntiFlickerPresetState>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anime"] = new AntiFlickerPresetState { Enabled = true, Strength = 70 },
            ["AnimeUltra"] = new AntiFlickerPresetState { Enabled = true, Strength = 88 },
            ["Video"] = new AntiFlickerPresetState { Enabled = true, Strength = 42 },
            ["Faces"] = new AntiFlickerPresetState { Enabled = true, Strength = 28 }
        };
    }

    private AntiFlickerPresetState GetAntiFlickerPreset(string mode)
    {
        if (_antiFlickerPresets.TryGetValue(mode, out var preset))
        {
            return preset;
        }

        preset = CreateDefaultAntiFlickerPresets()[mode];
        _antiFlickerPresets[mode] = preset;
        return preset;
    }

    private static string NormalizeContentMode(string? mode) => mode?.Trim() switch
    {
        "AnimeUltra" => "AnimeUltra",
        "Video" => "Video",
        "Faces" => "Faces",
        _ => "Anime"
    };

    private static string NormalizeInputPath(string folder)
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



