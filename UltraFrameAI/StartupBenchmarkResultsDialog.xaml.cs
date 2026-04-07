using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using UltraFrameAI.Resources;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UltraFrameAI;

public partial class StartupBenchmarkResultsDialog : Window
{
    public StartupBenchmarkResultsDialog(StartupBenchmarkReport report)
    {
        InitializeComponent();

        var clipName = Path.GetFileName(report.SourcePath);
        SummaryText = LocalizedStrings.StartupBenchmarkResultsSummary(clipName);
        SelectedGpuText = $"{LocalizedStrings.StartupBenchmarkResultsStrongestGpu}: {report.Recommendation.GpuLabel}";
        SelectedThreadsText = $"{LocalizedStrings.StartupBenchmarkResultsThreads}: {report.Recommendation.UpscalerThreads}";
        SelectedPresetText = $"{LocalizedStrings.StartupBenchmarkResultsPreset}: {report.Recommendation.EncoderPreset}";
        SelectedTileText = $"{LocalizedStrings.StartupBenchmarkResultsTile}: {report.Recommendation.TileSize}";
        ThroughputText = $"{LocalizedStrings.StartupBenchmarkResultsThroughput}: ~{report.Recommendation.ThroughputFps:0.0} FPS";
        WarningText = report.Recommendation.IsWeakGpu
            ? LocalizedStrings.StartupBenchmarkResultsWeakWarning
            : LocalizedStrings.StartupBenchmarkResultsOkay;
        WarningBrush = report.Recommendation.IsWeakGpu
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0B35E"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78D09A"));

        var assessment = HardwareAssessmentBuilder.Build(report);
        var weakLines = assessment.Lines
            .Where(line => !string.Equals(line.Verdict, LocalizedStrings.HardwareEnough, StringComparison.Ordinal))
            .ToArray();

        foreach (var line in weakLines)
        {
            WeakAssessmentLines.Add(line);
        }

        if (weakLines.Length > 0)
        {
            WeakAssessmentSummaryText = LocalizedStrings.StartupBenchmarkResultsWeakSummary(
                string.Join(", ", weakLines.Select(line => line.Title)));
        }

        foreach (var entry in report.Results
                     .Where(result => result.Success)
                     .OrderBy(result => result.Elapsed)
                     .Take(5))
        {
            TopCases.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} / {2} / tile {3} - {4:0.##} s",
                entry.Phase,
                entry.GpuLabel,
                $"{entry.UpscalerThreads} / {entry.EncoderPreset}",
                entry.TileSize,
                entry.Elapsed.TotalSeconds));
        }

        DataContext = this;
    }

    public string SummaryText { get; }

    public string SelectedGpuText { get; }

    public string SelectedThreadsText { get; }

    public string SelectedPresetText { get; }

    public string SelectedTileText { get; }

    public string ThroughputText { get; }

    public string WarningText { get; }

    public Brush WarningBrush { get; }

    public bool HasWeakAssessment => WeakAssessmentLines.Count > 0;

    public string WeakAssessmentSummaryText { get; } = string.Empty;

    public ObservableCollection<HardwareAssessmentLine> WeakAssessmentLines { get; } = new();

    public ObservableCollection<string> TopCases { get; } = new();

    public bool ShouldApplyRecommendations { get; private set; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ShouldApplyRecommendations = true;
        DialogResult = true;
        Close();
    }

    private void SkipApply_Click(object sender, RoutedEventArgs e)
    {
        ShouldApplyRecommendations = false;
        DialogResult = true;
        Close();
    }

}
