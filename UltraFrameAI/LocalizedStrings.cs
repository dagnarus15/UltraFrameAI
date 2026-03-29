using System.Globalization;
using System.Resources;
using UltraFrameAI.Resources;

namespace UltraFrameAI.Resources;

public enum UiLanguage
{
    English,
    Russian,
    German
}

public static class LocalizedStrings
{
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "language.txt");

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru");
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de");

    private static UiLanguage _currentLanguage;

    static LocalizedStrings()
    {
        _currentLanguage = LoadLanguage();
        Strings.Culture = GetCulture(_currentLanguage);
    }

    public static event EventHandler? LanguageChanged;

    public static UiLanguage CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value)
            {
                return;
            }

            _currentLanguage = value;
            Strings.Culture = GetCulture(value);
            SaveLanguage(value);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string AppTitle => GetText(nameof(AppTitle));
    public static string Subtitle => GetText(nameof(Subtitle));
    public static string Workspace => GetText(nameof(Workspace));
    public static string DropZoneTitle => GetText(nameof(DropZoneTitle));
    public static string DropZoneHelp => GetText(nameof(DropZoneHelp));
    public static string RootFolder => GetText(nameof(RootFolder));
    public static string Browse => GetText(nameof(Browse));
    public static string Cancel => GetText(nameof(Cancel));
    public static string ResetToLastFolder => GetText(nameof(ResetToLastFolder));
    public static string RecentFolders => GetText(nameof(RecentFolders));
    public static string OutputFolder => GetText(nameof(OutputFolder));
    public static string Advanced => GetText(nameof(Advanced));
    public static string QuickStartHint => GetText(nameof(QuickStartHint));
    public static string ScanningFolder => GetText(nameof(ScanningFolder));
    public static string Phase => GetText(nameof(Phase));
    public static string Codec => GetText(nameof(Codec));
    public static string Target => GetText(nameof(Target));
    public static string FFmpegThreads => GetText(nameof(FFmpegThreads));
    public static string UpscalerJobs => GetText(nameof(UpscalerJobs));
    public static string TileSize => GetText(nameof(TileSize));
    public static string OverwriteExistingOutput => GetText(nameof(OverwriteExistingOutput));
    public static string KeepTempFolders => GetText(nameof(KeepTempFolders));
    public static string StartBatch => GetText(nameof(StartBatch));
    public static string OpenOutputFolder => GetText(nameof(OpenOutputFolder));
    public static string DeleteItem => GetText(nameof(DeleteItem));
    public static string DeleteSelected => GetText(nameof(DeleteSelected));
    public static string DeleteAll => GetText(nameof(DeleteAll));
    public static string DeleteAllConfirmTitle => GetText(nameof(DeleteAllConfirmTitle));
    public static string DeleteAllConfirmMessage => GetText(nameof(DeleteAllConfirmMessage));
    public static string CurrentStage => GetText(nameof(CurrentStage));
    public static string Elapsed => GetText(nameof(Elapsed));
    public static string ETA => GetText(nameof(ETA));
    public static string ProgressDetails => GetText(nameof(ProgressDetails));
    public static string StepDuration => GetText(nameof(StepDuration));
    public static string Queue => GetText(nameof(Queue));
    public static string Log => GetText(nameof(Log));
    public static string ScanCurrentFolder => GetText(nameof(ScanCurrentFolder));
    public static string ScanCurrentFile => GetText(nameof(ScanCurrentFile));
    public static string ScanFoundVideos => GetText(nameof(ScanFoundVideos));
    public static string QueueQueued => GetText(nameof(QueueQueued));
    public static string QueueWaiting => GetText(nameof(QueueWaiting));
    public static string Language => GetText(nameof(Language));
    public static string LanguageEnglish => GetText(nameof(LanguageEnglish));
    public static string LanguageRussian => GetText(nameof(LanguageRussian));
    public static string LanguageGerman => GetText(nameof(LanguageGerman));
    public static string QueueIndex => GetText(nameof(QueueIndex));
    public static string QueueItem => GetText(nameof(QueueItem));
    public static string QueueStage => GetText(nameof(QueueStage));
    public static string QueueProgress => GetText(nameof(QueueProgress));
    public static string QueueElapsed => GetText(nameof(QueueElapsed));
    public static string QueueETA => GetText(nameof(QueueETA));
    public static string QueueOutput => GetText(nameof(QueueOutput));
    public static string LogReady => GetText(nameof(LogReady));
    public static string LogIdle => GetText(nameof(LogIdle));
    public static string LogNoItemSelected => GetText(nameof(LogNoItemSelected));
    public static string LogPickFolderHint => GetText(nameof(LogPickFolderHint));
    public static string LogWaitingForInput => GetText(nameof(LogWaitingForInput));
    public static string LogScanningFiles => GetText(nameof(LogScanningFiles));
    public static string LogNoItemsFound => GetText(nameof(LogNoItemsFound));
    public static string LogPreparing => GetText(nameof(LogPreparing));
    public static string LogProcessing => GetText(nameof(LogProcessing));
    public static string LogBatchFinished => GetText(nameof(LogBatchFinished));
    public static string LogCancelled => GetText(nameof(LogCancelled));
    public static string LogExtractingFrames => GetText(nameof(LogExtractingFrames));
    public static string LogUpscalingFrames => GetText(nameof(LogUpscalingFrames));
    public static string LogEncodingVideo => GetText(nameof(LogEncodingVideo));
    public static string LogItemComplete => GetText(nameof(LogItemComplete));
    public static string LogExtractionComplete => GetText(nameof(LogExtractionComplete));
    public static string LogUpscaleComplete => GetText(nameof(LogUpscaleComplete));
    public static string LogEncodeComplete => GetText(nameof(LogEncodeComplete));
    public static string LogBatchComplete => GetText(nameof(LogBatchComplete));
    public static string LogOutputExists => GetText(nameof(LogOutputExists));
    public static string LogSkippingEncode => GetText(nameof(LogSkippingEncode));
    public static string LogOldSrcMovedAside => GetText(nameof(LogOldSrcMovedAside));
    public static string LogOldUpMovedAside => GetText(nameof(LogOldUpMovedAside));
    public static string LogUnableToResolveTimestamps => GetText(nameof(LogUnableToResolveTimestamps));
    public static string LogUnableToBuildTimeline => GetText(nameof(LogUnableToBuildTimeline));
    public static string LogNoFramesExtracted => GetText(nameof(LogNoFramesExtracted));
    public static string LogSelectVideoFolder => GetText(nameof(LogSelectVideoFolder));
    public static string LogSelectOutputFolder => GetText(nameof(LogSelectOutputFolder));
    public static string LogBatchFinishedShort => GetText(nameof(LogBatchFinished));
    public static string LogStartingBatch => GetText(nameof(LogStartingBatch));
    public static string LogStartingItem(string itemTitle) => Format(nameof(LogStartingItem), itemTitle);
    public static string LogFinishedItem(string itemTitle) => Format(nameof(LogFinishedItem), itemTitle);
    public static string LogFoundVideoFiles(int count) => Format(nameof(LogFoundVideoFiles), count);
    public static string LogItemCount(int count) => Format(nameof(LogItemCount), count);
    public static string LogScanFailed(string message) => Format(nameof(LogScanFailed), message);
    public static string LogRootNotFound(string folder) => Format(nameof(LogRootNotFound), folder);
    public static string LogBatchFailed(string message) => Format(nameof(LogBatchFailed), message);
    public static string LogExtractedFrames(int count) => Format(nameof(LogExtractedFrames), count);
    public static string LogUpscaledFrames(int count) => Format(nameof(LogUpscaledFrames), count);
    public static string LogWroteFile(string fileName) => Format(nameof(LogWroteFile), fileName);
    public static string LogItemSummary(int current, int total, string elapsed) => Format(nameof(LogItemSummary), current, total, elapsed);
    public static string ScanFolderProgress(int checkedCount, int totalCount) => Format(nameof(ScanFolderProgress), checkedCount, totalCount);

    public static void SetLanguage(UiLanguage language)
    {
        CurrentLanguage = language;
    }

    public static string Get(string key) => GetText(key);

    public static string Format(string key, params object[] args) => string.Format(GetCulture(CurrentLanguage), GetText(key), args);

    private static string GetText(string key)
    {
        var value = Strings.ResourceManager.GetString(key, GetCulture(CurrentLanguage));
        return value ?? throw new MissingManifestResourceException($"Missing resource: {key}");
    }

    private static CultureInfo GetCulture(UiLanguage language) => language switch
    {
        UiLanguage.Russian => RussianCulture,
        UiLanguage.German => GermanCulture,
        _ => EnglishCulture
    };

    private static UiLanguage LoadLanguage()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var raw = File.ReadAllText(StoragePath).Trim();
                if (Enum.TryParse(raw, true, out UiLanguage language))
                {
                    return language;
                }
            }
        }
        catch
        {
        }

        return UiLanguage.English;
    }

    private static void SaveLanguage(UiLanguage language)
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(StoragePath, language.ToString());
        }
        catch
        {
        }
    }
}
