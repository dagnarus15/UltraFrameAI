using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private sealed class NaturalStringComparer : IComparer<string>
    {
        public static NaturalStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return StrCmpLogicalW(x, y);
        }
    }

    private sealed record RecentRootFoldersCacheEntry(List<string> Folders, bool Complete);
    private sealed record PersistedQueueItemCacheEntry(string SourcePath, string Title, string OutputPath, bool IsChecked, bool SkipInRender);
    private sealed record ResumePreflightTelemetry(string? PhaseText = null, string? DetailText = null, string? EtaText = null, string? FpsText = null);
    private sealed class ResumePreflightUiState
    {
        public string PhaseText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public string? EtaTextOverride { get; set; }
        public string FpsText { get; set; } = "--";
    }
    private sealed record AppSettingsCacheEntry(
        string RootFolder,
        string OutputFolder,
        string? FfmpegDirectory,
        string SelectedRenderMode,
        string SelectedCodec,
        string SelectedTarget,
        string SelectedImageTarget,
        string? SelectedImageOutputFormat,
        string SelectedContainer,
        string EncoderPreset,
        string FfmpegThreadsText,
        string UpscalerThreadsText,
        string TileSizeText,
        string SelectedGpuOptionKey,
        string SelectedUpscalerBackend,
        string SelectedRefinerBackend,
        bool EnableStableSr,
        bool EnableSupir,
        string StableSrUpscalerPath,
        string StableSrUpscalerWorkingDirectory,
        string StableSrUpscalerModelDir,
        string StableSrUpscalerArgumentsTemplate,
        string SupirUpscalerPath,
        string SupirUpscalerWorkingDirectory,
        string SupirUpscalerModelDir,
        string SupirUpscalerArgumentsTemplate,
        bool Overwrite,
        string CurrentLanguage,
        string? BackgroundColorHex,
        bool PreserveIncompleteOutput,
        bool? RepairBrokenTimestamps,
        bool StartupBenchmarkPromptShown,
        bool StartupBenchmarkCompleted,
        string? BenchmarkedHardwareSignature,
        double? StartupBenchmarkMinimumRamGb,
        double? StartupBenchmarkComfortableRamGb,
        double? StartupBenchmarkPeakRamGb,
        double? StartupBenchmarkMinimumVramGb,
        double? StartupBenchmarkComfortableVramGb,
        double? StartupBenchmarkPeakVramGb,
        bool ShowQueuePaths,
        List<PersistedQueueItemCacheEntry>? QueueItems,
        string? SelectedItemSourcePath,
        bool Complete);

    private readonly PipelineService _pipeline;
    private readonly ImageRenderService _imageRenderService;
    private readonly bool _persistUserState;
    private readonly RelayCommand _browseRootFolderCommand;
    private readonly RelayCommand _browseRootFileCommand;
    private readonly RelayCommand _browseOutputCommand;
    private readonly RelayCommand _openOutputCommand;
    private readonly AsyncRelayCommand _resetRootCommand;
    private readonly RelayCommand _setLanguageCommand;
    private readonly RelayCommand _removeItemCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _skipCurrentCommand;
    private readonly RelayCommand _togglePauseCommand;
    private readonly RelayCommand _finishAfterCurrentCommand;
    private readonly RelayCommand _setRenderModeCommand;
    private readonly RelayCommand _stopImageRenderCommand;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly AsyncRelayCommand _startSelectedCommand;
    private readonly ObservableCollection<LogEntryViewModel> _logLines = UiCollections.CreateLogCollection();
    private IReadOnlyList<TargetFormatOption> _targetOptions = Array.Empty<TargetFormatOption>();
    private IReadOnlyList<UpscalerBackendOption> _upscalerBackendOptions = Array.Empty<UpscalerBackendOption>();
    private IReadOnlyList<RefinerBackendOption> _refinerBackendOptions = Array.Empty<RefinerBackendOption>();
    private IReadOnlyList<GpuDeviceOption> _gpuOptions = Array.Empty<GpuDeviceOption>();
    private readonly IReadOnlyList<DetectedGpuDevice> _detectedGpuDevices;
    private readonly string _currentHardwareSignature;
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _lastRootFolderPath;
    private readonly string _recentRootFoldersPath;
    private readonly string _appSettingsPath;
    private const string DefaultTargetValue = "1080p";
    private const string CustomTargetActionValue = "__custom_target__";
    private static readonly string[] SupportedTargetValues = { "720p", DefaultTargetValue, "1440p", "2160p", "4320p" };
    private static readonly string[] SupportedImageTargetValues = { "720p", DefaultTargetValue, "1440p", "2160p", "4320p", "8640p" };

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _scanCts;
    private readonly HashSet<QueueItemViewModel> _attachedQueueItems = new();
    private bool _isBusy;
    private string _rootFolder = Directory.GetCurrentDirectory();
    private string _outputFolder = string.Empty;
    private RenderContentMode _selectedRenderMode = RenderContentMode.Video;
    private string _selectedCodec = "x264";
    private string _selectedTarget = DefaultTargetValue;
    private string _selectedImageTarget = DefaultTargetValue;
    private string _selectedImageOutputFormat = "png";
    private string _selectedContainer = "mkv";
    private string _encoderPreset = "slower";
    private string _ffmpegThreadsText = "0";
    private string _upscalerThreadsText = "4:4:4";
    private string _tileSizeText = "1024";
    private GpuDeviceOption? _selectedGpuOption;
    private UpscalerBackendKind _selectedUpscalerBackend = UpscalerBackendKind.RealEsrgan;
    private RefinerBackendKind _selectedRefinerBackend = RefinerBackendKind.None;
    private bool _enableStableSr;
    private bool _enableSupir;
    private string _stableSrUpscalerPath = string.Empty;
    private string _stableSrUpscalerWorkingDirectory = string.Empty;
    private string _stableSrUpscalerModelDir = string.Empty;
    private string _stableSrUpscalerArgumentsTemplate = string.Empty;
    private string _supirUpscalerPath = string.Empty;
    private string _supirUpscalerWorkingDirectory = string.Empty;
    private string _supirUpscalerModelDir = string.Empty;
    private string _supirUpscalerArgumentsTemplate = string.Empty;
    private bool _overwrite;
    private bool _startupBenchmarkPromptShown;
    private bool _startupBenchmarkCompleted;
    private string _benchmarkedHardwareSignature = string.Empty;
    private StartupBenchmarkPracticalRequirements? _startupBenchmarkPracticalRequirements;
    private StartupBenchmarkPromptKind _startupBenchmarkPromptKind;
    private bool _preserveIncompleteOutput;
    private bool _repairBrokenTimestamps = true;
    private bool _suppressAppSettingsPersistence;
    private string _statusSummary = string.Empty;
    private string _lastHeartbeat = string.Empty;
    private string _currentStage = string.Empty;
    private string _currentItemTitle = string.Empty;
    private string _currentItemDetail = string.Empty;
    private string _currentFrameTimestampText = string.Empty;
    private string _currentStatusLine = string.Empty;
    private string _currentStageDisplayText = string.Empty;
    private string _elapsedText = "--:--:--";
    private string _etaText = "--:--:--";
    private string _stageDurationText = "--:--:--";
    private string _processingFpsText = "--";
    private string _queueSummary = string.Empty;
    private string _queueSourceSummary = string.Empty;
    private string _queueSourceTooltip = string.Empty;
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
    private bool _showQueuePaths;
    private bool _isDeleteConfirmVisible;
    private double _overallProgress;
    private string _configuredFfmpegDirectory = string.Empty;
    private QueueItemViewModel? _selectedItem;
    private UiLanguage _currentLanguage;
    private readonly Dictionary<int, (int current, int total)> _sessionFrameProgress = new();
    private OutputConflictDecision? _sessionOutputDecision;
    private bool _isRenderMode;
    private bool _isRenderPaused;
    private bool _finishAfterCurrentRequested;
    private bool _stopImageRenderAfterCurrentRequested;
    private bool _isImageRenderOverlayVisible;
    private double _imageRenderProgress;
    private string _imageRenderStatusText = string.Empty;
    private string _imageRenderDetailText = string.Empty;
    private bool _closeRenderModeAfterCurrentSkip;
    private bool _keepRenderModeForAutoResume;
    private readonly List<QueueItemViewModel> _pendingAutoResumeItems = new();
    private string _lastRunProcessedFiles = "--";
    private string _lastRunElapsed = "--:--:--";
    private string _lastRunFps = "--";
    private RenderSessionResults? _pendingRenderSessionResults;

    public MainViewModel() : this(true)
    {
    }

    public MainViewModel(bool persistUserState)
    {
        _persistUserState = persistUserState;
        var settingsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UltraFrameAI");
        _lastRootFolderPath = Path.Combine(settingsRoot, "last-root-folder.txt");
        _recentRootFoldersPath = Path.Combine(settingsRoot, "recent-root-folders.txt");
        _appSettingsPath = Path.Combine(settingsRoot, "app-settings.json");
        _pipeline = new PipelineService();
        _imageRenderService = new ImageRenderService();
        Items = new ObservableCollection<QueueItemViewModel>();
        CurrentRenderItems = new ObservableCollection<QueueItemViewModel>();
        CurrentRenderItems.CollectionChanged += CurrentRenderItems_CollectionChanged;
        RecentRootFolders = new ObservableCollection<RecentFolderItem>();
        CodecOptions = new[] { "x264", "x265" };
        ImageOutputFormatOptions = new[] { "png", "jpg" };
        _targetOptions = BuildTargetOptions(_selectedTarget, include16K: false);
        ContainerOptions = new[] { "mkv" };
        EncoderPresetOptions = new[] { "fast", "medium", "slower" };

        _browseRootFolderCommand = new RelayCommand(BrowseRootFolder);
        _browseRootFileCommand = new RelayCommand(BrowseRootFile);
        _browseOutputCommand = new RelayCommand(BrowseOutput);
        _openOutputCommand = new RelayCommand(OpenOutputFolder);
        _resetRootCommand = new AsyncRelayCommand(ResetToLastFolderAsync, () => !IsBusy);
        _setLanguageCommand = new RelayCommand(SetLanguage);
        _removeItemCommand = new RelayCommand(RemoveItem, CanRemoveItem);
        _cancelCommand = new RelayCommand(CancelRun, () => IsBusy);
        _skipCurrentCommand = new RelayCommand(SkipCurrentItem, () => IsBusy);
        _togglePauseCommand = new RelayCommand(TogglePause, () => CanPauseRender);
        _finishAfterCurrentCommand = new RelayCommand(FinishAfterCurrentItem, () => CanFinishAfterCurrent);
        _setRenderModeCommand = new RelayCommand(SetRenderMode, _ => !IsBusy);
        _stopImageRenderCommand = new RelayCommand(StopImageRenderAfterCurrent, () => IsImageRenderOverlayVisible && !_stopImageRenderAfterCurrentRequested);
        _scanCommand = new AsyncRelayCommand(() => ScanAsync(showOverlay: true), () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
        _startSelectedCommand = new AsyncRelayCommand(StartSelectedAsync, () => !IsBusy);

        LocalizedStrings.LanguageChanged += (_, _) => RefreshLocalizedText();
        AppThemeManager.ThemeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentBackgroundPreviewBrush));
            if (!_suppressAppSettingsPersistence)
            {
                PersistAppSettings();
            }
        };
        _currentLanguage = LocalizedStrings.CurrentLanguage;
        _upscalerBackendOptions = BuildUpscalerBackendOptions();
        _refinerBackendOptions = BuildRefinerBackendOptions();
        _detectedGpuDevices = GpuDeviceDetector.DetectDevices();
        _currentHardwareSignature = ComputeHardwareSignature(_detectedGpuDevices);
        RebuildGpuOptions();
        LoadRecentRootFolders();
        _suppressAppSettingsPersistence = true;
        try
        {
            RootFolder = LoadPersistedRootFolder(_repoRoot);
            LoadAppSettings();
        }
        finally
        {
            _suppressAppSettingsPersistence = false;
        }
        EvaluateStartupBenchmarkPromptState();
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

    public bool? QueueSelectAllState
    {
        get
        {
            var eligibleItems = Items.Where(item => !item.IsBusy).ToArray();
            if (eligibleItems.Length == 0)
            {
                return false;
            }

            var selectedCount = eligibleItems.Count(item => item.IsChecked);
            return selectedCount switch
            {
                0 => false,
                _ when selectedCount == eligibleItems.Length => true,
                _ => null
            };
        }
    }

    public bool? RenderSkipAllState
    {
        get
        {
            var eligibleItems = CurrentRenderItems.Where(CanToggleSkipInRender).ToArray();
            if (eligibleItems.Length == 0)
            {
                return false;
            }

            var selectedCount = eligibleItems.Count(item => item.SkipInRender);
            return selectedCount switch
            {
                0 => false,
                _ when selectedCount == eligibleItems.Length => true,
                _ => null
            };
        }
    }

    public ObservableCollection<RecentFolderItem> RecentRootFolders { get; }

    public ObservableCollection<LogEntryViewModel> LogLines => _logLines;

    public IEnumerable<string> CodecOptions { get; }

    public IEnumerable<string> ImageOutputFormatOptions { get; }

    public IEnumerable<TargetFormatOption> TargetOptions => _targetOptions;

    public IEnumerable<string> ContainerOptions { get; }

    public IEnumerable<string> EncoderPresetOptions { get; }

    public ICommand BrowseRootFolderCommand => _browseRootFolderCommand;

    public ICommand BrowseRootFileCommand => _browseRootFileCommand;

    public ICommand BrowseOutputCommand => _browseOutputCommand;

    public ICommand OpenOutputCommand => _openOutputCommand;

    public ICommand ResetRootCommand => _resetRootCommand;

    public ICommand SetLanguageCommand => _setLanguageCommand;

    public ICommand RemoveItemCommand => _removeItemCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand SkipCurrentCommand => _skipCurrentCommand;

    public ICommand TogglePauseCommand => _togglePauseCommand;

    public ICommand FinishAfterCurrentCommand => _finishAfterCurrentCommand;

    public ICommand SetRenderModeCommand => _setRenderModeCommand;

    public ICommand StopImageRenderCommand => _stopImageRenderCommand;

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
                PersistAppSettings();
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
                _togglePauseCommand.RaiseCanExecuteChanged();
                _finishAfterCurrentCommand.RaiseCanExecuteChanged();
                _setRenderModeCommand.RaiseCanExecuteChanged();
                _stopImageRenderCommand.RaiseCanExecuteChanged();
                _resetRootCommand.RaiseCanExecuteChanged();
                _removeItemCommand.RaiseCanExecuteChanged();
                if (!value)
                {
                    _pipeline.CancelStopAfterCurrentItemRequest();
                    _finishAfterCurrentRequested = false;
                    OnPropertyChanged(nameof(CanFinishAfterCurrent));
                    OnPropertyChanged(nameof(RenderFinishAfterCurrentText));
                }
                UpdateActionStates();
                UpdatePauseStateUi();
                OnQueueStateChanged();
            }
        }
    }

    public bool IsRenderMode
    {
        get => _isRenderMode;
        private set
        {
            if (SetField(ref _isRenderMode, value))
            {
                UpdatePauseStateUi();
            }
        }
    }

    public bool IsRenderPaused
    {
        get => _isRenderPaused;
        private set
        {
            if (SetField(ref _isRenderPaused, value))
            {
                UpdatePauseStateUi();
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

    public string ComputedOutputFolderName => GetOutputFolderName();

    public string OutputLocationHintText => LocalizedStrings.OutputLocationHint(ComputedOutputFolderName);

    public RenderContentMode SelectedRenderMode
    {
        get => _selectedRenderMode;
        set
        {
            var oldMode = _selectedRenderMode;
            if (SetField(ref _selectedRenderMode, value))
            {
                RefreshTargetOptions();
                RefreshComputedOutputState();
                RefreshQueueOutputPaths();
                OnPropertyChanged(nameof(IsVideoMode));
                OnPropertyChanged(nameof(IsImagesMode));
                OnPropertyChanged(nameof(SelectedModeTarget));
                if (!_suppressAppSettingsPersistence && oldMode != value)
                {
                    ClearQueueForRenderModeChange();
                }
                UpdateActionStates();
                PersistAppSettings();
            }
        }
    }

    public bool IsVideoMode => SelectedRenderMode == RenderContentMode.Video;

    public bool IsImagesMode => SelectedRenderMode == RenderContentMode.Images;

    public string SelectedCodec
    {
        get => _selectedCodec;
        set
        {
            if (SetField(ref _selectedCodec, value))
            {
                RefreshComputedOutputState();
                PersistAppSettings();
            }
        }
    }

    public string SelectedModeTarget
    {
        get => IsImagesMode ? SelectedImageTarget : SelectedTarget;
        set
        {
            if (IsImagesMode)
            {
                SelectedImageTarget = value;
            }
            else
            {
                SelectedTarget = value;
            }
        }
    }

    public string SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (string.Equals(value, CustomTargetActionValue, StringComparison.Ordinal))
            {
                return;
            }

            if (SetField(ref _selectedTarget, value))
            {
                RefreshTargetOptions();
                RefreshComputedOutputState();
                PersistAppSettings();
                if (IsVideoMode)
                {
                    OnPropertyChanged(nameof(SelectedModeTarget));
                }
            }
        }
    }

    public string SelectedImageTarget
    {
        get => _selectedImageTarget;
        set
        {
            if (string.Equals(value, CustomTargetActionValue, StringComparison.Ordinal))
            {
                return;
            }

            if (SetField(ref _selectedImageTarget, value))
            {
                RefreshTargetOptions();
                RefreshComputedOutputState();
                PersistAppSettings();
                if (IsImagesMode)
                {
                    OnPropertyChanged(nameof(SelectedModeTarget));
                }
            }
        }
    }

    public string SelectedImageOutputFormat
    {
        get => _selectedImageOutputFormat;
        set
        {
            var normalized = NormalizeImageOutputFormat(value);
            if (SetField(ref _selectedImageOutputFormat, normalized))
            {
                RefreshComputedOutputState();
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

    public IReadOnlyList<UpscalerBackendOption> UpscalerBackendOptions => _upscalerBackendOptions;

    public IReadOnlyList<RefinerBackendOption> RefinerBackendOptions => _refinerBackendOptions;

    public IReadOnlyList<GpuDeviceOption> GpuOptions => _gpuOptions;

    public GpuDeviceOption? SelectedGpuOption
    {
        get => _selectedGpuOption;
        set
        {
            if (SetField(ref _selectedGpuOption, value))
            {
                PersistAppSettings();
            }
        }
    }

    public bool ShouldOfferStartupBenchmark => _startupBenchmarkPromptKind != StartupBenchmarkPromptKind.None;

    public bool HasConfiguredFfmpegTools()
        => File.Exists(ResolveToolPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe"))
           && File.Exists(ResolveToolPath("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe"));

    public string GetResolvedFfmpegExecutablePath()
        => ResolveToolPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe");

    public string GetResolvedFfprobeExecutablePath()
        => ResolveToolPath("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe");

    public string FfmpegDirectoryDisplay
        => GetResolvedFfmpegDirectory() ?? LocalizedStrings.Get("FfmpegDirectoryNotConfigured");

    public bool TrySetFfmpegDirectory(string directory, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error = LocalizedStrings.Get("FfmpegSetupInvalidFolder");
            return false;
        }

        var fullPath = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(fullPath, "ffmpeg.exe")) || !File.Exists(Path.Combine(fullPath, "ffprobe.exe")))
        {
            error = LocalizedStrings.Get("FfmpegSetupMissingTools");
            return false;
        }

        _configuredFfmpegDirectory = fullPath;
        OnPropertyChanged(nameof(FfmpegDirectoryDisplay));
        PersistAppSettings();
        return true;
    }

    public StartupBenchmarkPromptKind CurrentStartupBenchmarkPromptKind => _startupBenchmarkPromptKind;

    public bool HasDetectedGpuCandidates => _detectedGpuDevices.Count > 0;

    public UpscalerBackendKind SelectedUpscalerBackend
    {
        get => _selectedUpscalerBackend;
        set
        {
            if (SetField(ref _selectedUpscalerBackend, value))
            {
                EnsureSelectedExternalUpscalerDefaults();
                OnPropertyChanged(nameof(IsExternalUpscalerSelected));
                OnPropertyChanged(nameof(SelectedExternalUpscalerPath));
                OnPropertyChanged(nameof(SelectedExternalUpscalerWorkingDirectory));
                OnPropertyChanged(nameof(SelectedExternalUpscalerModelDir));
                OnPropertyChanged(nameof(SelectedExternalUpscalerArgumentsTemplate));
                OnPropertyChanged(nameof(SelectedExternalUpscalerSetupHint));
                PersistAppSettings();
            }
        }
    }

    public RefinerBackendKind SelectedRefinerBackend
    {
        get => _selectedRefinerBackend;
        set
        {
            if (SetField(ref _selectedRefinerBackend, value))
            {
                EnsureSelectedExternalRefinerDefaults();
                OnPropertyChanged(nameof(IsExternalRefinerSelected));
                OnPropertyChanged(nameof(SelectedExternalRefinerPath));
                OnPropertyChanged(nameof(SelectedExternalRefinerWorkingDirectory));
                OnPropertyChanged(nameof(SelectedExternalRefinerModelDir));
                OnPropertyChanged(nameof(SelectedExternalRefinerArgumentsTemplate));
                OnPropertyChanged(nameof(SelectedExternalRefinerSetupHint));
                PersistAppSettings();
            }
        }
    }

    public bool IsExternalUpscalerSelected => SelectedUpscalerBackend is UpscalerBackendKind.StableSrExternal or UpscalerBackendKind.SupirExternal;

    public bool IsExternalRefinerSelected => SelectedRefinerBackend is RefinerBackendKind.StableSrExternal or RefinerBackendKind.SupirExternal;

    public bool EnableStableSr
    {
        get => _enableStableSr;
        set
        {
            if (SetField(ref _enableStableSr, value))
            {
                if (!value && SelectedUpscalerBackend == UpscalerBackendKind.StableSrExternal)
                {
                    SelectedUpscalerBackend = UpscalerBackendKind.RealEsrgan;
                }
                if (!value && SelectedRefinerBackend == RefinerBackendKind.StableSrExternal)
                {
                    SelectedRefinerBackend = RefinerBackendKind.None;
                }

                _upscalerBackendOptions = BuildUpscalerBackendOptions();
                _refinerBackendOptions = BuildRefinerBackendOptions();
                OnPropertyChanged(nameof(UpscalerBackendOptions));
                OnPropertyChanged(nameof(RefinerBackendOptions));
                PersistAppSettings();
            }
        }
    }

    public bool EnableSupir
    {
        get => _enableSupir;
        set
        {
            if (SetField(ref _enableSupir, value))
            {
                if (!value && SelectedUpscalerBackend == UpscalerBackendKind.SupirExternal)
                {
                    SelectedUpscalerBackend = UpscalerBackendKind.RealEsrgan;
                }
                if (!value && SelectedRefinerBackend == RefinerBackendKind.SupirExternal)
                {
                    SelectedRefinerBackend = RefinerBackendKind.None;
                }

                _upscalerBackendOptions = BuildUpscalerBackendOptions();
                _refinerBackendOptions = BuildRefinerBackendOptions();
                OnPropertyChanged(nameof(UpscalerBackendOptions));
                OnPropertyChanged(nameof(RefinerBackendOptions));
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalUpscalerPath
    {
        get => GetSelectedExternalUpscalerPath();
        set
        {
            if (SetSelectedExternalUpscalerPath(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalUpscalerWorkingDirectory
    {
        get => GetSelectedExternalUpscalerWorkingDirectory();
        set
        {
            if (SetSelectedExternalUpscalerWorkingDirectory(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalUpscalerModelDir
    {
        get => GetSelectedExternalUpscalerModelDir();
        set
        {
            if (SetSelectedExternalUpscalerModelDir(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalUpscalerArgumentsTemplate
    {
        get => GetSelectedExternalUpscalerArgumentsTemplate();
        set
        {
            if (SetSelectedExternalUpscalerArgumentsTemplate(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalUpscalerSetupHint => SelectedUpscalerBackend switch
    {
        UpscalerBackendKind.StableSrExternal => LocalizedStrings.ExternalUpscalerStableSrHint,
        UpscalerBackendKind.SupirExternal => LocalizedStrings.ExternalUpscalerSupirHint,
        _ => LocalizedStrings.ExternalUpscalerHint
    };

    public string SelectedExternalRefinerPath
    {
        get => GetSelectedExternalRefinerPath();
        set
        {
            if (SetSelectedExternalRefinerPath(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalRefinerWorkingDirectory
    {
        get => GetSelectedExternalRefinerWorkingDirectory();
        set
        {
            if (SetSelectedExternalRefinerWorkingDirectory(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalRefinerModelDir
    {
        get => GetSelectedExternalRefinerModelDir();
        set
        {
            if (SetSelectedExternalRefinerModelDir(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalRefinerArgumentsTemplate
    {
        get => GetSelectedExternalRefinerArgumentsTemplate();
        set
        {
            if (SetSelectedExternalRefinerArgumentsTemplate(value))
            {
                OnPropertyChanged();
                PersistAppSettings();
            }
        }
    }

    public string SelectedExternalRefinerSetupHint => SelectedRefinerBackend switch
    {
        RefinerBackendKind.StableSrExternal => LocalizedStrings.ExternalRefinerStableSrHint,
        RefinerBackendKind.SupirExternal => LocalizedStrings.ExternalRefinerSupirHint,
        _ => LocalizedStrings.ExternalRefinerHint
    };

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

    public string CurrentFrameTimestampText
    {
        get => _currentFrameTimestampText;
        private set => SetField(ref _currentFrameTimestampText, value);
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

    public string ProcessingFpsText
    {
        get => _processingFpsText;
        private set => SetField(ref _processingFpsText, value);
    }

    public bool IsRenderPreviewLoading => string.Equals(CurrentItemDetail, LocalizedStrings.Get("ResumePreflightLoadingFile"), StringComparison.Ordinal);

    public string RenderPreviewLoadingText => LocalizedStrings.Get("ResumePreflightLoadingFile");

    public bool CanPauseRender =>
        IsBusy
        && IsRenderMode
        && CurrentRenderItems.Count > 0
        && !string.IsNullOrWhiteSpace(CurrentItemTitle)
        && !string.Equals(CurrentStage, LocalizedStrings.LogPreparing, StringComparison.Ordinal);

    public string RenderPauseGlyph => IsRenderPaused ? "\uE768" : "\uE769";

    public string RenderPauseToolTip => IsRenderPaused
        ? LocalizedStrings.Get("RenderResume")
        : LocalizedStrings.Get("RenderPause");

    public bool CanFinishAfterCurrent =>
        IsBusy
        && IsRenderMode
        && CurrentRenderItems.Count > 0
        && !string.IsNullOrWhiteSpace(CurrentItemTitle);

    public string RenderFinishAfterCurrentText => _finishAfterCurrentRequested
        ? LocalizedStrings.Get("RenderFinishAfterCurrentQueued")
        : LocalizedStrings.Get("RenderFinishAfterCurrent");

    public string QueueSummary
    {
        get => _queueSummary;
        private set => SetField(ref _queueSummary, value);
    }

    public string QueueSourceSummary
    {
        get => _queueSourceSummary;
        private set => SetField(ref _queueSourceSummary, value);
    }

    public string QueueSourceTooltip
    {
        get => _queueSourceTooltip;
        private set => SetField(ref _queueSourceTooltip, value);
    }

    public bool ShowQueuePaths
    {
        get => _showQueuePaths;
        set
        {
            if (SetField(ref _showQueuePaths, value))
            {
                ApplyQueuePathVisibilityToItems();
                PersistAppSettings();
            }
        }
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

    public bool IsImageRenderOverlayVisible
    {
        get => _isImageRenderOverlayVisible;
        private set
        {
            if (SetField(ref _isImageRenderOverlayVisible, value))
            {
                _stopImageRenderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ImageRenderProgress
    {
        get => _imageRenderProgress;
        private set => SetField(ref _imageRenderProgress, value);
    }

    public string ImageRenderStatusText
    {
        get => _imageRenderStatusText;
        private set => SetField(ref _imageRenderStatusText, value);
    }

    public string ImageRenderDetailText
    {
        get => _imageRenderDetailText;
        private set => SetField(ref _imageRenderDetailText, value);
    }

    public string StopImageRenderText => _stopImageRenderAfterCurrentRequested
        ? LocalizedStrings.Get("ImageRenderStopRequested")
        : LocalizedStrings.Get("ImageRenderStopAfterCurrent");

    public string CurrentLanguageFlagPath => CurrentLanguage switch
    {
        UiLanguage.Russian => "pack://application:,,,/images/flag-ru.png",
        UiLanguage.German => "pack://application:,,,/images/flag-de.png",
        UiLanguage.Japanese => "pack://application:,,,/images/flag-ja.png",
        UiLanguage.Chinese => "pack://application:,,,/images/flag-zh.png",
        _ => "pack://application:,,,/images/flag-en.png"
    };

    public ImageSource BackgroundColorWheelImage { get; } = AppThemeManager.CreateColorWheelImage();

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
        RememberRecentFolder(RootFolder, persist: true);
        if (File.Exists(path))
        {
            await LoadSingleFileAsync(path).ConfigureAwait(true);
            return;
        }

        await ScanAsync(showOverlay: true).ConfigureAwait(true);
    }

    public async Task LoadRootPathsAsync(IEnumerable<string> paths)
    {
        var validPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (validPaths.Length == 0)
        {
            return;
        }

        if (validPaths.Length == 1)
        {
            await LoadRootFolderAsync(validPaths[0]).ConfigureAwait(true);
            return;
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            Log(LocalizedStrings.LogScanningFiles);
            var allSourceFiles = new List<string>();
            var totalRoots = validPaths.Length;
            BeginScanOverlay(Path.GetDirectoryName(validPaths[0]) ?? validPaths[0]);

            for (var i = 0; i < validPaths.Length; i++)
            {
                var path = validPaths[i];
                _scanCts.Token.ThrowIfCancellationRequested();
                RootFolder = path;
                RememberRecentFolder(path, persist: true);
                ReportScanProgress(i, totalRoots, allSourceFiles.Count, Path.GetFileName(path));

                var files = IsImagesMode
                    ? await FindImageFilesAsync(path, null, _scanCts.Token).ConfigureAwait(true)
                    : await FindVideoFilesAsync(path, null, _scanCts.Token).ConfigureAwait(true);
                allSourceFiles.AddRange(files);
                ReportScanProgress(i + 1, totalRoots, allSourceFiles.Count, Path.GetFileName(path));
            }

            AddSourceFilesToQueue(
                allSourceFiles
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                GetScanRoot(validPaths[0]));
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogScanFailed(ex.Message));
            PostToUi(() => ShowPopupMessage(LocalizedStrings.AppTitle, ex.Message));
        }
        finally
        {
            EndScanOverlay();
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
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

    private void SetRenderMode(object? parameter)
    {
        if (parameter is RenderContentMode mode)
        {
            SelectedRenderMode = mode;
            return;
        }

        if (parameter is string text && Enum.TryParse(text, true, out RenderContentMode parsed))
        {
            SelectedRenderMode = parsed;
        }
    }

    private void StopImageRenderAfterCurrent()
    {
        _stopImageRenderAfterCurrentRequested = true;
        ImageRenderDetailText = LocalizedStrings.Get("ImageRenderStopRequestedDetail");
        OnPropertyChanged(nameof(StopImageRenderText));
        _stopImageRenderCommand.RaiseCanExecuteChanged();
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
        var anySelectedStartable = Items.Any(item => item.IsChecked && IsItemCompatibleWithCurrentMode(item));
        var anyDeletable = Items.Any(item => !item.IsBusy);
        var anyStartable = Items.Any(item => !item.IsBusy && IsItemCompatibleWithCurrentMode(item));

        CanDeleteSelected = anySelected;
        CanDeleteAll = anyDeletable && !IsBusy;
        CanStartAll = !IsBusy && anyStartable;
        CanStartSelected = !IsBusy && anySelectedStartable;
        OnPropertyChanged(nameof(QueueSelectAllState));
        _startCommand.RaiseCanExecuteChanged();
        _startSelectedCommand.RaiseCanExecuteChanged();
    }

    private bool IsItemCompatibleWithCurrentMode(QueueItemViewModel item)
        => IsImagesMode ? IsLikelyImageFile(item.SourcePath) : IsLikelyVideoFile(item.SourcePath);

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
            Log(LocalizedStrings.LogScanningFiles);

            var scanRoot = GetScanRoot(inputPath);
            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException(LocalizedStrings.LogRootNotFound(inputPath));
            }

            await Task.Yield();
            var sourceFiles = IsImagesMode
                ? await FindImageFilesAsync(inputPath, showOverlay ? ReportScanProgress : null, _scanCts.Token).ConfigureAwait(true)
                : await FindVideoFilesAsync(inputPath, showOverlay ? ReportScanProgress : null, _scanCts.Token).ConfigureAwait(true);

            var total = sourceFiles.Length;
            var addedItems = new List<QueueItemViewModel>();
            for (var i = 0; i < total; i++)
            {
                var sourceFile = sourceFiles[i];
                if (Items.Any(existing => string.Equals(existing.SourcePath, sourceFile, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var baseName = Path.GetFileNameWithoutExtension(sourceFile);
                var sourceDir = Path.GetDirectoryName(sourceFile) ?? scanRoot;
                var relativeDir = Path.GetRelativePath(scanRoot, sourceDir);
                if (relativeDir == ".")
                {
                    relativeDir = string.Empty;
                }

                var relativeFile = string.IsNullOrWhiteSpace(relativeDir)
                    ? baseName
                    : Path.Combine(relativeDir, baseName);
                var displayTitle = string.IsNullOrWhiteSpace(relativeDir)
                    ? Path.GetFileName(sourceFile)
                    : Path.Combine(relativeDir, Path.GetFileName(sourceFile));

                var item = new QueueItemViewModel()
                {
                    Index = Items.Count + 1,
                    Title = displayTitle,
                    SourcePath = sourceFile,
                    OutputPath = BuildOutputPath(sourceFile),
                };
                addedItems.Add(item);
            }

            AppendSortedItems(addedItems);
            var addedCount = addedItems.Count;

            ReindexItems();
            UpdateQueueSummary();
            UpdateActionStates();
            Log(LocalizedStrings.LogFoundVideoFiles(addedCount));
            if (Items.Count > 0)
            {
                SelectedItem ??= Items[0];
                UpdateSelectionDetails();
            }

            PersistAppSettings();
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogScanFailed(ex.Message));
            PostToUi(() => ShowPopupMessage(LocalizedStrings.AppTitle, ex.Message));
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
        var ffprobePath = ResolveToolPath("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe");
        var candidates = await Task.Run(() =>
        {
            if (File.Exists(inputPath))
            {
                return new[] { inputPath };
            }

            var list = new List<string>();
            foreach (var path in Directory.EnumerateFiles(inputPath, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipScanCandidate(path, GetOutputFolderName()) || !IsLikelyVideoFile(path))
                {
                    continue;
                }

                list.Add(path);
            }

            return list
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
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

    private async Task<string[]> FindImageFilesAsync(string inputPath, Action<int, int, int, string?>? progress, CancellationToken ct)
    {
        var matches = await Task.Run(() =>
        {
            if (File.Exists(inputPath))
            {
                return IsLikelyImageFile(inputPath)
                    ? new[] { inputPath }
                    : Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var path in Directory.EnumerateFiles(inputPath, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipScanCandidate(path, GetOutputFolderName()) || !IsLikelyImageFile(path))
                {
                    continue;
                }

                list.Add(path);
            }

            return list
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, ct).ConfigureAwait(false);

        var lastTick = Stopwatch.StartNew();
        for (var i = 0; i < matches.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var checkedCount = i + 1;
            if (progress is not null && (checkedCount == matches.Length || lastTick.ElapsedMilliseconds >= 250))
            {
                progress(checkedCount, matches.Length, checkedCount, Path.GetFileName(matches[i]));
                lastTick.Restart();
            }
        }

        progress?.Invoke(matches.Length, matches.Length, matches.Length, matches.Length > 0 ? Path.GetFileName(matches[^1]) : string.Empty);
        return matches;
    }

    private void AddSourceFilesToQueue(IReadOnlyList<string> sourceFiles, string scanRoot)
    {
        var addedItems = new List<QueueItemViewModel>();
        foreach (var sourceFile in sourceFiles)
        {
            if (Items.Any(existing => string.Equals(existing.SourcePath, sourceFile, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var sourceDir = Path.GetDirectoryName(sourceFile) ?? scanRoot;
            var relativeDir = string.Empty;
            try
            {
                relativeDir = Path.GetRelativePath(scanRoot, sourceDir);
                if (relativeDir == ".")
                {
                    relativeDir = string.Empty;
                }
            }
            catch
            {
                relativeDir = string.Empty;
            }

            var displayTitle = string.IsNullOrWhiteSpace(relativeDir)
                ? Path.GetFileName(sourceFile)
                : Path.Combine(relativeDir, Path.GetFileName(sourceFile));

            var item = new QueueItemViewModel()
            {
                Index = Items.Count + addedItems.Count + 1,
                Title = displayTitle,
                SourcePath = sourceFile,
                OutputPath = BuildOutputPath(sourceFile),
            };
            addedItems.Add(item);
        }

        AppendSortedItems(addedItems);
        ReindexItems();
        UpdateQueueSummary();
        UpdateActionStates();
        Log(LocalizedStrings.LogFoundVideoFiles(addedItems.Count));
        if (addedItems.Count > 0)
        {
            SelectedItem ??= addedItems[0];
            UpdateSelectionDetails();
        }

        OnQueueStateChanged();
        PersistAppSettings();
    }

    public System.Windows.Media.Brush CurrentBackgroundPreviewBrush => AppThemeManager.CreatePreviewBrush();

    private Task StartAsync() => IsImagesMode
        ? StartImageRenderAsync(Items.Where(item => IsLikelyImageFile(item.SourcePath)).ToArray(), LocalizedStrings.LogStartingBatch)
        : StartPipelineAsync(Items.Where(item => IsLikelyVideoFile(item.SourcePath)).ToArray(), LocalizedStrings.LogStartingBatch);

    private async Task StartSelectedAsync()
    {
        var selectedItems = Items
            .Where(item => item.IsChecked)
            .Where(item => IsImagesMode ? IsLikelyImageFile(item.SourcePath) : IsLikelyVideoFile(item.SourcePath))
            .ToArray();
        if (selectedItems.Length == 0)
        {
            Log(LocalizedStrings.LogNoItemSelected);
            return;
        }

        if (IsImagesMode)
        {
            await StartImageRenderAsync(selectedItems, LocalizedStrings.LogStartingSelectedBatch).ConfigureAwait(true);
        }
        else
        {
            await StartPipelineAsync(selectedItems, LocalizedStrings.LogStartingSelectedBatch).ConfigureAwait(true);
        }
    }

    private async Task StartImageRenderAsync(IReadOnlyList<QueueItemViewModel> runItems, string startMessage)
    {
        if (runItems.Count == 0)
        {
            Log(LocalizedStrings.LogNoItemsFound);
            return;
        }

        try
        {
            IsBusy = true;
            RememberRecentFolder(RootFolder, persist: true);
            _runCts = new CancellationTokenSource();
            _stopImageRenderAfterCurrentRequested = false;
            OnPropertyChanged(nameof(StopImageRenderText));
            ResetItemUi(runItems);
            Log(startMessage);
            _pendingRenderSessionResults = null;
            var sessionWatch = Stopwatch.StartNew();
            var sessionItems = new HashSet<QueueItemViewModel>(runItems);

            foreach (var item in runItems)
            {
                item.IsBusy = true;
                item.Progress = 0;
                item.ProgressText = "0%";
            }

            CurrentItemTitle = runItems[0].Title;
            CurrentStage = LocalizedStrings.Get("ImageRenderProgressTitle");
            ImageRenderProgress = 0;
            ImageRenderStatusText = LocalizedStrings.Get("ImageRenderPreparing");
            ImageRenderDetailText = string.Empty;
            IsImageRenderOverlayVisible = true;
            await Task.Yield();

            var options = BuildImageOptions();
            var effectiveItems = runItems
                .Where(item => Overwrite || !File.Exists(item.OutputPath))
                .ToArray();
            if (effectiveItems.Length == 0)
            {
                ImageRenderStatusText = LocalizedStrings.Get("ImageRenderNoItemsToProcess");
                Log(LocalizedStrings.Get("ImageRenderNoItemsToProcess"));
            }
            else
            {
                await _imageRenderService.RunAsync(
                    effectiveItems,
                    options,
                    HandleImageRenderProgress,
                    () => _stopImageRenderAfterCurrentRequested,
                    _runCts.Token).ConfigureAwait(true);
            }

            sessionWatch.Stop();
            UpdateLastRunSummary(sessionWatch.Elapsed);
            _pendingRenderSessionResults = BuildImageRenderSessionResults(sessionItems, sessionWatch.Elapsed);
            Log(LocalizedStrings.LogBatchFinished);
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
            _pendingRenderSessionResults = null;
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogBatchFailed(ex.Message));
            PostToUi(() => ShowPopupMessage(LocalizedStrings.AppTitle, LocalizedStrings.LogBatchFailed(ex.Message)));
            _pendingRenderSessionResults = null;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            foreach (var item in runItems)
            {
                item.IsBusy = false;
            }

            _stopImageRenderAfterCurrentRequested = false;
            OnPropertyChanged(nameof(StopImageRenderText));
            IsImageRenderOverlayVisible = false;
            IsBusy = false;
            OnPropertyChanged(nameof(RenderSkipAllState));
        }
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
            _pipeline.SetPaused(false);
            IsRenderPaused = false;
            RememberRecentFolder(RootFolder, persist: true);
            _runCts = new CancellationTokenSource();
            _closeRenderModeAfterCurrentSkip = false;
            _keepRenderModeForAutoResume = false;
            _pendingAutoResumeItems.Clear();
            ClearRenderSkipSelections(runItems);
            ResetItemUi(runItems);
            Log(startMessage);
            _sessionOutputDecision = null;
            _sessionFrameProgress.Clear();
            _pendingRenderSessionResults = null;
            var sessionWatch = Stopwatch.StartNew();
            var sessionItems = new HashSet<QueueItemViewModel>(runItems);

            CurrentRenderItems.Clear();
            foreach (var item in runItems)
            {
                CurrentRenderItems.Add(item);
            }

            ClearRenderPreviewPaths();
            CurrentStage = LocalizedStrings.LogPreparing;
            CurrentItemTitle = runItems[0].Title;
            CurrentItemDetail = string.Empty;
            CurrentStatusLine = LocalizedStrings.LogPreparing;
            UpdateCurrentStageDisplayText();
            UpdateRenderPreviewLoadingState();
            UpdatePauseStateUi();
            IsRenderMode = true;
            await Task.Yield();

            var options = BuildOptions();
            var effectiveItems = await PrepareRunListAsync(runItems, options, _runCts.Token).ConfigureAwait(true);
            while (effectiveItems.Count > 0)
            {
                CurrentRenderItems.Clear();
                foreach (var item in effectiveItems)
                {
                    CurrentRenderItems.Add(item);
                    sessionItems.Add(item);
                }

                ClearRenderPreviewPaths();
                await _pipeline.RunAsync(effectiveItems, options, HandleProgress, HandleRenderPreviewFrame, _runCts.Token).ConfigureAwait(true);
                if (_runCts.IsCancellationRequested || _finishAfterCurrentRequested)
                {
                    break;
                }

                effectiveItems = await PrepareNextRunListAsync(effectiveItems, options, _runCts.Token).ConfigureAwait(true);
            }
            Log(LocalizedStrings.LogBatchFinished);
            sessionWatch.Stop();
            UpdateLastRunSummary(sessionWatch.Elapsed);
            _pendingRenderSessionResults = BuildRenderSessionResults(sessionItems, sessionWatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
            _pendingRenderSessionResults = null;
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogBatchFailed(ex.Message));
            PostToUi(() => ShowPopupMessage(LocalizedStrings.AppTitle, LocalizedStrings.LogBatchFailed(ex.Message)));
            _pendingRenderSessionResults = null;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            _pipeline.SetPaused(false);
            IsRenderPaused = false;
            ClearRenderSkipSelections(Items);
            IsBusy = false;
            IsRenderMode = false;
            ClearRenderPreviewPaths();
            CurrentRenderItems.Clear();
            OnPropertyChanged(nameof(RenderSkipAllState));
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
        var upscalerBackend = SelectedUpscalerBackend;
        var useExternalUpscaler = upscalerBackend is UpscalerBackendKind.StableSrExternal or UpscalerBackendKind.SupirExternal;
        var upscalerPath = useExternalUpscaler
            ? ResolveExternalUpscalerPath()
            : FindFile("realesrgan-ncnn-vulkan.exe", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "realesrgan-ncnn-vulkan.exe"));
        var upscalerWorkingDirectory = ResolveUpscalerWorkingDirectory(upscalerPath, useExternalUpscaler ? SelectedExternalUpscalerWorkingDirectory : string.Empty);
        var modelDir = useExternalUpscaler
            ? ResolveExternalUpscalerModelDir()
            : FindDirectory("models", Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "models"));
        var externalArgsTemplate = useExternalUpscaler
            ? ResolveExternalUpscalerArgumentsTemplate()
            : string.Empty;
        var refinerBackend = SelectedRefinerBackend;
        var useExternalRefiner = refinerBackend is RefinerBackendKind.StableSrExternal or RefinerBackendKind.SupirExternal;
        var refinerPath = useExternalRefiner
            ? ResolveExternalRefinerPath()
            : string.Empty;
        var refinerWorkingDirectory = useExternalRefiner
            ? ResolveUpscalerWorkingDirectory(refinerPath, SelectedExternalRefinerWorkingDirectory)
            : string.Empty;
        var refinerModelDir = useExternalRefiner
            ? ResolveExternalRefinerModelDir()
            : string.Empty;
        var refinerArgsTemplate = useExternalRefiner
            ? ResolveExternalRefinerArgumentsTemplate()
            : string.Empty;

        return new PipelineOptions
        {
            RootFolder = RootFolder,
            OutputFolder = GetScanRoot(RootFolder),
            Overwrite = Overwrite,
            UseX265 = SelectedCodec == "x265",
            TargetHeight = ParseTargetHeight(SelectedTarget),
            OutputContainer = SelectedContainer,
            FfmpegThreads = ParseInt(FfmpegThreadsText, 0),
            UpscalerThreads = string.IsNullOrWhiteSpace(UpscalerThreadsText) ? "4:4:4" : UpscalerThreadsText,
            TileSize = ParseInt(TileSizeText, 1024),
            GpuId = SelectedGpuOption?.ResolvedGpuId,
            FfmpegPath = ResolveToolPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe"),
            FfprobePath = ResolveToolPath("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe"),
            UpscalerBackend = upscalerBackend,
            UpscalerPath = upscalerPath,
            UpscalerWorkingDirectory = upscalerWorkingDirectory,
            ModelDir = modelDir,
            ExternalUpscalerArgumentsTemplate = externalArgsTemplate,
            RefinerBackend = refinerBackend,
            RefinerPath = refinerPath,
            RefinerWorkingDirectory = refinerWorkingDirectory,
            RefinerModelDir = refinerModelDir,
            RefinerArgumentsTemplate = refinerArgsTemplate,
            EncoderPreset = EncoderPreset,
            PreserveIncompleteOutput = PreserveIncompleteOutput,
            RepairBrokenTimestamps = RepairBrokenTimestamps
        };
    }

    private PipelineOptions BuildImageOptions()
    {
        var options = BuildOptions();
        return new PipelineOptions
        {
            RootFolder = options.RootFolder,
            OutputFolder = options.OutputFolder,
            Overwrite = options.Overwrite,
            UseX265 = options.UseX265,
            TargetHeight = ParseTargetHeight(SelectedImageTarget),
            OutputContainer = options.OutputContainer,
            FfmpegThreads = options.FfmpegThreads,
            UpscalerThreads = options.UpscalerThreads,
            TileSize = options.TileSize,
            GpuId = options.GpuId,
            FfmpegPath = options.FfmpegPath,
            FfprobePath = options.FfprobePath,
            UpscalerBackend = UpscalerBackendKind.RealEsrgan,
            UpscalerPath = options.UpscalerPath,
            UpscalerWorkingDirectory = options.UpscalerWorkingDirectory,
            ModelDir = options.ModelDir,
            ExternalUpscalerArgumentsTemplate = string.Empty,
            RefinerBackend = RefinerBackendKind.None,
            EncoderPreset = options.EncoderPreset,
            PreserveIncompleteOutput = options.PreserveIncompleteOutput,
            RepairBrokenTimestamps = options.RepairBrokenTimestamps
        };
    }

    public void MarkStartupBenchmarkPromptShown()
    {
        if (_startupBenchmarkPromptShown)
        {
            return;
        }

        _startupBenchmarkPromptShown = true;
        EvaluateStartupBenchmarkPromptState();
        PersistAppSettings();
    }

    public void MarkStartupBenchmarkCompleted()
    {
        _startupBenchmarkPromptShown = true;
        _startupBenchmarkCompleted = true;
        _benchmarkedHardwareSignature = _currentHardwareSignature;
        EvaluateStartupBenchmarkPromptState();
        PersistAppSettings();
    }

    public void StoreStartupBenchmarkAssessment(StartupBenchmarkReport report)
    {
        _startupBenchmarkPracticalRequirements = HardwareAssessmentBuilder.DerivePracticalRequirements(report);
        PersistAppSettings();
    }

    public StartupBenchmarkPracticalRequirements? GetStoredStartupBenchmarkPracticalRequirements()
    {
        if (_startupBenchmarkPracticalRequirements is null)
        {
            return null;
        }

        if (!string.Equals(_benchmarkedHardwareSignature, _currentHardwareSignature, StringComparison.Ordinal))
        {
            return null;
        }

        return _startupBenchmarkPracticalRequirements;
    }

    public string? GetStartupBenchmarkSourcePath()
    {
        var preferred = Path.Combine(_repoRoot, "realesrgan-ncnn-vulkan-20220424", "onepiece_demo.mp4");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Items.Select(item => item.SourcePath).FirstOrDefault(File.Exists);
    }

    public IReadOnlyList<StartupBenchmarkGpuCandidate> GetStartupBenchmarkGpuCandidates()
        => GetRenderableGpuDevices()
            .Select(device => new StartupBenchmarkGpuCandidate(device.BackendGpuId ?? device.DeviceId, BuildGpuLabel(device), device.MemoryMb))
            .ToArray();

    public IReadOnlyList<StartupBenchmarkGpuCandidate> GetSelectedStartupBenchmarkGpuCandidates()
    {
        if (SelectedGpuOption is { IsAuto: false } selectedOption
            && int.TryParse(selectedOption.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedBackendGpuId))
        {
            var selected = GetRenderableGpuDevices()
                .Where(device => (device.BackendGpuId ?? device.DeviceId) == selectedBackendGpuId)
                .Select(device => new StartupBenchmarkGpuCandidate(device.BackendGpuId ?? device.DeviceId, BuildGpuLabel(device), device.MemoryMb))
                .ToArray();
            if (selected.Length > 0)
            {
                return selected;
            }
        }

        return GetStartupBenchmarkGpuCandidates();
    }

    public void ApplyStartupBenchmarkRecommendation(StartupBenchmarkRecommendation recommendation)
    {
        if (recommendation.GpuId is int gpuId)
        {
            SelectedGpuOption = GpuOptions.FirstOrDefault(option => option.ResolvedGpuId == gpuId) ?? SelectedGpuOption;
        }
        else
        {
            SelectedGpuOption = GpuOptions.FirstOrDefault(option => option.IsAuto) ?? SelectedGpuOption;
        }

        UpscalerThreadsText = recommendation.UpscalerThreads;
        EncoderPreset = recommendation.EncoderPreset;
        TileSizeText = recommendation.TileSize.ToString(CultureInfo.InvariantCulture);
        PersistAppSettings();
    }

    public void ApplyCustomTargetHeight(int height)
    {
        if (height < 120)
        {
            height = 120;
        }

        SelectedModeTarget = $"{height}p";
    }

    public RenderSessionResults? ConsumePendingRenderSessionResults()
    {
        var results = _pendingRenderSessionResults;
        _pendingRenderSessionResults = null;
        return results;
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
            CurrentFrameTimestampText = progress.CurrentFrameTimestampText ?? string.Empty;
            CurrentStatusLine = progress.CurrentStatus;
            _currentItemIndex = progress.ItemIndex;
            UpdateCurrentStageDisplayText();
            UpdateRenderPreviewLoadingState();
            UpdatePauseStateUi();
            CurrentFileName = progress.ItemTitle;
            StatusSummary = progress.Summary;
            StageDurationText = progress.StageElapsedText;
            ProcessingFpsText = progress.ProcessingFpsText;
            LastHeartbeat = progress.IsHeartbeat ? $"{DateTime.Now:HH:mm:ss} {progress.CurrentStatus}" : StatusSummary;
            UpdateRenderPreviewIndicators(progress.EtaText, progress.CurrentStatus);

            TrackFrameProgress(progress);

            if (!string.IsNullOrWhiteSpace(progress.LogLine))
            {
                Log(progress.LogLine);
            }

            if (_closeRenderModeAfterCurrentSkip
                && progress.Progress >= 100
                && string.Equals(progress.CurrentStatus, LocalizedStrings.LogSkippingEncode, StringComparison.Ordinal))
            {
                _closeRenderModeAfterCurrentSkip = false;
            }

            OnQueueStateChanged();
        });
    }

    private void HandleImageRenderProgress(ImageRenderProgress progress)
    {
        PostToUi(() =>
        {
            ImageRenderProgress = progress.Progress;
            ImageRenderStatusText = LocalizedStrings.ImageRenderProgress(progress.Completed, progress.Total);
            ImageRenderDetailText = progress.CurrentFile;
            CurrentItemTitle = progress.CurrentFile;
            OverallProgress = progress.Progress;
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

        if (IsRenderPaused)
        {
            _pipeline.SetPaused(false);
            IsRenderPaused = false;
        }

        _closeRenderModeAfterCurrentSkip = !_keepRenderModeForAutoResume && IsLastPendingRenderItem(current);
        _keepRenderModeForAutoResume = false;
        if (_finishAfterCurrentRequested)
        {
            _pipeline.RequestStopAfterCurrentItem();
        }

        current.SkipRequested = true;
        current.IsInterrupted = true;
        Log(LocalizedStrings.LogSkippingEncode);
    }

    private void TogglePause()
    {
        if (!CanPauseRender)
        {
            return;
        }

        var paused = !IsRenderPaused;
        var pauseFailureReason = _pipeline.TrySetPaused(paused);
        if (!paused && !string.IsNullOrWhiteSpace(pauseFailureReason))
        {
            IsRenderPaused = false;
            var current = GetCurrentItem();
            if (current is not null)
            {
                if (!_pendingAutoResumeItems.Contains(current))
                {
                    _pendingAutoResumeItems.Add(current);
                }

                _keepRenderModeForAutoResume = true;
                current.SkipRequested = true;
                current.IsInterrupted = true;
            }

            Log(LocalizedStrings.LogBatchFailed(LocalizedStrings.Get("PausedProcessExitedLog").Replace("{0}", pauseFailureReason)));
            _pipeline.RequestStopAfterCurrentItem();
            _pipeline.AbortCurrentItem();
            ShowPausedProcessExitedDialog(current?.Title ?? CurrentItemTitle, pauseFailureReason);
            return;
        }

        IsRenderPaused = paused;
    }

    private void FinishAfterCurrentItem()
    {
        if (!CanFinishAfterCurrent)
        {
            return;
        }

        _finishAfterCurrentRequested = !_finishAfterCurrentRequested;
        if (_finishAfterCurrentRequested)
        {
            _pipeline.RequestStopAfterCurrentItem();
            Log(LocalizedStrings.Get("RenderFinishAfterCurrentLog"));
        }
        else
        {
            _pipeline.CancelStopAfterCurrentItemRequest();
            Log(LocalizedStrings.Get("RenderFinishAfterCurrentCancelledLog"));
        }

        OnPropertyChanged(nameof(CanFinishAfterCurrent));
        OnPropertyChanged(nameof(RenderFinishAfterCurrentText));
        _finishAfterCurrentCommand.RaiseCanExecuteChanged();
    }

    private async Task<List<QueueItemViewModel>> PrepareAutoResumeRunListAsync(PipelineOptions options, CancellationToken ct)
    {
        if (_pendingAutoResumeItems.Count == 0)
        {
            return new List<QueueItemViewModel>();
        }

        var candidates = _pendingAutoResumeItems
            .Distinct()
            .ToArray();
        _pendingAutoResumeItems.Clear();

        var result = new List<QueueItemViewModel>(candidates.Length);
        foreach (var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            item.SkipRequested = false;
            item.ForceOverwrite = false;
            item.ResumeRequested = false;
            item.ResumeProcessedFrames = 0;
            item.IsSkipped = false;
            item.IsInterrupted = false;

            var resumeResult = await TryGetAutoResumeInfoAsync(item, options, ct).ConfigureAwait(true);
            if (!resumeResult.Success)
            {
                Log(LocalizedStrings.Get("PausedProcessExitedAutoResumeFailed").Replace("{0}", item.Title));
                continue;
            }

            item.ResumeRequested = true;
            item.ResumeProcessedFrames = resumeResult.ProcessedFrames;
            result.Add(item);
        }

        if (result.Count > 0)
        {
            Log(LocalizedStrings.Get("PausedProcessExitedAutoResumeStarting"));
        }

        return result;
    }

    private async Task<(bool Success, int ProcessedFrames)> TryGetAutoResumeInfoAsync(
        QueueItemViewModel item,
        PipelineOptions options,
        CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            item.ResumeSourceOutputPath = string.Empty;
            var result = await TryGetResumeInfoAsync(item, options, ct).ConfigureAwait(true);
            if (result.Success)
            {
                return result;
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(true);
        }

        return (false, 0);
    }

    private async Task<List<QueueItemViewModel>> PrepareNextRunListAsync(
        IReadOnlyList<QueueItemViewModel> previousRunItems,
        PipelineOptions options,
        CancellationToken ct)
    {
        var autoResumeItems = await PrepareAutoResumeRunListAsync(options, ct).ConfigureAwait(true);
        if (previousRunItems.Count == 0)
        {
            return autoResumeItems;
        }

        var result = new List<QueueItemViewModel>(previousRunItems.Count);
        var autoResumeSet = new HashSet<QueueItemViewModel>(autoResumeItems);

        foreach (var item in previousRunItems)
        {
            if (autoResumeSet.Contains(item))
            {
                if (!item.SkipInRender)
                {
                    result.Add(item);
                }
                else
                {
                    MarkItemSkipped(item);
                }
                continue;
            }

            if (item.SkipInRender)
            {
                MarkItemSkipped(item);
                continue;
            }

            if (!item.IsCompleted && !item.IsInterruptedOrSkipped)
            {
                result.Add(item);
            }
        }

        foreach (var item in autoResumeItems)
        {
            if (!item.SkipInRender && !result.Contains(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private bool IsLastPendingRenderItem(QueueItemViewModel current)
    {
        foreach (var item in CurrentRenderItems)
        {
            if (ReferenceEquals(item, current))
            {
                continue;
            }

            if (!item.IsCompleted && !item.IsInterruptedOrSkipped)
            {
                return false;
            }
        }

        return true;
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

    private RenderSessionResults? BuildRenderSessionResults(IEnumerable<QueueItemViewModel> sessionItems, TimeSpan totalElapsed)
    {
        var results = sessionItems
            .Where(item => item.IsCompleted)
            .OrderBy(item => item.Index)
            .Select(item =>
            {
                var averageFpsText = "--";
                if (TryParseDisplayedDuration(item.ElapsedText, out var itemElapsed)
                    && itemElapsed.TotalSeconds > 0
                    && _sessionFrameProgress.TryGetValue(item.Index, out var frameProgress)
                    && frameProgress.total > 0)
                {
                    var fps = frameProgress.total / itemElapsed.TotalSeconds;
                    averageFpsText = fps.ToString("0.0", CultureInfo.InvariantCulture);
                }

                return new RenderSessionItemResult(item.Title, item.ElapsedText, averageFpsText, item.OutputPath);
            })
            .ToArray();

        return results.Length == 0
            ? null
            : new RenderSessionResults(FormatDuration(totalElapsed), results);
    }

    private RenderSessionResults? BuildImageRenderSessionResults(IEnumerable<QueueItemViewModel> sessionItems, TimeSpan totalElapsed)
    {
        var results = sessionItems
            .Where(item => File.Exists(item.OutputPath))
            .OrderBy(item => item.Index)
            .Select(item => new RenderSessionItemResult(
                item.Title,
                string.IsNullOrWhiteSpace(item.ElapsedText) ? FormatDuration(TimeSpan.Zero) : item.ElapsedText,
                "--",
                item.OutputPath,
                ShowAverageFps: false))
            .ToArray();

        return results.Length == 0
            ? null
            : new RenderSessionResults(FormatDuration(totalElapsed), results);
    }

    private static bool TryParseDisplayedDuration(string text, out TimeSpan value)
    {
        return TimeSpan.TryParseExact(
            text,
            @"hh\:mm\:ss",
            CultureInfo.InvariantCulture,
            out value);
    }

    private async Task<List<QueueItemViewModel>> PrepareRunListAsync(IReadOnlyList<QueueItemViewModel> items, PipelineOptions options, CancellationToken ct)
    {
        var list = new List<QueueItemViewModel>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            item.SkipInRender = false;
            item.SkipRequested = false;
            item.ForceOverwrite = false;
            item.ResumeRequested = false;
            item.ResumeProcessedFrames = 0;
            item.ResumeSourceOutputPath = string.Empty;
            item.IsSkipped = false;
            item.IsInterrupted = false;

            var overwriteAllowed = options.Overwrite;
            if (File.Exists(item.OutputPath) && !overwriteAllowed)
            {
                var decision = await ResolveOutputConflictAsync(item).ConfigureAwait(true);
                switch (decision)
                {
                    case OutputConflictDecision.ResumeAll:
                        _sessionOutputDecision = OutputConflictDecision.ResumeAll;
                        if (await TryGetResumeInfoAsync(item, options, ct).ConfigureAwait(true) is var resumeAllResult && resumeAllResult.Success)
                        {
                            item.ResumeRequested = true;
                            item.ResumeProcessedFrames = resumeAllResult.ProcessedFrames;
                            list.Add(item);
                            continue;
                        }
                        decision = await ResolveResumeFallbackAsync(item, options).ConfigureAwait(true);
                        if (decision == OutputConflictDecision.Replace)
                        {
                            _sessionOutputDecision = OutputConflictDecision.ReplaceAll;
                            item.ForceOverwrite = true;
                            list.Add(item);
                            continue;
                        }

                        _sessionOutputDecision = OutputConflictDecision.SkipAll;
                        MarkItemSkipped(item);
                        continue;
                    case OutputConflictDecision.Resume:
                        if (await TryGetResumeInfoAsync(item, options, ct).ConfigureAwait(true) is var resumeResult && resumeResult.Success)
                        {
                            item.ResumeRequested = true;
                            item.ResumeProcessedFrames = resumeResult.ProcessedFrames;
                            list.Add(item);
                            continue;
                        }
                        decision = await ResolveResumeFallbackAsync(item, options).ConfigureAwait(true);
                        if (decision == OutputConflictDecision.Replace)
                        {
                            item.ForceOverwrite = true;
                            list.Add(item);
                            continue;
                        }

                        MarkItemSkipped(item);
                        continue;
                    case OutputConflictDecision.ReplaceAll:
                        _sessionOutputDecision = OutputConflictDecision.ReplaceAll;
                        ResetExistingOutputState(item);
                        item.ForceOverwrite = true;
                        list.Add(item);
                        continue;
                    case OutputConflictDecision.Replace:
                        ResetExistingOutputState(item);
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

            if (overwriteAllowed && File.Exists(item.OutputPath) && !item.ResumeRequested)
            {
                ResetExistingOutputState(item);
            }

            list.Add(item);
        }

        return list;
    }

    private async Task<(bool Success, int ProcessedFrames)> TryGetResumeInfoAsync(QueueItemViewModel item, PipelineOptions options, CancellationToken ct)
    {
        var statusState = new ResumePreflightUiState
        {
            PhaseText = LocalizedStrings.Get("ResumePreflightChecking"),
            DetailText = LocalizedStrings.Get("ResumePreflightLoadingFile"),
            FpsText = "--"
        };
        var statusSync = new object();
        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stopwatch = Stopwatch.StartNew();

        void PublishStatus()
        {
            string phaseText;
            string detailText;
            string? etaTextOverride;
            string fpsText;
            lock (statusSync)
            {
                phaseText = statusState.PhaseText;
                detailText = statusState.DetailText;
                etaTextOverride = statusState.EtaTextOverride;
                fpsText = statusState.FpsText;
            }

            PostToUi(() => ApplyResumePreflightStatus(item, phaseText, detailText, stopwatch.Elapsed, etaTextOverride, fpsText));
        }

        PublishStatus();
        var statusLoop = Task.Run(async () =>
        {
            try
            {
                while (!statusCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, statusCts.Token).ConfigureAwait(false);
                    PublishStatus();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, statusCts.Token);

        try
        {
            var result = await Task.Run(() =>
            {
                var success = TryGetResumeInfo(
                    item,
                    options,
                    out var processedFrames,
                    phase =>
                    {
                        lock (statusSync)
                        {
                            statusState.PhaseText = phase;
                            statusState.DetailText = LocalizedStrings.Get("ResumePreflightLoadingFile");
                            statusState.EtaTextOverride = null;
                            statusState.FpsText = "--";
                        }
                    },
                    telemetry =>
                    {
                        lock (statusSync)
                        {
                            if (!string.IsNullOrWhiteSpace(telemetry.PhaseText))
                            {
                                statusState.PhaseText = telemetry.PhaseText;
                            }

                            if (!string.IsNullOrWhiteSpace(telemetry.DetailText))
                            {
                                statusState.DetailText = telemetry.DetailText;
                            }

                            statusState.EtaTextOverride = telemetry.EtaText;
                            statusState.FpsText = string.IsNullOrWhiteSpace(telemetry.FpsText) ? "--" : telemetry.FpsText;
                        }
                    });
                return (success, processedFrames);
            }, ct).ConfigureAwait(true);

            PublishStatus();
            return result;
        }
        finally
        {
            statusCts.Cancel();
            try
            {
                await statusLoop.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<OutputConflictDecision> ResolveOutputConflictAsync(QueueItemViewModel item)
    {
        if (_sessionOutputDecision == OutputConflictDecision.SkipAll ||
            _sessionOutputDecision == OutputConflictDecision.ReplaceAll ||
            _sessionOutputDecision == OutputConflictDecision.ResumeAll)
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

    private async Task<OutputConflictDecision> ResolveResumeFallbackAsync(QueueItemViewModel item, PipelineOptions options)
    {
        await Task.Yield();

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return OutputConflictDecision.Skip;
        }

        return ShowResumeUnavailableDialog(item, GetResumeUnavailableReason(item, options));
    }

    private bool TryGetResumeInfo(
        QueueItemViewModel item,
        PipelineOptions options,
        out int processedFrames,
        Action<string>? reportPhase = null,
        Action<ResumePreflightTelemetry>? reportTelemetry = null)
    {
        processedFrames = 0;
        item.ResumeSourceOutputPath = item.OutputPath;
        if (!IsResumeOutputUsable(item.OutputPath))
        {
            return false;
        }

        if (!PipelineService.TryLoadResumeState(item.OutputPath, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return TryEstimateResumeInfoFromOutput(item, options, out processedFrames);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var sourcePath = root.TryGetProperty("SourcePath", out var sourcePathNode) ? sourcePathNode.GetString() ?? string.Empty : string.Empty;
            var outputPath = root.TryGetProperty("OutputPath", out var outputPathNode) ? outputPathNode.GetString() ?? string.Empty : string.Empty;
            var codec = root.TryGetProperty("Codec", out var codecNode) ? codecNode.GetString() ?? string.Empty : string.Empty;
            var targetHeight = root.TryGetProperty("TargetHeight", out var targetHeightNode) ? targetHeightNode.GetInt32() : 0;
            var upscalerBackend = root.TryGetProperty("UpscalerBackend", out var upscalerBackendNode) ? upscalerBackendNode.GetString() ?? string.Empty : string.Empty;
            var refinerBackend = root.TryGetProperty("RefinerBackend", out var refinerBackendNode) ? refinerBackendNode.GetString() ?? string.Empty : string.Empty;
            var parsedProcessedFrames = root.TryGetProperty("ProcessedFrames", out var processedFramesNode) ? processedFramesNode.GetInt32() : 0;
            var totalFrames = root.TryGetProperty("TotalFrames", out var totalFramesNode) ? totalFramesNode.GetInt32() : 0;
            var complete = root.TryGetProperty("Complete", out var completeNode) && completeNode.GetBoolean();
            var canResume = root.TryGetProperty("CanResume", out var canResumeNode) && canResumeNode.GetBoolean();
            var expectedCodec = options.UseX265 ? "libx265" : "libx264";
            var expectedTargetHeight = options.TargetHeight;
            var isValid = canResume
                && !complete
                && totalFrames > parsedProcessedFrames
                && string.Equals(sourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(outputPath, item.OutputPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(codec, expectedCodec, StringComparison.OrdinalIgnoreCase)
                && targetHeight == expectedTargetHeight
                && string.Equals(upscalerBackend, options.UpscalerBackend.ToString(), StringComparison.Ordinal)
                && string.Equals(refinerBackend, options.RefinerBackend.ToString(), StringComparison.Ordinal);
            if (isValid && parsedProcessedFrames <= 0)
            {
                return TryEstimateResumeInfoFromOutput(item, options, out processedFrames);
            }

            if (!isValid)
            {
                return TryEstimateResumeInfoFromOutput(item, options, out processedFrames);
            }
            else
            {
                processedFrames = parsedProcessedFrames;
            }

            return isValid;
        }
        catch
        {
            return TryEstimateResumeInfoFromOutput(item, options, out processedFrames);
        }
    }

    private void ApplyResumePreflightStatus(
        QueueItemViewModel item,
        string phaseText,
        string detailText,
        TimeSpan elapsed,
        string? etaTextOverride,
        string fpsText)
    {
        CurrentStage = LocalizedStrings.LogPreparing;
        CurrentStatusLine = phaseText;
        UpdateCurrentStageDisplayText();
        CurrentItemTitle = item.Title;
        CurrentItemDetail = detailText;
        CurrentFrameTimestampText = string.Empty;
        OverallProgress = 0;
        ElapsedText = FormatDuration(elapsed);
        StageDurationText = FormatDuration(elapsed);
        EtaText = string.IsNullOrWhiteSpace(etaTextOverride)
            ? EstimateResumePreflightEta(item, phaseText, elapsed)
            : etaTextOverride;
        ProcessingFpsText = string.IsNullOrWhiteSpace(fpsText) ? "--" : fpsText;
        StatusSummary = phaseText;
        CurrentFileName = item.Title;

        item.Stage = LocalizedStrings.LogPreparing;
        item.Progress = 0;
        item.ProgressText = "0%";
        item.OutputState = phaseText;
        item.Detail = detailText;
        item.ElapsedText = FormatDuration(elapsed);
        item.EtaText = EtaText;
        item.IsBusy = true;

        UpdateRenderPreviewLoadingState();
        UpdatePauseStateUi();
        OnQueueStateChanged();
    }

    private string EstimateResumePreflightEta(QueueItemViewModel item, string phaseText, TimeSpan elapsed)
    {
        try
        {
            var sizeMb = File.Exists(item.OutputPath)
                ? new FileInfo(item.OutputPath).Length / (1024d * 1024d)
                : 0d;
            var recovering = string.Equals(phaseText, LocalizedStrings.Get("ResumePreflightRecovering"), StringComparison.Ordinal);
            var totalSeconds = (recovering
                ? Math.Clamp(10 + sizeMb / 35d, 10, 180)
                : Math.Clamp(4 + sizeMb / 180d, 5, 30)) * 10;
            var remaining = GetExtendedCountdown(TimeSpan.FromSeconds(totalSeconds), elapsed, TimeSpan.FromSeconds(15));
            return $"~{FormatDuration(remaining)}";
        }
        catch
        {
            return "~00:01:00";
        }
    }

    private static TimeSpan GetExtendedCountdown(TimeSpan estimatedTotal, TimeSpan elapsed, TimeSpan extensionStep)
    {
        var remaining = estimatedTotal - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            return remaining;
        }

        var step = extensionStep <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : extensionStep;
        var overtime = -remaining;
        var chunks = (int)Math.Floor(overtime.TotalSeconds / step.TotalSeconds) + 1;
        return step * chunks - overtime;
    }

    private bool TryEstimateResumeInfoFromOutput(QueueItemViewModel item, PipelineOptions options, out int processedFrames)
    {
        processedFrames = 0;
        try
        {
            var resumeOutputPath = string.IsNullOrWhiteSpace(item.ResumeSourceOutputPath) ? item.OutputPath : item.ResumeSourceOutputPath;
            if (!File.Exists(resumeOutputPath))
            {
                return false;
            }

            var ffprobePath = ResolveFfprobePath();
            if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            {
                return false;
            }

            if (!IsResumeOutputUsable(resumeOutputPath, ffprobePath))
            {
                return false;
            }

            var sourceDuration = ProbeMediaDurationSeconds(ffprobePath, item.SourcePath);
            var outputDuration = ProbeMediaDurationSeconds(ffprobePath, resumeOutputPath);
            var sourceFps = ProbeMediaFps(ffprobePath, item.SourcePath);
            if (sourceDuration <= 0 || outputDuration <= 0 || sourceFps <= 0)
            {
                return false;
            }

            if (outputDuration >= sourceDuration * 0.995)
            {
                return false;
            }

            processedFrames = Math.Max(1, (int)Math.Round(outputDuration * sourceFps, MidpointRounding.AwayFromZero));
            var estimatedTotalFrames = Math.Max(processedFrames + 1, (int)Math.Round(sourceDuration * sourceFps, MidpointRounding.AwayFromZero));
            return processedFrames > 0 && processedFrames < estimatedTotalFrames;
        }
        catch
        {
            processedFrames = 0;
            return false;
        }
    }

    private bool TryRecoverResumeOutput(
        QueueItemViewModel item,
        PipelineOptions options,
        out string recoveredOutputPath,
        Action<ResumePreflightTelemetry>? progressCallback = null)
    {
        recoveredOutputPath = string.Empty;
        try
        {
            var ffmpegPath = ResolveExistingFfmpegPath(options.FfmpegPath);
            var ffprobePath = ResolveFfprobePath();
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath) || string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            {
                return false;
            }

            var workingDirectory = Path.GetDirectoryName(item.OutputPath) ?? Environment.CurrentDirectory;
            var tempDirectory = Path.Combine(Path.GetTempPath(), "UltraFrameAI", "resume-recovered");
            Directory.CreateDirectory(tempDirectory);
            recoveredOutputPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.mkv");
            var codec = options.UseX265 ? "libx265" : "libx264";
            var preset = string.IsNullOrWhiteSpace(options.EncoderPreset) ? "slower" : options.EncoderPreset;
            var crf = options.UseX265 ? 18 : 16;
            var targetDurationSeconds = EstimateRecoverableDurationSeconds(item, ffprobePath);
            var sourceFps = ProbeMediaFps(ffprobePath, item.SourcePath);

            var args =
                $"-hide_banner -y -nostats -loglevel error -progress pipe:1 -fflags +genpts+discardcorrupt -err_detect ignore_err " +
                $"-i {Quote(item.OutputPath)} -map 0:v:0 -an -c:v {codec} -preset {preset} -crf {crf} -pix_fmt yuv420p {Quote(recoveredOutputPath)}";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            if (!process.Start())
            {
                TryDeleteFile(recoveredOutputPath);
                recoveredOutputPath = string.Empty;
                return false;
            }

            var recoveryWatch = Stopwatch.StartNew();
            var stdoutTask = Task.Run(async () =>
            {
                double outTimeSeconds = 0;
                string fpsText = "--";
                string speedText = string.Empty;
                var frameCount = 0;

                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex];
                    var value = line[(separatorIndex + 1)..];
                    switch (key)
                    {
                        case "frame":
                            frameCount = int.TryParse(value, out var parsedFrameCount)
                                ? parsedFrameCount
                                : frameCount;
                            break;
                        case "out_time_ms":
                            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var outTimeMs))
                            {
                                outTimeSeconds = outTimeMs / 1_000_000d;
                            }
                            break;
                        case "fps":
                            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps)
                                && fps > 0)
                            {
                                fpsText = $"{fps:0.0} FPS";
                            }
                            break;
                        case "speed":
                            speedText = value;
                            if (string.Equals(fpsText, "--", StringComparison.Ordinal))
                            {
                                var parsedSpeed = ParseSpeedMultiplier(speedText);
                                if (parsedSpeed > 0 && sourceFps > 0)
                                {
                                    fpsText = $"~{sourceFps * parsedSpeed:0.0} FPS";
                                }
                            }
                            break;
                        case "progress":
                            if (string.Equals(fpsText, "--", StringComparison.Ordinal) && frameCount > 0 && recoveryWatch.Elapsed.TotalSeconds > 0.1)
                            {
                                fpsText = $"~{frameCount / recoveryWatch.Elapsed.TotalSeconds:0.0} FPS";
                            }
                            progressCallback?.Invoke(new ResumePreflightTelemetry(
                                PhaseText: LocalizedStrings.Get("ResumePreflightRecovering"),
                                DetailText: LocalizedStrings.Get("ResumePreflightLoadingFile"),
                                EtaText: BuildRecoveryEtaText(targetDurationSeconds, outTimeSeconds, speedText, recoveryWatch.Elapsed),
                                FpsText: fpsText));
                            break;
                    }
                }
            });
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0 || !IsResumeOutputUsable(recoveredOutputPath, ffprobePath))
            {
                TryDeleteFile(recoveredOutputPath);
                recoveredOutputPath = string.Empty;
                return false;
            }

            return true;
        }
        catch
        {
            TryDeleteFile(recoveredOutputPath);
            recoveredOutputPath = string.Empty;
            return false;
        }
    }

    private double EstimateRecoverableDurationSeconds(QueueItemViewModel item, string ffprobePath)
    {
        try
        {
            var sourceDuration = ProbeMediaDurationSeconds(ffprobePath, item.SourcePath);
            if (PipelineService.TryLoadResumeState(item.OutputPath, out var json) && !string.IsNullOrWhiteSpace(json))
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var processedFrames = root.TryGetProperty("ProcessedFrames", out var processedFramesNode) ? processedFramesNode.GetInt32() : 0;
                var totalFrames = root.TryGetProperty("TotalFrames", out var totalFramesNode) ? totalFramesNode.GetInt32() : 0;
                if (sourceDuration > 0 && processedFrames > 0 && totalFrames > processedFrames)
                {
                    var estimatedDuration = sourceDuration * processedFrames / totalFrames;
                    if (estimatedDuration > 0)
                    {
                        return estimatedDuration;
                    }
                }
            }

            var probedOutputDuration = ProbeMediaDurationSeconds(ffprobePath, item.OutputPath);
            if (probedOutputDuration > 0)
            {
                return probedOutputDuration;
            }

            return sourceDuration > 0 ? sourceDuration * 0.5 : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string BuildRecoveryEtaText(double targetDurationSeconds, double outTimeSeconds, string speedText, TimeSpan elapsed)
    {
        if (targetDurationSeconds > 0 && outTimeSeconds > 0)
        {
            var clampedProcessedSeconds = Math.Min(targetDurationSeconds, outTimeSeconds);
            var remainingProcessedSeconds = Math.Max(0, targetDurationSeconds - clampedProcessedSeconds);
            var speed = ParseSpeedMultiplier(speedText);
            if (speed > 0)
            {
                return $"~{FormatDuration(TimeSpan.FromSeconds(Math.Max(1, remainingProcessedSeconds / speed)))}";
            }

            var progress = clampedProcessedSeconds / targetDurationSeconds;
            if (progress > 0.001)
            {
                var totalEstimatedSeconds = elapsed.TotalSeconds / progress;
                var remaining = Math.Max(1, totalEstimatedSeconds - elapsed.TotalSeconds);
                return $"~{FormatDuration(TimeSpan.FromSeconds(remaining))}";
            }
        }

        return $"~{FormatDuration(GetExtendedCountdown(TimeSpan.FromSeconds(60), elapsed, TimeSpan.FromSeconds(15)))}";
    }

    private static double ParseSpeedMultiplier(string speedText)
    {
        if (string.IsNullOrWhiteSpace(speedText))
        {
            return 0;
        }

        var normalized = speedText.Trim().TrimEnd('x', 'X');
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed)
            ? speed
            : 0;
    }

    private bool IsResumeOutputUsable(string outputPath)
    {
        var ffprobePath = ResolveFfprobePath();
        return !string.IsNullOrWhiteSpace(ffprobePath)
            && File.Exists(ffprobePath)
            && IsResumeOutputUsable(outputPath, ffprobePath);
    }

    private static bool IsResumeOutputUsable(string outputPath, string ffprobePath)
    {
        try
        {
            if (!File.Exists(outputPath))
            {
                return false;
            }

            var output = RunFfprobe(
                ffprobePath,
                $"-v error -select_streams v:0 -show_entries stream=codec_type -of default=noprint_wrappers=1:nokey=1 {Quote(outputPath)}",
                Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

            return string.Equals(output.Trim(), "video", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string ResolveFfprobePath()
    {
        return ResolveToolPath("ffprobe.exe", @"C:\ffmpeg\bin\ffprobe.exe");
    }

    private string ResolveExistingFfmpegPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        return ResolveToolPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe");
    }

    private static double ProbeMediaDurationSeconds(string ffprobePath, string mediaPath)
    {
        var output = RunFfprobe(
            ffprobePath,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Quote(mediaPath)}",
            Path.GetDirectoryName(mediaPath) ?? Environment.CurrentDirectory);
        return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 0.0;
    }

    private static double ProbeMediaFps(string ffprobePath, string mediaPath)
    {
        var output = RunFfprobe(
            ffprobePath,
            $"-v error -select_streams v:0 -show_entries stream=avg_frame_rate -of default=noprint_wrappers=1:nokey=1 {Quote(mediaPath)}",
            Path.GetDirectoryName(mediaPath) ?? Environment.CurrentDirectory);
        return PipelineService.ParseFraction(output.Trim());
    }

    private static string RunFfprobe(string ffprobePath, string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            }
        };

        if (!process.Start())
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : string.Empty;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private string GetResumeUnavailableReason(QueueItemViewModel item, PipelineOptions options)
    {
        try
        {
            if (!File.Exists(item.OutputPath))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonMissingOutput");
            }

            var ffprobePath = ResolveFfprobePath();
            if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonProbeUnavailable");
            }

            if (!IsResumeOutputUsable(item.OutputPath, ffprobePath))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonInvalidOutput");
            }

            var sourceDuration = ProbeMediaDurationSeconds(ffprobePath, item.SourcePath);
            var outputDuration = ProbeMediaDurationSeconds(ffprobePath, item.OutputPath);
            if (sourceDuration > 0 && outputDuration >= sourceDuration * 0.995)
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonAlreadyComplete");
            }

            if (!PipelineService.TryLoadResumeState(item.OutputPath, out var json) || string.IsNullOrWhiteSpace(json))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonMissingState");
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var complete = root.TryGetProperty("Complete", out var completeNode) && completeNode.GetBoolean();
            if (complete)
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonAlreadyComplete");
            }

            var sourcePath = root.TryGetProperty("SourcePath", out var sourcePathNode) ? sourcePathNode.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(sourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonSourceMismatch");
            }

            var codec = root.TryGetProperty("Codec", out var codecNode) ? codecNode.GetString() ?? string.Empty : string.Empty;
            var expectedCodec = options.UseX265 ? "libx265" : "libx264";
            if (!string.Equals(codec, expectedCodec, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizedStrings.Get("ResumeUnavailableReasonSettingsChanged");
            }

            return LocalizedStrings.Get("ResumeUnavailableReasonGeneric");
        }
        catch
        {
            return LocalizedStrings.Get("ResumeUnavailableReasonGeneric");
        }
    }

    private OutputConflictDecision ShowResumeUnavailableDialog(QueueItemViewModel item, string reasonText)
    {
        var owner = TryGetActiveOwnerWindow();
        var dialog = new ResumeUnavailableDialog(item.OutputPath, reasonText);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Decision : OutputConflictDecision.Skip;
    }

    private void ShowPausedProcessExitedDialog(string fileName, string processName)
    {
        var owner = TryGetActiveOwnerWindow();
        var dialog = new PausedProcessExitedDialog(fileName, processName);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        _ = dialog.ShowDialog();
    }

    private static Window? TryGetActiveOwnerWindow()
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null)
            {
                return null;
            }

            if (!app.Dispatcher.CheckAccess())
            {
                return null;
            }

            return app.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        }
        catch
        {
            return null;
        }
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

    private static void ResetExistingOutputState(QueueItemViewModel item)
    {
        PipelineService.RemoveCompletedOutput(item.OutputPath);
        try
        {
            if (File.Exists(item.OutputPath))
            {
                File.Delete(item.OutputPath);
            }
        }
        catch
        {
        }

        try
        {
            var resumeStatePath = PipelineService.GetResumeStatePath(item.OutputPath);
            if (File.Exists(resumeStatePath))
            {
                File.Delete(resumeStatePath);
            }
        }
        catch
        {
        }
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
        _pipeline.SetPaused(false);
        IsRenderPaused = false;
        _runCts?.Cancel();
        _scanCts?.Cancel();
    }

    private void UpdateQueueSummary()
    {
        QueueSummary = LocalizedStrings.LogItemCount(Items.Count);
        QueueSourceSummary = BuildQueueSourceSummary();
        QueueSourceTooltip = BuildQueueSourceTooltip();
    }

    private string BuildQueueSourceSummary()
    {
        if (Items.Count == 0)
        {
            return string.Empty;
        }

        var directories = Items
            .Select(item => Path.GetDirectoryName(item.SourcePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, NaturalStringComparer.Instance)
            .ToArray();

        if (directories.Length == 0)
        {
            return string.Empty;
        }

        if (directories.Length == 1)
        {
            return LocalizedStrings.QueueSourceSingle(directories[0]);
        }

        return LocalizedStrings.QueueSourceMultiple(directories.Length);
    }

    private string BuildQueueSourceTooltip()
    {
        if (Items.Count == 0)
        {
            return string.Empty;
        }

        var directories = Items
            .Select(item => Path.GetDirectoryName(item.SourcePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, NaturalStringComparer.Instance)
            .ToArray();

        return directories.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, directories);
    }

    private void UpdateSelectionDetails()
    {
        if (SelectedItem is null)
        {
            CurrentItemTitle = LocalizedStrings.LogNoItemSelected;
            CurrentItemDetail = string.Empty;
            CurrentFrameTimestampText = string.Empty;
            CurrentFileName = string.Empty;
            ClearRenderPreviewPaths();
            UpdateRenderPreviewLoadingState();
            return;
        }

        CurrentItemTitle = SelectedItem.Title;
        CurrentItemDetail = SelectedItem.SourcePath;
        CurrentFrameTimestampText = string.Empty;
        CurrentFileName = Path.GetFileName(SelectedItem.SourcePath);
        ClearRenderPreviewPaths();
        UpdateRenderPreviewLoadingState();
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
        CurrentFrameTimestampText = string.Empty;
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

    private void UpdateRenderPreviewLoadingState()
    {
        OnPropertyChanged(nameof(IsRenderPreviewLoading));
        OnPropertyChanged(nameof(RenderPreviewLoadingText));
    }

    private void UpdatePauseStateUi()
    {
        OnPropertyChanged(nameof(CanPauseRender));
        OnPropertyChanged(nameof(RenderPauseGlyph));
        OnPropertyChanged(nameof(RenderPauseToolTip));
        OnPropertyChanged(nameof(CanFinishAfterCurrent));
        OnPropertyChanged(nameof(RenderFinishAfterCurrentText));
        _togglePauseCommand.RaiseCanExecuteChanged();
        _finishAfterCurrentCommand.RaiseCanExecuteChanged();
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

    private static void ClearRenderSkipSelections(IEnumerable<QueueItemViewModel> items)
    {
        foreach (var item in items)
        {
            item.SkipInRender = false;
            item.SkipRequested = false;
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
        PersistAppSettings();
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
        RefreshTargetOptions();
        _upscalerBackendOptions = BuildUpscalerBackendOptions();
        _refinerBackendOptions = BuildRefinerBackendOptions();
        RebuildGpuOptions(_selectedGpuOption?.Key);
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(CurrentLanguageFlagPath));
        OnPropertyChanged(nameof(FfmpegDirectoryDisplay));
        OnPropertyChanged(nameof(UpscalerBackendOptions));
        OnPropertyChanged(nameof(RefinerBackendOptions));
        OnPropertyChanged(nameof(GpuOptions));

        if (Items.Count == 0)
        {
            StatusSummary = LocalizedStrings.LogReady;
            LastHeartbeat = LocalizedStrings.LogIdle;
            CurrentStage = LocalizedStrings.LogIdle;
            CurrentItemTitle = LocalizedStrings.LogNoItemSelected;
            CurrentItemDetail = string.Empty;
            CurrentFrameTimestampText = string.Empty;
            CurrentStatusLine = LocalizedStrings.LogWaitingForInput;
            UpdateCurrentStageDisplayText();
            QueueSummary = LocalizedStrings.LogItemCount(0);
            StageDurationText = "--:--:--";
            ProcessingFpsText = "--";
            UpdateActionStates();
            UpdatePauseStateUi();
            CurrentFileName = string.Empty;
        }
        else
        {
            UpdateQueueSummary();
            UpdateSelectionDetails();
            UpdateActionStates();
            UpdatePauseStateUi();
        }

        if (IsScanOverlayVisible)
        {
            ScanStatusText = LocalizedStrings.ScanningFolder;
            ScanFoundText = LocalizedStrings.LogFoundVideoFiles(_scanFoundCount);
        }
    }

    private static IReadOnlyList<TargetFormatOption> BuildTargetOptions(string? selectedTarget, bool include16K)
    {
        var options = new List<TargetFormatOption>
        {
            new("720p", "720p (HD)"),
            new("1080p", "1080p (Full HD)"),
            new("1440p", "1440p (QHD)"),
            new("2160p", "2160p (4K)"),
            new("4320p", "4320p (8K)")
        };
        if (include16K)
        {
            options.Add(new TargetFormatOption("8640p", "8640p (16K)"));
        }

        if (!string.IsNullOrWhiteSpace(selectedTarget) &&
            !(include16K ? SupportedImageTargetValues : SupportedTargetValues).Contains(selectedTarget, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new TargetFormatOption(selectedTarget, selectedTarget));
        }

        options.Add(new TargetFormatOption(CustomTargetActionValue, LocalizedStrings.CustomTargetValue, true));
        return options;
    }

    private IReadOnlyList<UpscalerBackendOption> BuildUpscalerBackendOptions()
    {
        var options = new List<UpscalerBackendOption>
        {
            new(UpscalerBackendKind.RealEsrgan, LocalizedStrings.UpscalerBackendRealEsrgan)
        };

        if (EnableStableSr)
        {
            options.Add(new UpscalerBackendOption(UpscalerBackendKind.StableSrExternal, LocalizedStrings.UpscalerBackendStableSr));
        }

        if (EnableSupir)
        {
            options.Add(new UpscalerBackendOption(UpscalerBackendKind.SupirExternal, LocalizedStrings.UpscalerBackendSupir));
        }

        return options;
    }

    private IReadOnlyList<RefinerBackendOption> BuildRefinerBackendOptions()
    {
        var options = new List<RefinerBackendOption>
        {
            new(RefinerBackendKind.None, LocalizedStrings.RefinerBackendNone)
        };

        if (EnableStableSr)
        {
            options.Add(new RefinerBackendOption(RefinerBackendKind.StableSrExternal, LocalizedStrings.RefinerBackendStableSr));
        }

        if (EnableSupir)
        {
            options.Add(new RefinerBackendOption(RefinerBackendKind.SupirExternal, LocalizedStrings.RefinerBackendSupir));
        }

        return options;
    }

    private void RebuildGpuOptions(string? preferredKey = null)
    {
        var options = BuildGpuOptions();
        var keyToSelect = preferredKey ?? _selectedGpuOption?.Key ?? GpuDeviceOption.AutoKey;
        _gpuOptions = options;
        _selectedGpuOption = options.FirstOrDefault(option => string.Equals(option.Key, keyToSelect, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault();
    }

    private IReadOnlyList<GpuDeviceOption> BuildGpuOptions()
    {
        var options = new List<GpuDeviceOption>();
        var renderableDevices = GetRenderableGpuDevices();
        var strongestDevice = renderableDevices.FirstOrDefault();
        var autoLabel = strongestDevice is null
            ? LocalizedStrings.GpuSelectionAuto
            : LocalizedStrings.GpuSelectionAutoWithName(strongestDevice.Name);
        options.Add(new GpuDeviceOption(GpuDeviceOption.AutoKey, autoLabel, strongestDevice is null ? null : strongestDevice.BackendGpuId ?? strongestDevice.DeviceId));

        foreach (var device in renderableDevices)
        {
            var resolvedGpuId = device.BackendGpuId ?? device.DeviceId;
            options.Add(new GpuDeviceOption(
                resolvedGpuId.ToString(CultureInfo.InvariantCulture),
                BuildGpuLabel(device),
                resolvedGpuId));
        }

        return options;
    }

    private IReadOnlyList<DetectedGpuDevice> GetRenderableGpuDevices()
    {
        var backendCapable = _detectedGpuDevices
            .Where(device => device.BackendGpuId.HasValue)
            .ToArray();

        return backendCapable.Length > 0
            ? backendCapable
            : _detectedGpuDevices;
    }

    private static string BuildGpuLabel(DetectedGpuDevice device)
    {
        return device.Name;
    }

    private static string ComputeHardwareSignature(IReadOnlyList<DetectedGpuDevice> devices)
    {
        if (devices.Count == 0)
        {
            return "none";
        }

        return string.Join(
            "||",
            devices.Select(device => $"{device.Name.Trim().ToUpperInvariant()}|{device.MemoryMb?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));
    }

    private void EvaluateStartupBenchmarkPromptState()
    {
        _startupBenchmarkPromptKind = !_startupBenchmarkCompleted && !_startupBenchmarkPromptShown
            ? StartupBenchmarkPromptKind.Welcome
            : StartupBenchmarkPromptKind.None;

        OnPropertyChanged(nameof(ShouldOfferStartupBenchmark));
        OnPropertyChanged(nameof(CurrentStartupBenchmarkPromptKind));
    }

    private static UpscalerBackendKind NormalizeUpscalerBackendKind(string? raw) => raw?.Trim() switch
    {
        nameof(UpscalerBackendKind.StableSrExternal) => UpscalerBackendKind.StableSrExternal,
        nameof(UpscalerBackendKind.SupirExternal) => UpscalerBackendKind.SupirExternal,
        _ => UpscalerBackendKind.RealEsrgan
    };

    private static RefinerBackendKind NormalizeRefinerBackendKind(string? raw) => raw?.Trim() switch
    {
        nameof(RefinerBackendKind.StableSrExternal) => RefinerBackendKind.StableSrExternal,
        nameof(RefinerBackendKind.SupirExternal) => RefinerBackendKind.SupirExternal,
        _ => RefinerBackendKind.None
    };

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

    private void ClearQueueForRenderModeChange()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= QueueItem_PropertyChanged;
        }

        _attachedQueueItems.Clear();
        Items.Clear();
        SelectedItem = null;
        CurrentFileName = string.Empty;
        ClearRenderPreviewPaths();
        UpdateQueueSummary();
        UpdateSelectionDetails();
        OnPropertyChanged(nameof(QueueSelectAllState));
        OnQueueStateChanged();
    }

    private void CurrentRenderItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentRenderItemsCountText));
        OnPropertyChanged(nameof(RenderSkipAllState));
        UpdatePauseStateUi();
    }

    private void AttachQueueItem(QueueItemViewModel item)
    {
        if (_attachedQueueItems.Add(item))
        {
            item.PropertyChanged += QueueItem_PropertyChanged;
        }
    }

    private void ReindexItems()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
        }
    }

    private void AppendSortedItems(IEnumerable<QueueItemViewModel> items)
    {
        foreach (var item in items
                     .OrderBy(item => item.Title, NaturalStringComparer.Instance)
                     .ThenBy(item => item.SourcePath, NaturalStringComparer.Instance))
        {
            item.ShowPathInQueue = ShowQueuePaths;
            AttachQueueItem(item);
            Items.Add(item);
        }
    }

    private void ApplyQueuePathVisibilityToItems()
    {
        foreach (var item in Items)
        {
            item.ShowPathInQueue = ShowQueuePaths;
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
                OnPropertyChanged(nameof(QueueSelectAllState));
                _removeItemCommand.RaiseCanExecuteChanged();
                OnQueueStateChanged();
            });
        }

        if (e.PropertyName is nameof(QueueItemViewModel.SkipInRender))
        {
            PostToUi(() =>
            {
                PersistAppSettings();
                OnPropertyChanged(nameof(RenderSkipAllState));
                OnQueueStateChanged();
            });
        }

        if (e.PropertyName is nameof(QueueItemViewModel.IsCompleted) or nameof(QueueItemViewModel.IsSkipped) or nameof(QueueItemViewModel.IsInterrupted))
        {
            PostToUi(() => OnPropertyChanged(nameof(RenderSkipAllState)));
        }
    }

    public void ToggleQueueSelectAll()
    {
        var eligibleItems = Items.Where(item => !item.IsBusy).ToArray();
        if (eligibleItems.Length == 0)
        {
            return;
        }

        var shouldSelectAll = QueueSelectAllState != true;
        foreach (var item in eligibleItems)
        {
            item.IsChecked = shouldSelectAll;
        }

        UpdateActionStates();
        OnPropertyChanged(nameof(QueueSelectAllState));
        OnQueueStateChanged();
    }

    public void ToggleRenderSkipAll()
    {
        var eligibleItems = CurrentRenderItems.Where(CanToggleSkipInRender).ToArray();
        if (eligibleItems.Length == 0)
        {
            return;
        }

        var shouldSkipAll = RenderSkipAllState != true;
        foreach (var item in eligibleItems)
        {
            item.SkipInRender = shouldSkipAll;
        }

        PersistAppSettings();
        OnPropertyChanged(nameof(RenderSkipAllState));
        OnQueueStateChanged();
    }

    private static bool CanToggleSkipInRender(QueueItemViewModel item)
        => !item.IsBusy && !item.IsCompleted && !item.IsInterruptedOrSkipped;

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

    private static void ShowPopupMessage(string title, string message)
    {
        var popup = new PopupMessageDialog(title, message);
        var owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;

        if (owner is not null && owner != popup)
        {
            popup.Owner = owner;
        }

        popup.ShowDialog();
    }

    private static int ParseInt(string? text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);

    private static string FormatDuration(TimeSpan value) => value < TimeSpan.Zero ? "--:--:--" : value.ToString(@"hh\:mm\:ss");

    private string ResolveExternalUpscalerPath()
    {
        var path = NormalizeInputPath(SelectedExternalUpscalerPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(LocalizedStrings.ExternalUpscalerPathRequired);
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(LocalizedStrings.ExternalUpscalerPathRequired, path);
        }

        return path;
    }

    private string ResolveExternalUpscalerModelDir()
    {
        if (string.IsNullOrWhiteSpace(SelectedExternalUpscalerModelDir))
        {
            return string.Empty;
        }

        return FindDirectory(SelectedExternalUpscalerModelDir, SelectedExternalUpscalerModelDir);
    }

    private string ResolveExternalUpscalerArgumentsTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedExternalUpscalerArgumentsTemplate))
        {
            throw new InvalidOperationException(LocalizedStrings.ExternalUpscalerArgumentsRequired);
        }

        return SelectedExternalUpscalerArgumentsTemplate.Trim();
    }

    private string ResolveExternalRefinerPath()
    {
        var path = NormalizeInputPath(SelectedExternalRefinerPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(LocalizedStrings.ExternalUpscalerPathRequired);
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(LocalizedStrings.ExternalUpscalerPathRequired, path);
        }

        return path;
    }

    private string ResolveExternalRefinerModelDir()
    {
        if (string.IsNullOrWhiteSpace(SelectedExternalRefinerModelDir))
        {
            return string.Empty;
        }

        return FindDirectory(SelectedExternalRefinerModelDir, SelectedExternalRefinerModelDir);
    }

    private string ResolveExternalRefinerArgumentsTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedExternalRefinerArgumentsTemplate))
        {
            throw new InvalidOperationException(LocalizedStrings.ExternalUpscalerArgumentsRequired);
        }

        return SelectedExternalRefinerArgumentsTemplate.Trim();
    }

    private static string ResolveUpscalerWorkingDirectory(string upscalerPath, string configuredWorkingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
        {
            var normalized = NormalizeInputPath(configuredWorkingDirectory);
            if (Directory.Exists(normalized))
            {
                return normalized;
            }
        }

        return Path.GetDirectoryName(upscalerPath) ?? Environment.CurrentDirectory;
    }

    private static string GetDefaultExternalUpscalerArgumentsTemplate() =>
        "-p -W {width} -H {height} -N {frameBudget} -c {channels} -i {input} -o {output} -s {scale} -m {modelDirQ} -j {threadsQ}";

    private static string GetDefaultExternalRefinerArgumentsTemplate() =>
        "-p -W {width} -H {height} -N {frameBudget} -c {channels} -i {input} -o {output} -m {modelDirQ} -j {threadsQ}";

    private void EnsureSelectedExternalUpscalerDefaults()
    {
        switch (SelectedUpscalerBackend)
        {
            case UpscalerBackendKind.StableSrExternal:
                if (string.IsNullOrWhiteSpace(_stableSrUpscalerArgumentsTemplate))
                {
                    _stableSrUpscalerArgumentsTemplate = GetDefaultExternalUpscalerArgumentsTemplate();
                }
                break;
            case UpscalerBackendKind.SupirExternal:
                if (string.IsNullOrWhiteSpace(_supirUpscalerArgumentsTemplate))
                {
                    _supirUpscalerArgumentsTemplate = GetDefaultExternalUpscalerArgumentsTemplate();
                }
                break;
        }
    }

    private void EnsureSelectedExternalRefinerDefaults()
    {
        switch (SelectedRefinerBackend)
        {
            case RefinerBackendKind.StableSrExternal:
                if (string.IsNullOrWhiteSpace(_stableSrUpscalerArgumentsTemplate))
                {
                    _stableSrUpscalerArgumentsTemplate = GetDefaultExternalRefinerArgumentsTemplate();
                }
                break;
            case RefinerBackendKind.SupirExternal:
                if (string.IsNullOrWhiteSpace(_supirUpscalerArgumentsTemplate))
                {
                    _supirUpscalerArgumentsTemplate = GetDefaultExternalRefinerArgumentsTemplate();
                }
                break;
        }
    }

    private string GetSelectedExternalUpscalerPath() => SelectedUpscalerBackend switch
    {
        UpscalerBackendKind.StableSrExternal => _stableSrUpscalerPath,
        UpscalerBackendKind.SupirExternal => _supirUpscalerPath,
        _ => string.Empty
    };

    private bool SetSelectedExternalUpscalerPath(string value)
    {
        return SelectedUpscalerBackend switch
        {
            UpscalerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerPath, value, nameof(SelectedExternalUpscalerPath)),
            UpscalerBackendKind.SupirExternal => SetField(ref _supirUpscalerPath, value, nameof(SelectedExternalUpscalerPath)),
            _ => false
        };
    }

    private string GetSelectedExternalUpscalerWorkingDirectory() => SelectedUpscalerBackend switch
    {
        UpscalerBackendKind.StableSrExternal => _stableSrUpscalerWorkingDirectory,
        UpscalerBackendKind.SupirExternal => _supirUpscalerWorkingDirectory,
        _ => string.Empty
    };

    private bool SetSelectedExternalUpscalerWorkingDirectory(string value)
    {
        return SelectedUpscalerBackend switch
        {
            UpscalerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerWorkingDirectory, value, nameof(SelectedExternalUpscalerWorkingDirectory)),
            UpscalerBackendKind.SupirExternal => SetField(ref _supirUpscalerWorkingDirectory, value, nameof(SelectedExternalUpscalerWorkingDirectory)),
            _ => false
        };
    }

    private string GetSelectedExternalUpscalerModelDir() => SelectedUpscalerBackend switch
    {
        UpscalerBackendKind.StableSrExternal => _stableSrUpscalerModelDir,
        UpscalerBackendKind.SupirExternal => _supirUpscalerModelDir,
        _ => string.Empty
    };

    private bool SetSelectedExternalUpscalerModelDir(string value)
    {
        return SelectedUpscalerBackend switch
        {
            UpscalerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerModelDir, value, nameof(SelectedExternalUpscalerModelDir)),
            UpscalerBackendKind.SupirExternal => SetField(ref _supirUpscalerModelDir, value, nameof(SelectedExternalUpscalerModelDir)),
            _ => false
        };
    }

    private string GetSelectedExternalUpscalerArgumentsTemplate() => SelectedUpscalerBackend switch
    {
        UpscalerBackendKind.StableSrExternal => _stableSrUpscalerArgumentsTemplate,
        UpscalerBackendKind.SupirExternal => _supirUpscalerArgumentsTemplate,
        _ => string.Empty
    };

    private bool SetSelectedExternalUpscalerArgumentsTemplate(string value)
    {
        return SelectedUpscalerBackend switch
        {
            UpscalerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerArgumentsTemplate, value, nameof(SelectedExternalUpscalerArgumentsTemplate)),
            UpscalerBackendKind.SupirExternal => SetField(ref _supirUpscalerArgumentsTemplate, value, nameof(SelectedExternalUpscalerArgumentsTemplate)),
            _ => false
        };
    }

    private string GetSelectedExternalRefinerPath() => SelectedRefinerBackend switch
    {
        RefinerBackendKind.StableSrExternal => _stableSrUpscalerPath,
        RefinerBackendKind.SupirExternal => _supirUpscalerPath,
        _ => string.Empty
    };

    private bool SetSelectedExternalRefinerPath(string value)
    {
        return SelectedRefinerBackend switch
        {
            RefinerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerPath, value, nameof(SelectedExternalRefinerPath)),
            RefinerBackendKind.SupirExternal => SetField(ref _supirUpscalerPath, value, nameof(SelectedExternalRefinerPath)),
            _ => false
        };
    }

    private string GetSelectedExternalRefinerWorkingDirectory() => SelectedRefinerBackend switch
    {
        RefinerBackendKind.StableSrExternal => _stableSrUpscalerWorkingDirectory,
        RefinerBackendKind.SupirExternal => _supirUpscalerWorkingDirectory,
        _ => string.Empty
    };

    private bool SetSelectedExternalRefinerWorkingDirectory(string value)
    {
        return SelectedRefinerBackend switch
        {
            RefinerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerWorkingDirectory, value, nameof(SelectedExternalRefinerWorkingDirectory)),
            RefinerBackendKind.SupirExternal => SetField(ref _supirUpscalerWorkingDirectory, value, nameof(SelectedExternalRefinerWorkingDirectory)),
            _ => false
        };
    }

    private string GetSelectedExternalRefinerModelDir() => SelectedRefinerBackend switch
    {
        RefinerBackendKind.StableSrExternal => _stableSrUpscalerModelDir,
        RefinerBackendKind.SupirExternal => _supirUpscalerModelDir,
        _ => string.Empty
    };

    private bool SetSelectedExternalRefinerModelDir(string value)
    {
        return SelectedRefinerBackend switch
        {
            RefinerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerModelDir, value, nameof(SelectedExternalRefinerModelDir)),
            RefinerBackendKind.SupirExternal => SetField(ref _supirUpscalerModelDir, value, nameof(SelectedExternalRefinerModelDir)),
            _ => false
        };
    }

    private string GetSelectedExternalRefinerArgumentsTemplate() => SelectedRefinerBackend switch
    {
        RefinerBackendKind.StableSrExternal => _stableSrUpscalerArgumentsTemplate,
        RefinerBackendKind.SupirExternal => _supirUpscalerArgumentsTemplate,
        _ => string.Empty
    };

    private bool SetSelectedExternalRefinerArgumentsTemplate(string value)
    {
        return SelectedRefinerBackend switch
        {
            RefinerBackendKind.StableSrExternal => SetField(ref _stableSrUpscalerArgumentsTemplate, value, nameof(SelectedExternalRefinerArgumentsTemplate)),
            RefinerBackendKind.SupirExternal => SetField(ref _supirUpscalerArgumentsTemplate, value, nameof(SelectedExternalRefinerArgumentsTemplate)),
            _ => false
        };
    }

    private string ResolveToolPath(string fileName, string fallback)
        => FindFile(fileName, fallback, _configuredFfmpegDirectory);

    private string? GetResolvedFfmpegDirectory()
    {
        var path = ResolveToolPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe");
        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }

    private static string FindFile(string fileName, string fallback, string? preferredDirectory = null)
    {
        var pathCandidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.Combine(path, fileName));

        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(preferredDirectory) ? string.Empty : Path.Combine(preferredDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg", fileName),
            fallback
        }.Concat(pathCandidates);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return string.IsNullOrWhiteSpace(preferredDirectory) ? fileName : Path.Combine(preferredDirectory, fileName);
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
        if (!_persistUserState)
        {
            return fallback;
        }

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
        if (!_persistUserState)
        {
            return;
        }

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

            if (!string.IsNullOrWhiteSpace(loaded.FfmpegDirectory))
            {
                _configuredFfmpegDirectory = loaded.FfmpegDirectory;
            }
            OnPropertyChanged(nameof(FfmpegDirectoryDisplay));

            if (!string.IsNullOrWhiteSpace(loaded.RootFolder) && (Directory.Exists(loaded.RootFolder) || File.Exists(loaded.RootFolder)))
            {
                RootFolder = loaded.RootFolder;
            }

            var mode = Enum.TryParse(loaded.SelectedRenderMode, true, out RenderContentMode parsedMode)
                ? parsedMode
                : RenderContentMode.Video;
            var target = SupportedTargetValues.Contains(loaded.SelectedTarget, StringComparer.OrdinalIgnoreCase)
                ? SupportedTargetValues.First(option => string.Equals(option, loaded.SelectedTarget, StringComparison.OrdinalIgnoreCase))
                : DefaultTargetValue;
            var imageTarget = SupportedImageTargetValues.Contains(loaded.SelectedImageTarget, StringComparer.OrdinalIgnoreCase)
                ? SupportedImageTargetValues.First(option => string.Equals(option, loaded.SelectedImageTarget, StringComparison.OrdinalIgnoreCase))
                : DefaultTargetValue;
            var codec = loaded.SelectedCodec == "x265" ? "x265" : "x264";
            const string container = "mkv";

            SelectedRenderMode = mode;
            SelectedTarget = target;
            SelectedImageTarget = imageTarget;
            SelectedImageOutputFormat = NormalizeImageOutputFormat(loaded.SelectedImageOutputFormat);
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

            RebuildGpuOptions(loaded.SelectedGpuOptionKey);

            EnableStableSr = loaded.EnableStableSr;
            EnableSupir = loaded.EnableSupir;
            var loadedUpscalerBackend = NormalizeUpscalerBackendKind(loaded.SelectedUpscalerBackend);
            if ((loadedUpscalerBackend == UpscalerBackendKind.StableSrExternal && !EnableStableSr) ||
                (loadedUpscalerBackend == UpscalerBackendKind.SupirExternal && !EnableSupir))
            {
                loadedUpscalerBackend = UpscalerBackendKind.RealEsrgan;
            }

            SelectedUpscalerBackend = loadedUpscalerBackend;
            var loadedRefinerBackend = NormalizeRefinerBackendKind(loaded.SelectedRefinerBackend);
            if ((loadedRefinerBackend == RefinerBackendKind.StableSrExternal && !EnableStableSr) ||
                (loadedRefinerBackend == RefinerBackendKind.SupirExternal && !EnableSupir))
            {
                loadedRefinerBackend = RefinerBackendKind.None;
            }

            SelectedRefinerBackend = loadedRefinerBackend;

            if (!string.IsNullOrWhiteSpace(loaded.StableSrUpscalerPath))
            {
                _stableSrUpscalerPath = loaded.StableSrUpscalerPath;
            }

            if (!string.IsNullOrWhiteSpace(loaded.StableSrUpscalerWorkingDirectory))
            {
                _stableSrUpscalerWorkingDirectory = loaded.StableSrUpscalerWorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(loaded.StableSrUpscalerModelDir))
            {
                _stableSrUpscalerModelDir = loaded.StableSrUpscalerModelDir;
            }

            if (!string.IsNullOrWhiteSpace(loaded.StableSrUpscalerArgumentsTemplate))
            {
                _stableSrUpscalerArgumentsTemplate = loaded.StableSrUpscalerArgumentsTemplate;
            }

            if (!string.IsNullOrWhiteSpace(loaded.SupirUpscalerPath))
            {
                _supirUpscalerPath = loaded.SupirUpscalerPath;
            }

            if (!string.IsNullOrWhiteSpace(loaded.SupirUpscalerWorkingDirectory))
            {
                _supirUpscalerWorkingDirectory = loaded.SupirUpscalerWorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(loaded.SupirUpscalerModelDir))
            {
                _supirUpscalerModelDir = loaded.SupirUpscalerModelDir;
            }

            if (!string.IsNullOrWhiteSpace(loaded.SupirUpscalerArgumentsTemplate))
            {
                _supirUpscalerArgumentsTemplate = loaded.SupirUpscalerArgumentsTemplate;
            }

            Overwrite = loaded.Overwrite;

            if (!string.IsNullOrWhiteSpace(loaded.CurrentLanguage) && Enum.TryParse(loaded.CurrentLanguage, true, out UiLanguage parsedLanguage))
            {
                CurrentLanguage = parsedLanguage;
            }

            AppThemeManager.ApplyBackgroundColor(loaded.BackgroundColorHex);

            PreserveIncompleteOutput = loaded.PreserveIncompleteOutput;
            RepairBrokenTimestamps = loaded.RepairBrokenTimestamps ?? true;
            ShowQueuePaths = loaded.ShowQueuePaths;
            _startupBenchmarkPromptShown = loaded.StartupBenchmarkPromptShown;
            _startupBenchmarkCompleted = loaded.StartupBenchmarkCompleted;
            _benchmarkedHardwareSignature = loaded.BenchmarkedHardwareSignature ?? string.Empty;
            if (loaded.StartupBenchmarkMinimumRamGb is > 0
                && loaded.StartupBenchmarkComfortableRamGb is > 0
                && loaded.StartupBenchmarkPeakRamGb is > 0
                && loaded.StartupBenchmarkMinimumVramGb is > 0
                && loaded.StartupBenchmarkComfortableVramGb is > 0
                && loaded.StartupBenchmarkPeakVramGb is > 0)
            {
                _startupBenchmarkPracticalRequirements = new StartupBenchmarkPracticalRequirements(
                    loaded.StartupBenchmarkMinimumRamGb.Value,
                    loaded.StartupBenchmarkComfortableRamGb.Value,
                    loaded.StartupBenchmarkMinimumVramGb.Value,
                    loaded.StartupBenchmarkComfortableVramGb.Value,
                    loaded.StartupBenchmarkPeakRamGb.Value,
                    loaded.StartupBenchmarkPeakVramGb.Value);
            }
            EvaluateStartupBenchmarkPromptState();

            RestorePersistedQueueItems(loaded.QueueItems, loaded.SelectedItemSourcePath);
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
            await Task.Yield();
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(LocalizedStrings.LogRootNotFound(filePath), filePath);
            }

            if (IsImagesMode ? !IsLikelyImageFile(filePath) : !IsLikelyVideoFile(filePath))
            {
                Log(LocalizedStrings.LogNoItemsFound);
                return;
            }

            if (Items.Any(existing => string.Equals(existing.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                UpdateQueueSummary();
                UpdateActionStates();
                Log(LocalizedStrings.LogFoundVideoFiles(0));
                return;
            }

            var outputPath = BuildOutputPath(filePath);

            var item = new QueueItemViewModel
            {
                Index = Items.Count + 1,
                Title = Path.GetFileName(filePath),
                SourcePath = filePath,
                OutputPath = outputPath
            };
            AttachQueueItem(item);
            Items.Add(item);

            ReindexItems();
            UpdateQueueSummary();
            UpdateActionStates();
            Log(LocalizedStrings.LogFoundVideoFiles(1));
            SelectedItem = item;
            ClearRenderPreviewPaths();
            UpdateSelectionDetails();
            OnQueueStateChanged();
            PersistAppSettings();
        }
        catch (OperationCanceledException)
        {
            Log(LocalizedStrings.LogCancelled);
        }
        catch (Exception ex)
        {
            Log(LocalizedStrings.LogScanFailed(ex.Message));
            PostToUi(() => ShowPopupMessage(LocalizedStrings.AppTitle, ex.Message));
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void LoadRecentRootFolders()
    {
        if (!_persistUserState)
        {
            return;
        }

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
        if (!_persistUserState)
        {
            return;
        }

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
        if (_suppressAppSettingsPersistence || !_persistUserState)
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
                _configuredFfmpegDirectory,
                SelectedRenderMode.ToString(),
                SelectedCodec,
                SelectedTarget,
                SelectedImageTarget,
                SelectedImageOutputFormat,
                SelectedContainer,
                EncoderPreset,
                FfmpegThreadsText,
                UpscalerThreadsText,
                TileSizeText,
                SelectedGpuOption?.Key ?? GpuDeviceOption.AutoKey,
                SelectedUpscalerBackend.ToString(),
                SelectedRefinerBackend.ToString(),
                EnableStableSr,
                EnableSupir,
                _stableSrUpscalerPath,
                _stableSrUpscalerWorkingDirectory,
                _stableSrUpscalerModelDir,
                _stableSrUpscalerArgumentsTemplate,
                _supirUpscalerPath,
                _supirUpscalerWorkingDirectory,
                _supirUpscalerModelDir,
                _supirUpscalerArgumentsTemplate,
                Overwrite,
                CurrentLanguage.ToString(),
                AppThemeManager.CurrentBackgroundColorHex,
                PreserveIncompleteOutput,
                RepairBrokenTimestamps,
                _startupBenchmarkPromptShown,
                _startupBenchmarkCompleted,
                _benchmarkedHardwareSignature,
                _startupBenchmarkPracticalRequirements?.MinimumRamGb,
                _startupBenchmarkPracticalRequirements?.ComfortableRamGb,
                _startupBenchmarkPracticalRequirements?.PeakRamGb,
                _startupBenchmarkPracticalRequirements?.MinimumVramGb,
                _startupBenchmarkPracticalRequirements?.ComfortableVramGb,
                _startupBenchmarkPracticalRequirements?.PeakVramGb,
                ShowQueuePaths,
                BuildPersistedQueueItems(),
                SelectedItem?.SourcePath,
                true);

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomicText(_appSettingsPath, json);
        }
        catch
        {
        }
    }

    private List<PersistedQueueItemCacheEntry> BuildPersistedQueueItems() =>
        Items
            .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath))
            .Select(item => new PersistedQueueItemCacheEntry(
                item.SourcePath,
                item.Title,
                item.OutputPath,
                item.IsChecked,
                SkipInRender: false))
            .ToList();

    private void RestorePersistedQueueItems(IReadOnlyList<PersistedQueueItemCacheEntry>? queueItems, string? selectedItemSourcePath)
    {
        if (queueItems is null || queueItems.Count == 0)
        {
            return;
        }

        var restoredItems = new List<QueueItemViewModel>(queueItems.Count);
        foreach (var entry in queueItems)
        {
            var sourcePath = NormalizeInputPath(entry.SourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(entry.Title)
                ? Path.GetFileName(sourcePath)
                : entry.Title;
            var outputPath = BuildOutputPath(sourcePath);

            var item = new QueueItemViewModel
            {
                Index = restoredItems.Count + 1,
                Title = title,
                SourcePath = sourcePath,
                OutputPath = outputPath
            };
            item.ShowPathInQueue = ShowQueuePaths;
            item.IsChecked = entry.IsChecked;
            item.SkipInRender = false;
            AttachQueueItem(item);
            restoredItems.Add(item);
        }

        if (restoredItems.Count == 0)
        {
            return;
        }

        foreach (var item in restoredItems)
        {
            Items.Add(item);
        }

        ReindexItems();
        ApplyQueuePathVisibilityToItems();
        UpdateQueueSummary();
        UpdateActionStates();
        SelectedItem = restoredItems.FirstOrDefault(item =>
            string.Equals(item.SourcePath, selectedItemSourcePath, StringComparison.OrdinalIgnoreCase)) ?? restoredItems[0];
        UpdateSelectionDetails();
        OnQueueStateChanged();
    }

    private void SaveRecentRootFolders()
    {
        if (!_persistUserState)
        {
            return;
        }

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

    private string GetOutputFolderName()
    {
        return IsImagesMode
            ? $"_images_{SelectedImageTarget}_{SelectedImageOutputFormat}_output"
            : $"_{SelectedCodec}_{SelectedTarget}_output";
    }

    private void RefreshTargetOptions()
    {
        _targetOptions = BuildTargetOptions(IsImagesMode ? _selectedImageTarget : _selectedTarget, include16K: IsImagesMode);
        OnPropertyChanged(nameof(TargetOptions));
    }

    private static int ParseTargetHeight(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return 1080;
        }

        var digits = new string(target.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) && parsed > 0
            ? parsed
            : 1080;
    }

    private static string NormalizeImageOutputFormat(string? value)
    {
        return string.Equals(value, "jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";
    }

    private string GetOutputSuffix()
    {
        return IsImagesMode ? $".{SelectedImageOutputFormat}" : ".mkv";
    }

    private string GetOutputFolderForSource(string sourcePath)
    {
        var baseDirectory = GetInputDirectory(sourcePath);
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            baseDirectory = _repoRoot;
        }

        return Path.Combine(baseDirectory, GetOutputFolderName());
    }

    private string BuildOutputPath(string sourcePath)
    {
        return Path.Combine(GetOutputFolderForSource(sourcePath), Path.GetFileNameWithoutExtension(sourcePath) + GetOutputSuffix());
    }

    private void RefreshComputedOutputState()
    {
        OnPropertyChanged(nameof(ComputedOutputFolderName));
        OnPropertyChanged(nameof(OutputLocationHintText));

        foreach (var item in Items)
        {
            item.OutputPath = BuildOutputPath(item.SourcePath);
        }
    }

    private void RefreshQueueOutputPaths()
    {
        RefreshComputedOutputState();
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

    private static bool ShouldSkipScanCandidate(string filePath, string? outputFolderName)
    {
        var full = Path.GetFullPath(filePath);

        var current = Path.GetDirectoryName(full);
        while (!string.IsNullOrWhiteSpace(current))
        {
            var leaf = Path.GetFileName(current);
            if (leaf.StartsWith("_work_", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(outputFolderName) && leaf.Equals(outputFolderName, StringComparison.OrdinalIgnoreCase)) ||
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

    private static bool IsLikelyImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        switch (extension.ToLowerInvariant())
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".webp":
            case ".bmp":
            case ".tif":
            case ".tiff":
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



