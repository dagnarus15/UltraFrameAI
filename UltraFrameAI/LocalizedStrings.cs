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
    public static string BrowseFolder => GetText(nameof(BrowseFolder));
    public static string BrowseFile => GetText(nameof(BrowseFile));
    public static string BrowseVideoFileDialogTitle => GetText(nameof(BrowseVideoFileDialogTitle));
    public static string BrowseVideoFilesFilter => GetText(nameof(BrowseVideoFilesFilter));
    public static string Cancel => GetText(nameof(Cancel));
    public static string ResetToLastFolder => GetText(nameof(ResetToLastFolder));
    public static string RecentFolders => GetText(nameof(RecentFolders));
    public static string OutputFolder => GetText(nameof(OutputFolder));
    public static string Advanced => GetText(nameof(Advanced));
    public static string Benchmark => GetText(nameof(Benchmark));
    public static string BenchmarkHint => GetText(nameof(BenchmarkHint));
    public static string BenchmarkWindowTitle => GetText(nameof(BenchmarkWindowTitle));
    public static string BenchmarkChartsWindowTitle => GetText(nameof(BenchmarkChartsWindowTitle));
    public static string BenchmarkChartsHint => GetText(nameof(BenchmarkChartsHint));
    public static string BenchmarkChartsWindowHint => GetText(nameof(BenchmarkChartsWindowHint));
    public static string BenchmarkChartsOpen => GetText(nameof(BenchmarkChartsOpen));
    public static string BenchmarkSourceLabel => GetText(nameof(BenchmarkSourceLabel));
    public static string BenchmarkSourceHint => GetText(nameof(BenchmarkSourceHint));
    public static string BenchmarkSourceFileLabel => GetText(nameof(BenchmarkSourceFileLabel));
    public static string BenchmarkSourceFileHint => GetText(nameof(BenchmarkSourceFileHint));
    public static string BenchmarkOutputLabel => GetText(nameof(BenchmarkOutputLabel));
    public static string BenchmarkOutputHint => GetText(nameof(BenchmarkOutputHint));
    public static string BenchmarkChooseSourceFileFirst => GetText(nameof(BenchmarkChooseSourceFileFirst));
    public static string BenchmarkSampleSeconds => GetText(nameof(BenchmarkSampleSeconds));
    public static string BenchmarkQualityNote => GetText(nameof(BenchmarkQualityNote));
    public static string BenchmarkWinnerTitle => GetText(nameof(BenchmarkWinnerTitle));
    public static string BenchmarkFastestOverallLabel => GetText(nameof(BenchmarkFastestOverallLabel));
    public static string BenchmarkWeightedRecommendationLabel => GetText(nameof(BenchmarkWeightedRecommendationLabel));
    public static string BenchmarkBestQualityTimeLabel => GetText(nameof(BenchmarkBestQualityTimeLabel));
    public static string BenchmarkRuntimeMode => GetText(nameof(BenchmarkRuntimeMode));
    public static string BenchmarkQualityTimeMode => GetText(nameof(BenchmarkQualityTimeMode));
    public static string BenchmarkCurrentStep => GetText(nameof(BenchmarkCurrentStep));
    public static string BenchmarkCurrentCase => GetText(nameof(BenchmarkCurrentCase));
    public static string BenchmarkScatterTitle => GetText(nameof(BenchmarkScatterTitle));
    public static string BenchmarkHistogramTitle => GetText(nameof(BenchmarkHistogramTitle));
    public static string BenchmarkSortLabel => GetText(nameof(BenchmarkSortLabel));
    public static string BenchmarkSortMetric => GetText(nameof(BenchmarkSortMetric));
    public static string BenchmarkSortName => GetText(nameof(BenchmarkSortName));
    public static string BenchmarkLegendCodecPreset => GetText(nameof(BenchmarkLegendCodecPreset));
    public static string BenchmarkLegendAntiFlicker => GetText(nameof(BenchmarkLegendAntiFlicker));
    public static string BenchmarkLegendThreads => GetText(nameof(BenchmarkLegendThreads));
    public static string BenchmarkLegendTileSize => GetText(nameof(BenchmarkLegendTileSize));
    public static string BenchmarkLegendFastest => GetText(nameof(BenchmarkLegendFastest));
    public static string BenchmarkLegendBestQualityTime => GetText(nameof(BenchmarkLegendBestQualityTime));
    public static string LastRunSummaryTitle => GetText(nameof(LastRunSummaryTitle));
    public static string ProcessedFiles => GetText(nameof(ProcessedFiles));
    public static string BenchmarkLogSource => GetText(nameof(BenchmarkLogSource));
    public static string BenchmarkLogSample => GetText(nameof(BenchmarkLogSample));
    public static string BenchmarkLogSampleStart => GetText(nameof(BenchmarkLogSampleStart));
    public static string BenchmarkLogSampleDuration => GetText(nameof(BenchmarkLogSampleDuration));
    public static string BenchmarkLogGroupSection => GetText(nameof(BenchmarkLogGroupSection));
    public static string BenchmarkLogSummary => GetText(nameof(BenchmarkLogSummary));
    public static string BenchmarkLogFastestOverall => GetText(nameof(BenchmarkLogFastestOverall));
    public static string BenchmarkLogBestCodecPreset => GetText(nameof(BenchmarkLogBestCodecPreset));
    public static string BenchmarkLogBestAntiFlicker => GetText(nameof(BenchmarkLogBestAntiFlicker));
    public static string BenchmarkLogBestUpscalerThreads => GetText(nameof(BenchmarkLogBestUpscalerThreads));
    public static string BenchmarkLogBestTileSize => GetText(nameof(BenchmarkLogBestTileSize));
    public static string BenchmarkLogBestSettings => GetText(nameof(BenchmarkLogBestSettings));
    public static string BenchmarkLogRecommendedFastPreset => GetText(nameof(BenchmarkLogRecommendedFastPreset));
    public static string BenchmarkLogMetricsSummary => GetText(nameof(BenchmarkLogMetricsSummary));
    public static string BenchmarkSummaryRecommendedFastPreset => GetText(nameof(BenchmarkSummaryRecommendedFastPreset));
    public static string BenchmarkSummaryBestSettings => GetText(nameof(BenchmarkSummaryBestSettings));
    public static string BenchmarkSummaryMetricsSummary => GetText(nameof(BenchmarkSummaryMetricsSummary));
    public static string BenchmarkReportTableGroup => GetText(nameof(BenchmarkReportTableGroup));
    public static string BenchmarkReportTableCase => GetText(nameof(BenchmarkReportTableCase));
    public static string BenchmarkReportTableCodec => GetText(nameof(BenchmarkReportTableCodec));
    public static string BenchmarkReportTablePreset => GetText(nameof(BenchmarkReportTablePreset));
    public static string BenchmarkReportTableAf => GetText(nameof(BenchmarkReportTableAf));
    public static string BenchmarkReportTableMode => GetText(nameof(BenchmarkReportTableMode));
    public static string BenchmarkReportTableStrength => GetText(nameof(BenchmarkReportTableStrength));
    public static string BenchmarkReportTableThreads => GetText(nameof(BenchmarkReportTableThreads));
    public static string BenchmarkReportTableTile => GetText(nameof(BenchmarkReportTableTile));
    public static string BenchmarkReportTableTime => GetText(nameof(BenchmarkReportTableTime));
    public static string BenchmarkReportTableOutputMb => GetText(nameof(BenchmarkReportTableOutputMb));
    public static string BenchmarkReportTableCpu => GetText(nameof(BenchmarkReportTableCpu));
    public static string BenchmarkReportTableRamGb => GetText(nameof(BenchmarkReportTableRamGb));
    public static string BenchmarkReportTableGpu => GetText(nameof(BenchmarkReportTableGpu));
    public static string BenchmarkReportTableVramGb => GetText(nameof(BenchmarkReportTableVramGb));
    public static string BenchmarkCaseCodecPreset => GetText(nameof(BenchmarkCaseCodecPreset));
    public static string BenchmarkCaseAntiFlickerOff => GetText(nameof(BenchmarkCaseAntiFlickerOff));
    public static string BenchmarkCaseAntiFlickerLuma => GetText(nameof(BenchmarkCaseAntiFlickerLuma));
    public static string BenchmarkCaseAntiFlickerFlow => GetText(nameof(BenchmarkCaseAntiFlickerFlow));
    public static string BenchmarkVideoFilesFilter => GetText(nameof(BenchmarkVideoFilesFilter));
    public static string BenchmarkErrorRequiresSource => GetText(nameof(BenchmarkErrorRequiresSource));
    public static string BenchmarkErrorNoVideoFilesFound => GetText(nameof(BenchmarkErrorNoVideoFilesFound));
    public static string BenchmarkErrorSourceFileNotFound => GetText(nameof(BenchmarkErrorSourceFileNotFound));
    public static string BenchmarkErrorUnableToCreateSampleClipWithExitCode => GetText(nameof(BenchmarkErrorUnableToCreateSampleClipWithExitCode));
    public static string BenchmarkErrorUnableToCreateSampleClip => GetText(nameof(BenchmarkErrorUnableToCreateSampleClip));
    public static string BenchmarkErrorPipelineFailed => GetText(nameof(BenchmarkErrorPipelineFailed));
    public static string BenchmarkShortCodecFast => GetText(nameof(BenchmarkShortCodecFast));
    public static string BenchmarkSuccess => GetText(nameof(BenchmarkSuccess));
    public static string BenchmarkFailure => GetText(nameof(BenchmarkFailure));
    public static string BenchmarkNotAvailable => GetText(nameof(BenchmarkNotAvailable));
    public static string BenchmarkStatusStarting => GetText(nameof(BenchmarkStatusStarting));
    public static string BenchmarkStatusStarted => GetText(nameof(BenchmarkStatusStarted));
    public static string BenchmarkStatusFinished => GetText(nameof(BenchmarkStatusFinished));
    public static string BenchmarkStatusCancelled => GetText(nameof(BenchmarkStatusCancelled));
    public static string BenchmarkStatusCancelledUser => GetText(nameof(BenchmarkStatusCancelledUser));
    public static string BenchmarkStatusFailed => GetText(nameof(BenchmarkStatusFailed));
    public static string BenchmarkStatusStartingDetailed => GetText(nameof(BenchmarkStatusStartingDetailed));
    public static string BenchmarkCaseStarted => GetText(nameof(BenchmarkCaseStarted));
    public static string BenchmarkCaseCompleted => GetText(nameof(BenchmarkCaseCompleted));
    public static string BenchmarkSummaryHeader => GetText(nameof(BenchmarkSummaryHeader));
    public static string BenchmarkSummaryFastestOverall => GetText(nameof(BenchmarkSummaryFastestOverall));
    public static string BenchmarkSummaryBestCodecPreset => GetText(nameof(BenchmarkSummaryBestCodecPreset));
    public static string BenchmarkSummaryBestAntiFlicker => GetText(nameof(BenchmarkSummaryBestAntiFlicker));
    public static string BenchmarkSummaryBestUpscalerThreads => GetText(nameof(BenchmarkSummaryBestUpscalerThreads));
    public static string BenchmarkSummaryBestTileSize => GetText(nameof(BenchmarkSummaryBestTileSize));
    public static string BenchmarkShortCodecMed => GetText(nameof(BenchmarkShortCodecMed));
    public static string BenchmarkShortCodecSlow => GetText(nameof(BenchmarkShortCodecSlow));
    public static string BenchmarkShortCodecVFast => GetText(nameof(BenchmarkShortCodecVFast));
    public static string BenchmarkShortAFOff => GetText(nameof(BenchmarkShortAFOff));
    public static string BenchmarkShortAFOn => GetText(nameof(BenchmarkShortAFOn));
    public static string BenchmarkShortAFAnime => GetText(nameof(BenchmarkShortAFAnime));
    public static string BenchmarkShortAFUltra => GetText(nameof(BenchmarkShortAFUltra));
    public static string BenchmarkShortAFVideo => GetText(nameof(BenchmarkShortAFVideo));
    public static string BenchmarkShortAFFaces => GetText(nameof(BenchmarkShortAFFaces));
    public static string BenchmarkShortThreadsPrefix => GetText(nameof(BenchmarkShortThreadsPrefix));
    public static string BenchmarkShortTilePrefix => GetText(nameof(BenchmarkShortTilePrefix));
    public static string BenchmarkNoResults => GetText(nameof(BenchmarkNoResults));
    public static string BenchmarkChartTimeAxis => GetText(nameof(BenchmarkChartTimeAxis));
    public static string BenchmarkChartQualityAxis => GetText(nameof(BenchmarkChartQualityAxis));
    public static string BenchmarkStart => GetText(nameof(BenchmarkStart));
    public static string BenchmarkStop => GetText(nameof(BenchmarkStop));
    public static string AntiFlicker => GetText(nameof(AntiFlicker));
    public static string AntiFlickerHint => GetText(nameof(AntiFlickerHint));
    public static string AntiFlickerMode => GetText(nameof(AntiFlickerMode));
    public static string AntiFlickerModeHint => GetText(nameof(AntiFlickerModeHint));
    public static string AntiFlickerModeHelpTitle => GetText(nameof(AntiFlickerModeHelpTitle));
    public static string AntiFlickerModeHelpBody => GetText(nameof(AntiFlickerModeHelpBody));
    public static string AntiFlickerModeLumaStabilizer => GetText(nameof(AntiFlickerModeLumaStabilizer));
    public static string AntiFlickerModeFlowGuided => GetText(nameof(AntiFlickerModeFlowGuided));
    public static string AntiFlickerStrength => GetText(nameof(AntiFlickerStrength));
    public static string AntiFlickerPreset => GetText(nameof(AntiFlickerPreset));
    public static string AntiFlickerPresetHint => GetText(nameof(AntiFlickerPresetHint));
    public static string AntiFlickerAnimeHint => GetText(nameof(AntiFlickerAnimeHint));
    public static string AntiFlickerVideoHint => GetText(nameof(AntiFlickerVideoHint));
    public static string AntiFlickerFacesHint => GetText(nameof(AntiFlickerFacesHint));
    public static string AntiFlickerUltra => GetText(nameof(AntiFlickerUltra));
    public static string AntiFlickerUltraHint => GetText(nameof(AntiFlickerUltraHint));
    public static string ContentMode => GetText(nameof(ContentMode));
    public static string ContentModeAnime => GetText(nameof(ContentModeAnime));
    public static string ContentModeAnimeUltra => GetText(nameof(ContentModeAnimeUltra));
    public static string ContentModeVideo => GetText(nameof(ContentModeVideo));
    public static string ContentModeFaces => GetText(nameof(ContentModeFaces));
    public static string QuickStartHint => GetText(nameof(QuickStartHint));
    public static string ScanningFolder => GetText(nameof(ScanningFolder));
    public static string Codec => GetText(nameof(Codec));
    public static string Target => GetText(nameof(Target));
    public static string Resolution => GetText(nameof(Resolution));
    public static string CustomTargetValue => GetText(nameof(CustomTargetValue));
    public static string CustomTargetDialogTitle => GetText(nameof(CustomTargetDialogTitle));
    public static string CustomTargetDialogPrompt => GetText(nameof(CustomTargetDialogPrompt));
    public static string CustomTargetDialogValidation => GetText(nameof(CustomTargetDialogValidation));
    public static string Container => GetText(nameof(Container));
    public static string FFmpegThreads => GetText(nameof(FFmpegThreads));
    public static string UpscalerJobs => GetText(nameof(UpscalerJobs));
    public static string TileSize => GetText(nameof(TileSize));
    public static string GpuSelection => GetText(nameof(GpuSelection));
    public static string GpuSelectionHint => GetText(nameof(GpuSelectionHint));
    public static string GpuSelectionAuto => GetText(nameof(GpuSelectionAuto));
    public static string ContainerHelpTitle => GetText(nameof(ContainerHelpTitle));
    public static string ContainerHelpBody => GetText(nameof(ContainerHelpBody));
    public static string SettingsOverviewHelpTitle => GetText(nameof(SettingsOverviewHelpTitle));
    public static string SettingsOverviewHelpBody => GetText(nameof(SettingsOverviewHelpBody));
    public static string OverwriteExistingOutputHelpTitle => GetText(nameof(OverwriteExistingOutputHelpTitle));
    public static string OverwriteExistingOutputHelpBody => GetText(nameof(OverwriteExistingOutputHelpBody));
    public static string PreserveIncompleteOutputHelpTitle => GetText(nameof(PreserveIncompleteOutputHelpTitle));
    public static string PreserveIncompleteOutputHelpBody => GetText(nameof(PreserveIncompleteOutputHelpBody));
    public static string FfmpegThreadsHelpTitle => GetText(nameof(FfmpegThreadsHelpTitle));
    public static string FfmpegThreadsHelpBody => GetText(nameof(FfmpegThreadsHelpBody));
    public static string UpscalerJobsHelpTitle => GetText(nameof(UpscalerJobsHelpTitle));
    public static string UpscalerJobsHelpBody => GetText(nameof(UpscalerJobsHelpBody));
    public static string TileSizeHelpTitle => GetText(nameof(TileSizeHelpTitle));
    public static string TileSizeHelpBody => GetText(nameof(TileSizeHelpBody));
    public static string StartupBenchmarkPromptTitle => GetText(nameof(StartupBenchmarkPromptTitle));
    public static string StartupBenchmarkPromptGreeting => GetText(nameof(StartupBenchmarkPromptGreeting));
    public static string StartupBenchmarkPromptThanks => GetText(nameof(StartupBenchmarkPromptThanks));
    public static string StartupBenchmarkPromptBody => GetText(nameof(StartupBenchmarkPromptBody));
    public static string StartupBenchmarkNewHardwareTitle => GetText(nameof(StartupBenchmarkNewHardwareTitle));
    public static string StartupBenchmarkNewHardwareGreeting => GetText(nameof(StartupBenchmarkNewHardwareGreeting));
    public static string StartupBenchmarkNewHardwareBody => GetText(nameof(StartupBenchmarkNewHardwareBody));
    public static string StartupBenchmarkPromptRun => GetText(nameof(StartupBenchmarkPromptRun));
    public static string StartupBenchmarkPromptSkip => GetText(nameof(StartupBenchmarkPromptSkip));
    public static string StartupBenchmarkUnavailableTitle => GetText(nameof(StartupBenchmarkUnavailableTitle));
    public static string StartupBenchmarkUnavailableBody => GetText(nameof(StartupBenchmarkUnavailableBody));
    public static string StartupBenchmarkProgressTitle => GetText(nameof(StartupBenchmarkProgressTitle));
    public static string StartupBenchmarkProgressIntro => GetText(nameof(StartupBenchmarkProgressIntro));
    public static string StartupBenchmarkCurrentPhase => GetText(nameof(StartupBenchmarkCurrentPhase));
    public static string StartupBenchmarkCurrentCase => GetText(nameof(StartupBenchmarkCurrentCase));
    public static string StartupBenchmarkCancel => GetText(nameof(StartupBenchmarkCancel));
    public static string StartupBenchmarkResultsTitle => GetText(nameof(StartupBenchmarkResultsTitle));
    public static string StartupBenchmarkResultsApplied => GetText(nameof(StartupBenchmarkResultsApplied));
    public static string StartupBenchmarkResultsApply => GetText(nameof(StartupBenchmarkResultsApply));
    public static string StartupBenchmarkResultsSkipApply => GetText(nameof(StartupBenchmarkResultsSkipApply));
    public static string StartupBenchmarkResultsStrongestGpu => GetText(nameof(StartupBenchmarkResultsStrongestGpu));
    public static string StartupBenchmarkResultsThreads => GetText(nameof(StartupBenchmarkResultsThreads));
    public static string StartupBenchmarkResultsPreset => GetText(nameof(StartupBenchmarkResultsPreset));
    public static string StartupBenchmarkResultsTile => GetText(nameof(StartupBenchmarkResultsTile));
    public static string StartupBenchmarkResultsThroughput => GetText(nameof(StartupBenchmarkResultsThroughput));
    public static string StartupBenchmarkResultsTopCases => GetText(nameof(StartupBenchmarkResultsTopCases));
    public static string StartupBenchmarkResultsRecommendationsTitle => GetText(nameof(StartupBenchmarkResultsRecommendationsTitle));
    public static string StartupBenchmarkResultsWeakWarning => GetText(nameof(StartupBenchmarkResultsWeakWarning));
    public static string StartupBenchmarkResultsOkay => GetText(nameof(StartupBenchmarkResultsOkay));
    public static string GeneralHelpTitle => GetText(nameof(GeneralHelpTitle));
    public static string GeneralHelpBody => GetText(nameof(GeneralHelpBody));
    public static string HelpCenterIntro => GetText(nameof(HelpCenterIntro));
    public static string HelpCenterHowToTab => GetText(nameof(HelpCenterHowToTab));
    public static string HelpCenterFaqTab => GetText(nameof(HelpCenterFaqTab));
    public static string HelpCenterHardwareTab => GetText(nameof(HelpCenterHardwareTab));
    public static string HelpCenterDonateTab => GetText(nameof(HelpCenterDonateTab));
    public static string HelpCenterLinksTab => GetText(nameof(HelpCenterLinksTab));
    public static string HelpCenterHowToTitle => GetText(nameof(HelpCenterHowToTitle));
    public static string HelpCenterHowToBody => GetText(nameof(HelpCenterHowToBody));
    public static string HelpCenterDetectedDevicesTitle => GetText(nameof(HelpCenterDetectedDevicesTitle));
    public static string HelpCenterLinksTitle => GetText(nameof(HelpCenterLinksTitle));
    public static string HelpCenterLinksContacts => GetText(nameof(HelpCenterLinksContacts));
    public static string HelpCenterLinksSources => GetText(nameof(HelpCenterLinksSources));
    public static string DonationDialogTitle => GetText(nameof(DonationDialogTitle));
    public static string DonationDialogThanks => GetText(nameof(DonationDialogThanks));
    public static string DonationDialogBody => GetText(nameof(DonationDialogBody));
    public static string RenderSessionResultsTitle => GetText(nameof(RenderSessionResultsTitle));
    public static string RenderSessionResultsDonatePrefix => GetText(nameof(RenderSessionResultsDonatePrefix));
    public static string RenderSessionResultsDonateLink => GetText(nameof(RenderSessionResultsDonateLink));
    public static string RenderSessionResultsDonateSuffix => GetText(nameof(RenderSessionResultsDonateSuffix));
    public static string HelpFaqQuestion1 => GetText(nameof(HelpFaqQuestion1));
    public static string HelpFaqAnswer1 => GetText(nameof(HelpFaqAnswer1));
    public static string HelpFaqQuestion2 => GetText(nameof(HelpFaqQuestion2));
    public static string HelpFaqAnswer2 => GetText(nameof(HelpFaqAnswer2));
    public static string HelpFaqQuestion3 => GetText(nameof(HelpFaqQuestion3));
    public static string HelpFaqAnswer3 => GetText(nameof(HelpFaqAnswer3));
    public static string HelpFaqQuestion4 => GetText(nameof(HelpFaqQuestion4));
    public static string HelpFaqAnswer4 => GetText(nameof(HelpFaqAnswer4));
    public static string HelpFaqQuestion5 => GetText(nameof(HelpFaqQuestion5));
    public static string HelpFaqAnswer5 => GetText(nameof(HelpFaqAnswer5));
    public static string HardwareAssessmentTitle => GetText(nameof(HardwareAssessmentTitle));
    public static string HardwareGpuTitle => GetText(nameof(HardwareGpuTitle));
    public static string HardwareVramTitle => GetText(nameof(HardwareVramTitle));
    public static string HardwareCpuTitle => GetText(nameof(HardwareCpuTitle));
    public static string HardwareRamTitle => GetText(nameof(HardwareRamTitle));
    public static string HardwareEnough => GetText(nameof(HardwareEnough));
    public static string HardwareWeak => GetText(nameof(HardwareWeak));
    public static string HardwareTooLow => GetText(nameof(HardwareTooLow));
    public static string HardwareGpuMissingDetail => GetText(nameof(HardwareGpuMissingDetail));
    public static string HardwareGpuStaticEnoughDetail => GetText(nameof(HardwareGpuStaticEnoughDetail));
    public static string HardwareGpuStaticLowDetail => GetText(nameof(HardwareGpuStaticLowDetail));
    public static string HardwareGpuEnoughDetail => GetText(nameof(HardwareGpuEnoughDetail));
    public static string HardwareGpuWeakDetail => GetText(nameof(HardwareGpuWeakDetail));
    public static string HardwareGpuMinimumNvidia => GetText(nameof(HardwareGpuMinimumNvidia));
    public static string HardwareGpuComfortableNvidia => GetText(nameof(HardwareGpuComfortableNvidia));
    public static string HardwareGpuMinimumAmd => GetText(nameof(HardwareGpuMinimumAmd));
    public static string HardwareGpuComfortableAmd => GetText(nameof(HardwareGpuComfortableAmd));
    public static string HardwareGpuMinimumIntel => GetText(nameof(HardwareGpuMinimumIntel));
    public static string HardwareGpuComfortableIntel => GetText(nameof(HardwareGpuComfortableIntel));
    public static string HardwareGpuMinimumGeneric => GetText(nameof(HardwareGpuMinimumGeneric));
    public static string HardwareGpuComfortableGeneric => GetText(nameof(HardwareGpuComfortableGeneric));
    public static string HardwareVramMinimumDetail => GetText(nameof(HardwareVramMinimumDetail));
    public static string HardwareVramEnoughDetail => GetText(nameof(HardwareVramEnoughDetail));
    public static string HardwareVramLowDetail => GetText(nameof(HardwareVramLowDetail));
    public static string HardwareCpuMinimumDetail => GetText(nameof(HardwareCpuMinimumDetail));
    public static string HardwareCpuEnoughDetail => GetText(nameof(HardwareCpuEnoughDetail));
    public static string HardwareCpuLowDetail => GetText(nameof(HardwareCpuLowDetail));
    public static string HardwareRamMinimumDetail => GetText(nameof(HardwareRamMinimumDetail));
    public static string HardwareRamEnoughDetail => GetText(nameof(HardwareRamEnoughDetail));
    public static string HardwareRamLowDetail => GetText(nameof(HardwareRamLowDetail));
    public static string UpscalerBackend => GetText(nameof(UpscalerBackend));
    public static string UpscalerBackendHint => GetText(nameof(UpscalerBackendHint));
    public static string UpscalerBackendRealEsrgan => GetText(nameof(UpscalerBackendRealEsrgan));
    public static string UpscalerBackendStableSr => GetText(nameof(UpscalerBackendStableSr));
    public static string UpscalerBackendSupir => GetText(nameof(UpscalerBackendSupir));
    public static string RefinerBackend => GetText(nameof(RefinerBackend));
    public static string RefinerBackendHint => GetText(nameof(RefinerBackendHint));
    public static string RefinerBackendNone => GetText(nameof(RefinerBackendNone));
    public static string RefinerBackendStableSr => GetText(nameof(RefinerBackendStableSr));
    public static string RefinerBackendSupir => GetText(nameof(RefinerBackendSupir));
    public static string EnableStableSr => GetText(nameof(EnableStableSr));
    public static string EnableSupir => GetText(nameof(EnableSupir));
    public static string ExternalUpscalerHint => GetText(nameof(ExternalUpscalerHint));
    public static string ExternalUpscalerStableSrHint => GetText(nameof(ExternalUpscalerStableSrHint));
    public static string ExternalUpscalerSupirHint => GetText(nameof(ExternalUpscalerSupirHint));
    public static string ExternalRefinerHint => GetText(nameof(ExternalRefinerHint));
    public static string ExternalRefinerStableSrHint => GetText(nameof(ExternalRefinerStableSrHint));
    public static string ExternalRefinerSupirHint => GetText(nameof(ExternalRefinerSupirHint));
    public static string ExternalUpscalerPath => GetText(nameof(ExternalUpscalerPath));
    public static string ExternalUpscalerWorkingDirectory => GetText(nameof(ExternalUpscalerWorkingDirectory));
    public static string ExternalUpscalerModelDir => GetText(nameof(ExternalUpscalerModelDir));
    public static string ExternalUpscalerArgumentsTemplate => GetText(nameof(ExternalUpscalerArgumentsTemplate));
    public static string ExternalUpscalerHelpTitle => GetText(nameof(ExternalUpscalerHelpTitle));
    public static string ExternalUpscalerHelpBody => GetText(nameof(ExternalUpscalerHelpBody));
    public static string ExternalUpscalerPathRequired => GetText(nameof(ExternalUpscalerPathRequired));
    public static string ExternalUpscalerArgumentsRequired => GetText(nameof(ExternalUpscalerArgumentsRequired));
    public static string OverwriteExistingOutput => GetText(nameof(OverwriteExistingOutput));
    public static string PreserveIncompleteOutput => GetText(nameof(PreserveIncompleteOutput));
    public static string PreserveIncompleteOutputHint => GetText(nameof(PreserveIncompleteOutputHint));
    public static string StartBatch => GetText(nameof(StartBatch));
    public static string StartSelected => GetText(nameof(StartSelected));
    public static string OutputLocationHint(string folderName) => Format(nameof(OutputLocationHint), folderName);
    public static string OutputFolderNearFileLabel => GetText(nameof(OutputFolderNearFileLabel));
    public static string OpenOutputFolder => GetText(nameof(OpenOutputFolder));
    public static string OpenResultFolder => GetText(nameof(OpenResultFolder));
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
    public static string ProcessingFps => GetText(nameof(ProcessingFps));
    public static string Queue => GetText(nameof(Queue));
    public static string Log => GetText(nameof(Log));
    public static string LogTime => GetText(nameof(LogTime));
    public static string LogMessage => GetText(nameof(LogMessage));
    public static string ScanCurrentFolder => GetText(nameof(ScanCurrentFolder));
    public static string ScanCurrentFile => GetText(nameof(ScanCurrentFile));
    public static string ScanFoundVideos => GetText(nameof(ScanFoundVideos));
    public static string QueueQueued => GetText(nameof(QueueQueued));
    public static string QueueWaiting => GetText(nameof(QueueWaiting));
    public static string Language => GetText(nameof(Language));
    public static string LanguageEnglish => GetText(nameof(LanguageEnglish));
    public static string LanguageRussian => GetText(nameof(LanguageRussian));
    public static string LanguageGerman => GetText(nameof(LanguageGerman));
    public static string Apply => GetText(nameof(Apply));
    public static string Ok => GetText(nameof(Ok));
    public static string BackgroundColor => GetText(nameof(BackgroundColor));
    public static string BackgroundColorDialogTitle => GetText(nameof(BackgroundColorDialogTitle));
    public static string BackgroundColorDialogBody => GetText(nameof(BackgroundColorDialogBody));
    public static string BackgroundColorSpectrum => GetText(nameof(BackgroundColorSpectrum));
    public static string BackgroundColorPresets => GetText(nameof(BackgroundColorPresets));
    public static string BackgroundColorActive => GetText(nameof(BackgroundColorActive));
    public static string BackgroundColorPrevious => GetText(nameof(BackgroundColorPrevious));
    public static string BackgroundColorDefault => GetText(nameof(BackgroundColorDefault));
    public static string BackgroundColorHex => GetText(nameof(BackgroundColorHex));
    public static string SettingsTabGeneral => GetText(nameof(SettingsTabGeneral));
    public static string SettingsTabPerformance => GetText(nameof(SettingsTabPerformance));
    public static string SettingsTabBackends => GetText(nameof(SettingsTabBackends));
    public static string SettingsTabAntiFlicker => GetText(nameof(SettingsTabAntiFlicker));
    public static string SettingsTabAppearance => GetText(nameof(SettingsTabAppearance));
    public static string QueueIndex => GetText(nameof(QueueIndex));
    public static string QueueItem => GetText(nameof(QueueItem));
    public static string QueueStage => GetText(nameof(QueueStage));
    public static string QueueProgress => GetText(nameof(QueueProgress));
    public static string QueueElapsed => GetText(nameof(QueueElapsed));
    public static string QueueETA => GetText(nameof(QueueETA));
    public static string QueueOutput => GetText(nameof(QueueOutput));
    public static string QueueStatus => GetText(nameof(QueueStatus));
    public static string QueueStatusNew => GetText(nameof(QueueStatusNew));
    public static string QueueStatusInterrupted => GetText(nameof(QueueStatusInterrupted));
    public static string QueueStatusCompleted => GetText(nameof(QueueStatusCompleted));
    public static string QueuePath => GetText(nameof(QueuePath));
    public static string QueueShowPaths => GetText(nameof(QueueShowPaths));
    public static string QueueSourceSingle(string path) => Format(nameof(QueueSourceSingle), path);
    public static string QueueSourceMultiple(int count)
    {
        return CurrentLanguage switch
        {
            UiLanguage.Russian => $"Источники: {count} {GetRussianFolderWord(count)}",
            UiLanguage.German => $"Quellen: {count} {GetGermanFolderWord(count)}",
            _ => $"Sources: {count} {GetEnglishFolderWord(count)}"
        };
    }
    public static string LogReady => GetText(nameof(LogReady));
    public static string LogIdle => GetText(nameof(LogIdle));
    public static string LogNoItemSelected => GetText(nameof(LogNoItemSelected));
    public static string LogPickFolderHint => GetText(nameof(LogPickFolderHint));
    public static string LogWaitingForInput => GetText(nameof(LogWaitingForInput));
    public static string LogScanningFiles => GetText(nameof(LogScanningFiles));
    public static string LogNoItemsFound => GetText(nameof(LogNoItemsFound));
    public static string LogPreparing => GetText(nameof(LogPreparing));
    public static string LogPreparingFrames => GetText(nameof(LogPreparingFrames));
    public static string LogSearchingFirstPng => GetText(nameof(LogSearchingFirstPng));
    public static string LogWaitingFirstUpscaledFrame => GetText(nameof(LogWaitingFirstUpscaledFrame));
    public static string LogProbingSource => GetText(nameof(LogProbingSource));
    public static string LogReadingVideoInfo => GetText(nameof(LogReadingVideoInfo));
    public static string LogCheckingCache => GetText(nameof(LogCheckingCache));
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
    public static string LogInvalidVideoInfo => GetText(nameof(LogInvalidVideoInfo));
    public static string LogNoFramesExtracted => GetText(nameof(LogNoFramesExtracted));
    public static string LogSelectVideoFolder => GetText(nameof(LogSelectVideoFolder));
    public static string LogSelectOutputFolder => GetText(nameof(LogSelectOutputFolder));
    public static string CloseDuringRenderTitle => GetText(nameof(CloseDuringRenderTitle));
    public static string CloseDuringRenderMessage => GetText(nameof(CloseDuringRenderMessage));
    public static string CloseDuringRenderStop => GetText(nameof(CloseDuringRenderStop));
    public static string CloseDuringRenderKeep => GetText(nameof(CloseDuringRenderKeep));
    public static string LogBatchFinishedShort => GetText(nameof(LogBatchFinished));
    public static string LogStartingBatch => GetText(nameof(LogStartingBatch));
    public static string LogStartingSelectedBatch => GetText(nameof(LogStartingSelectedBatch));
    public static string LogStartingItem(string itemTitle) => Format(nameof(LogStartingItem), itemTitle);
    public static string LogFinishedItem(string itemTitle) => Format(nameof(LogFinishedItem), itemTitle);
    public static string LogFoundVideoFiles(int count) => Format(nameof(LogFoundVideoFiles), count);
    public static string GpuSelectionAutoWithName(string gpuName) => Format(nameof(GpuSelectionAutoWithName), gpuName);
    public static string StartupBenchmarkResultsSummary(string clipName) => Format(nameof(StartupBenchmarkResultsSummary), clipName);
    public static string StartupBenchmarkResultsWeakSummary(string weakParts) => Format(nameof(StartupBenchmarkResultsWeakSummary), weakParts);
    public static string HardwareDeviceMemoryDetail(double gigabytes) => Format(nameof(HardwareDeviceMemoryDetail), gigabytes.ToString("0.#", CultureInfo.InvariantCulture));
    public static string HardwareDeviceMemoryUnknown => GetText(nameof(HardwareDeviceMemoryUnknown));
    public static string HardwareNoDevicesFound => GetText(nameof(HardwareNoDevicesFound));
    public static string HardwareNoDevicesFoundDetail => GetText(nameof(HardwareNoDevicesFoundDetail));
    public static string RenderSessionResultsSummary(string elapsed) => Format(nameof(RenderSessionResultsSummary), elapsed);
    public static string RenderSessionResultsItemComplete(string elapsed) => Format(nameof(RenderSessionResultsItemComplete), elapsed);
    public static string RenderSessionResultsItemFps(string fps) => Format(nameof(RenderSessionResultsItemFps), fps);
    public static string LogItemCount(int count)
    {
        return CurrentLanguage switch
        {
            UiLanguage.Russian => $"{count} {GetRussianFileWord(count)}",
            UiLanguage.German => $"{count} {GetGermanFileWord(count)}",
            _ => $"{count} {GetEnglishFileWord(count)}"
        };
    }
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

    private static string GetEnglishFileWord(int count) => count == 1 ? "file" : "files";

    private static string GetGermanFileWord(int count) => count == 1 ? "Datei" : "Dateien";

    private static string GetEnglishFolderWord(int count) => count == 1 ? "folder" : "folders";

    private static string GetGermanFolderWord(int count) => count == 1 ? "Ordner" : "Ordner";

    private static string GetRussianFileWord(int count)
    {
        var absoluteCount = Math.Abs(count);
        var lastTwoDigits = absoluteCount % 100;
        if (lastTwoDigits >= 11 && lastTwoDigits <= 14)
        {
            return "файлов";
        }

        return (absoluteCount % 10) switch
        {
            1 => "файл",
            2 or 3 or 4 => "файла",
            _ => "файлов"
        };
    }

    private static string GetRussianFolderWord(int count)
    {
        var absoluteCount = Math.Abs(count);
        var lastTwoDigits = absoluteCount % 100;
        if (lastTwoDigits >= 11 && lastTwoDigits <= 14)
        {
            return "папок";
        }

        return (absoluteCount % 10) switch
        {
            1 => "папка",
            2 or 3 or 4 => "папки",
            _ => "папок"
        };
    }

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

            var tempPath = StoragePath + ".tmp";
            File.WriteAllText(tempPath, language.ToString());
            File.Move(tempPath, StoragePath, true);
        }
        catch
        {
        }
    }
}

