using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using UltraFrameAI.Resources;

using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using IoPath = System.IO.Path;

namespace UltraFrameAI;

public partial class BenchmarkChartsWindow : Window, INotifyPropertyChanged
{
    private static readonly string PlacementPath = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UltraFrameAI",
        "benchmark-charts-window.json");

    private readonly ObservableCollection<BenchmarkCaseResult> _results;
    private readonly ObservableCollection<BenchmarkWindow.BenchmarkLegendItem> _legendItems = new();
    private readonly ObservableCollection<BenchmarkWindow.BenchmarkLegendItem> _rankingLegendItems = new();
    private readonly Dictionary<string, WpfBrush> _groupBrushes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codec/Preset"] = BrushFromHex("#4DA3FF"),
        ["Anti-flicker"] = BrushFromHex("#B48CFF"),
        ["Upscaler threads"] = BrushFromHex("#4CD18C"),
        ["Tile size"] = BrushFromHex("#F5B14C"),
    };

    private string _bestOverallText = LocalizedStrings.BenchmarkNotAvailable;
    private string _bestQualityTimeText = LocalizedStrings.BenchmarkNotAvailable;
    private BenchmarkChartMode _chartMode = BenchmarkChartMode.Runtime;
    private BenchmarkRankingSortMode _rankingSortMode = BenchmarkRankingSortMode.Metric;

    public BenchmarkChartsWindow(ObservableCollection<BenchmarkCaseResult> results)
    {
        InitializeComponent();
        DataContext = this;
        _results = results;
        _results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            RefreshWinnerSummary();
            RenderCharts();
        };
        _legendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendCodecPreset, _groupBrushes["Codec/Preset"]));
        _legendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendAntiFlicker, _groupBrushes["Anti-flicker"]));
        _legendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendThreads, _groupBrushes["Upscaler threads"]));
        _legendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendTileSize, _groupBrushes["Tile size"]));
        _rankingLegendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendFastest, WpfBrushes.WhiteSmoke));
        _rankingLegendItems.Add(new BenchmarkWindow.BenchmarkLegendItem(LocalizedStrings.BenchmarkLegendBestQualityTime, WpfBrushes.Gold));
        RefreshWinnerSummary();
        SizeChanged += (_, _) => ScheduleRenderCharts();
        Loaded += (_, _) =>
        {
            RestorePlacement();
            ScheduleRenderCharts();
        };
        LocationChanged += (_, _) => SavePlacement();
        StateChanged += (_, _) => SavePlacement();
        Closing += (_, _) => SavePlacement();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BenchmarkCaseResult> Results => _results;

    public ObservableCollection<BenchmarkWindow.BenchmarkLegendItem> LegendItems => _legendItems;

    public ObservableCollection<BenchmarkWindow.BenchmarkLegendItem> RankingLegendItems => _rankingLegendItems;

    public bool HasResults => _results.Any(result => result.Success);

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

    public BenchmarkChartMode ChartMode
    {
        get => _chartMode;
        set
        {
            if (SetField(ref _chartMode, value))
            {
                OnPropertyChanged(nameof(IsRuntimeChart));
                OnPropertyChanged(nameof(IsQualityTimeChart));
                ScheduleRenderCharts();
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
                ScheduleRenderCharts();
            }
        }
    }

    public bool IsMetricRanking => RankingSortMode == BenchmarkRankingSortMode.Metric;

    public bool IsNameRanking => RankingSortMode == BenchmarkRankingSortMode.Name;

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

    private void SetRuntimeChart_Click(object sender, RoutedEventArgs e) => ChartMode = BenchmarkChartMode.Runtime;

    private void SetQualityChart_Click(object sender, RoutedEventArgs e) => ChartMode = BenchmarkChartMode.QualityTime;

    private void SetMetricRanking_Click(object sender, RoutedEventArgs e) => RankingSortMode = BenchmarkRankingSortMode.Metric;

    private void SetNameRanking_Click(object sender, RoutedEventArgs e) => RankingSortMode = BenchmarkRankingSortMode.Name;

    private void RestorePlacement()
    {
        try
        {
            if (!File.Exists(PlacementPath))
            {
                return;
            }

            var json = File.ReadAllText(PlacementPath);
            var state = JsonSerializer.Deserialize<BenchmarkChartsWindowPlacement>(json);
            if (state is null)
            {
                return;
            }

            if (state.Width > 0)
            {
                Width = Math.Max(MinWidth, state.Width);
            }

            if (state.Height > 0)
            {
                Height = Math.Max(MinHeight, state.Height);
            }

            var workArea = SystemParameters.WorkArea;
            var left = Math.Min(Math.Max(state.Left, workArea.Left), workArea.Right - 120);
            var top = Math.Min(Math.Max(state.Top, workArea.Top), workArea.Bottom - 120);
            Left = left;
            Top = top;
            if (state.WindowState is WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch
        {
        }
    }

    private void SavePlacement()
    {
        try
        {
            Directory.CreateDirectory(IoPath.GetDirectoryName(PlacementPath)!);
            var state = new BenchmarkChartsWindowPlacement
            {
                Left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
                Top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
                Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
                WindowState = WindowState
            };

            File.WriteAllText(PlacementPath, JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
        }
    }

    private void RenderCharts()
    {
        RenderScatterChart();
        RenderHistogram();
    }

    public void RefreshCharts()
    {
        ScheduleRenderCharts();
    }

    private void ScheduleRenderCharts()
    {
        if (!IsLoaded)
        {
            Dispatcher.BeginInvoke(RenderCharts, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.BeginInvoke(RenderCharts, DispatcherPriority.Loaded);
    }

    private void RenderScatterChart()
    {
        ScatterCanvas.Children.Clear();
        var points = Results.Where(result => result.Success).ToList();
        if (points.Count == 0)
        {
            ScatterCanvas.Width = 0;
            ScatterCanvas.Height = 0;
            return;
        }

        var availableWidth = ScatterHost.ActualWidth;
        var availableHeight = ScatterHost.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var squareSize = Math.Max(1, Math.Min(availableWidth, availableHeight));
        ScatterCanvas.Width = squareSize;
        ScatterCanvas.Height = squareSize;
        var width = squareSize;
        var height = squareSize;
        var left = 62d;
        var right = 24d;
        var top = 20d;
        var bottom = 44d;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var maxTime = Math.Max(1, points.Max(p => p.Elapsed.TotalSeconds) * 1.10);
        var minQuality = Math.Max(0, points.Min(p => p.QualityScore) - 5);
        var maxQuality = Math.Min(100, points.Max(p => p.QualityScore) + 5);
        if (Math.Abs(maxQuality - minQuality) < 0.5)
        {
            maxQuality = minQuality + 1;
        }

        var bestPoint = points.OrderByDescending(p => p.QualityScore / Math.Max(0.1, p.Elapsed.TotalSeconds)).FirstOrDefault();

        AddAxisLine(ScatterCanvas, left, top, left, top + plotHeight, WpfBrushes.WhiteSmoke, 1);
        AddAxisLine(ScatterCanvas, left, top + plotHeight, left + plotWidth, top + plotHeight, WpfBrushes.WhiteSmoke, 1);

        for (var i = 0; i <= 4; i++)
        {
            var fraction = i / 4d;
            var x = left + plotWidth * fraction;
            var y = top + plotHeight * (1 - fraction);
            AddGridLine(ScatterCanvas, x, top, x, top + plotHeight);
            AddGridLine(ScatterCanvas, left, y, left + plotWidth, y);
            AddLabel(ScatterCanvas, x - 10, top + plotHeight + 8, $"{maxTime * fraction:0.0}", 11, WpfHorizontalAlignment.Center);
            AddLabel(ScatterCanvas, 4, y - 8, $"{minQuality + (maxQuality - minQuality) * fraction:0}", 11, WpfHorizontalAlignment.Left);
        }

        AddLabel(ScatterCanvas, left + plotWidth * 0.5 - 50, top + plotHeight + 26, LocalizedStrings.BenchmarkChartTimeAxis, 12, WpfHorizontalAlignment.Center);
        AddLabel(ScatterCanvas, 8, 4, LocalizedStrings.BenchmarkChartQualityAxis, 12, WpfHorizontalAlignment.Left);

        foreach (var point in points)
        {
            var x = left + (point.Elapsed.TotalSeconds / maxTime) * plotWidth;
            var y = top + (1 - ((point.QualityScore - minQuality) / (maxQuality - minQuality))) * plotHeight;
            var brush = GetGroupBrush(point.Group);
            var isBest = bestPoint is not null && ReferenceEquals(point, bestPoint);
            var ellipse = new Ellipse
            {
                Width = isBest ? 16 : 12,
                Height = isBest ? 16 : 12,
                Fill = brush,
                Stroke = isBest ? WpfBrushes.Gold : WpfBrushes.WhiteSmoke,
                StrokeThickness = isBest ? 2.2 : 1.2,
                ToolTip = CreateBenchmarkToolTip(
                    $"{point.Group} / {point.Name}",
                    $"{point.Elapsed.TotalSeconds:0.###} s",
                    $"{LocalizedStrings.ContentMode}: {GetLocalizedContentMode(point.ContentMode)}",
                    $"{LocalizedStrings.BenchmarkChartQualityAxis}: {point.QualityScore:0.#}",
                    $"{LocalizedStrings.BenchmarkBestQualityTimeLabel}: {point.QualityScore / Math.Max(0.1, point.Elapsed.TotalSeconds):0.###}")
            };
            Canvas.SetLeft(ellipse, x - (isBest ? 8 : 6));
            Canvas.SetTop(ellipse, y - (isBest ? 8 : 6));
            ScatterCanvas.Children.Add(ellipse);
        }
    }

    private void RenderHistogram()
    {
        HistogramCanvas.Children.Clear();
        var points = Results.Where(result => result.Success).ToList();
        if (points.Count == 0)
        {
            HistogramCanvas.Width = 0;
            HistogramCanvas.Height = 0;
            return;
        }

        var availableWidth = HistogramHost.ActualWidth;
        var availableHeight = HistogramHost.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var height = Math.Max(availableHeight, 360);
        var left = 78d;
        var right = 24d;
        var top = 18d;
        var bottom = 72d;
        var width = Math.Max(1, availableWidth);
        var rowHeight = 34d;
        var gap = 14d;
        var plotWidth = Math.Max(1, width - left - right);
        var modeIsRuntime = ChartMode == BenchmarkChartMode.Runtime;
        var maxMetric = modeIsRuntime
            ? Math.Max(1, points.Max(p => p.Elapsed.TotalSeconds) * 1.10)
            : Math.Max(1, points.Max(p => p.QualityScore / Math.Max(0.1, p.Elapsed.TotalSeconds)) * 1.10);
        var orderedPoints = RankingSortMode == BenchmarkRankingSortMode.Name
            ? points.OrderBy(p => p.Group, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : modeIsRuntime
                ? points.OrderByDescending(p => p.Elapsed).ThenBy(p => p.Group, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : points.OrderByDescending(p => p.QualityScore / Math.Max(0.1, p.Elapsed.TotalSeconds)).ThenBy(p => p.Group, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var neededHeight = top + orderedPoints.Count * (rowHeight + gap) + bottom;
        height = Math.Max(height, neededHeight);

        HistogramCanvas.Width = width;
        HistogramCanvas.Height = height;

        AddAxisLine(HistogramCanvas, left, top, left, height - bottom, WpfBrushes.WhiteSmoke, 1);
        AddAxisLine(HistogramCanvas, left, height - bottom, width - right, height - bottom, WpfBrushes.WhiteSmoke, 1);

        for (var i = 0; i <= 4; i++)
        {
            var fraction = i / 4d;
            var x = left + plotWidth * fraction;
            AddGridLine(HistogramCanvas, x, top, x, height - bottom);
            AddLabel(HistogramCanvas, x - 18, height - bottom + 8, $"{maxMetric * fraction:0.0}", 11, WpfHorizontalAlignment.Center, 36);
        }

        AddLabel(HistogramCanvas, left + plotWidth * 0.5 - 70, height - 2, modeIsRuntime ? LocalizedStrings.BenchmarkChartTimeAxis : LocalizedStrings.BenchmarkQualityTimeMode, 12, WpfHorizontalAlignment.Center);

        for (var i = 0; i < orderedPoints.Count; i++)
        {
            var point = orderedPoints[i];
            var y = top + i * (rowHeight + gap);
            var metric = modeIsRuntime
                ? point.Elapsed.TotalSeconds
                : point.QualityScore / Math.Max(0.1, point.Elapsed.TotalSeconds);
            var barWidth = Math.Max(8, Math.Min(plotWidth, (metric / maxMetric) * plotWidth));
            var bar = new WpfRectangle
            {
                Width = barWidth,
                Height = rowHeight,
                RadiusX = 9,
                RadiusY = 9,
                Fill = GetGroupBrush(point.Group),
                ToolTip = modeIsRuntime
                    ? CreateBenchmarkToolTip(
                        $"{point.Group} / {point.Name}",
                        $"{LocalizedStrings.BenchmarkChartTimeAxis}: {metric:0.###} s",
                        $"{LocalizedStrings.ContentMode}: {GetLocalizedContentMode(point.ContentMode)}",
                        $"{LocalizedStrings.BenchmarkChartQualityAxis}: {point.QualityScore:0.#}",
                        $"{LocalizedStrings.BenchmarkBestQualityTimeLabel}: {point.QualityScore / Math.Max(0.1, point.Elapsed.TotalSeconds):0.###}")
                    : CreateBenchmarkToolTip(
                        $"{point.Group} / {point.Name}",
                        $"{LocalizedStrings.BenchmarkQualityTimeMode}: {metric:0.###}",
                        $"{LocalizedStrings.ContentMode}: {GetLocalizedContentMode(point.ContentMode)}",
                        $"{LocalizedStrings.BenchmarkChartTimeAxis}: {point.Elapsed.TotalSeconds:0.###} s",
                        $"{LocalizedStrings.BenchmarkChartQualityAxis}: {point.QualityScore:0.#}")
            };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, y);
            HistogramCanvas.Children.Add(bar);

            AddLabel(HistogramCanvas, 6, y + 7, GetShortRankingLabel(point), 10, WpfHorizontalAlignment.Left, Math.Max(0, left - 16));
            AddLabel(HistogramCanvas, left + barWidth + 8, y + 7, $"{metric:0.###}", 11, WpfHorizontalAlignment.Left, Math.Max(0, width - left - right - barWidth - 8));
        }
    }

    private static void AddAxisLine(Canvas canvas, double x1, double y1, double x2, double y2, WpfBrush brush, double thickness)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.7
        });
    }

    private static void AddGridLine(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(WpfColor.FromArgb(50, 159, 176, 199)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 4 }
        });
    }

    private static void AddLabel(Canvas canvas, double left, double top, string text, double size, WpfHorizontalAlignment alignment, double? maxWidth = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = WpfBrushes.WhiteSmoke,
            HorizontalAlignment = alignment
        };

        if (maxWidth.HasValue)
        {
            label.MaxWidth = maxWidth.Value;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.Wrap;
        }

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private System.Windows.Controls.ToolTip CreateBenchmarkToolTip(params string[] lines)
    {
        var stackPanel = new StackPanel();
        foreach (var line in lines)
        {
            stackPanel.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = WpfBrushes.WhiteSmoke,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        return new System.Windows.Controls.ToolTip
        {
            Content = stackPanel,
            Style = (Style)FindResource("BenchmarkToolTipStyle")
        };
    }

    private static string GetShortRankingLabel(BenchmarkCaseResult point)
    {
        return point.Group switch
        {
            "Codec/Preset" => ShortCodecPreset(point.Codec, point.Preset),
            "Anti-flicker" => ShortAntiFlicker(point.ContentMode, point.AntiFlicker, point.Strength),
            "Upscaler threads" => $"{LocalizedStrings.BenchmarkShortThreadsPrefix} {point.UpscalerThreads}",
            "Tile size" => $"{LocalizedStrings.BenchmarkShortTilePrefix} {point.TileSize}",
            _ => point.Name
        };
    }

    private static string GetLocalizedContentMode(string contentMode)
        => contentMode switch
        {
            "anime-ultra" => LocalizedStrings.ContentModeAnimeUltra,
            "animeultra" => LocalizedStrings.ContentModeAnimeUltra,
            "anime" => LocalizedStrings.ContentModeAnime,
            "video" => LocalizedStrings.ContentModeVideo,
            "faces" => LocalizedStrings.ContentModeFaces,
            _ => contentMode
        };

    private static string ShortCodecPreset(string codec, string preset)
    {
        var shortPreset = preset switch
        {
            "slower" => LocalizedStrings.BenchmarkShortCodecSlow,
            "slow" => LocalizedStrings.BenchmarkShortCodecSlow,
            "medium" => LocalizedStrings.BenchmarkShortCodecMed,
            "fast" => LocalizedStrings.BenchmarkShortCodecFast,
            "faster" => LocalizedStrings.BenchmarkShortCodecFast,
            "veryfast" => LocalizedStrings.BenchmarkShortCodecVFast,
            _ => preset
        };

        return $"{codec} {shortPreset}";
    }

    private static string ShortAntiFlicker(string contentMode, bool enabled, double strength)
    {
        var normalized = contentMode.ToLowerInvariant();
        if (!enabled)
        {
            return LocalizedStrings.BenchmarkShortAFOff;
        }

        var mode = normalized switch
        {
            "anime" => strength >= 80 ? LocalizedStrings.BenchmarkShortAFUltra : LocalizedStrings.BenchmarkShortAFAnime,
            "anime-ultra" => LocalizedStrings.BenchmarkShortAFUltra,
            "animeultra" => LocalizedStrings.BenchmarkShortAFUltra,
            "video" => LocalizedStrings.BenchmarkShortAFVideo,
            "faces" => LocalizedStrings.BenchmarkShortAFFaces,
            _ => LocalizedStrings.BenchmarkShortAFOn
        };

        return mode;
    }

    private static string GetLocalizedGroupName(string group)
        => group switch
        {
            "Codec/Preset" => LocalizedStrings.BenchmarkLegendCodecPreset,
            "Anti-flicker" => LocalizedStrings.BenchmarkLegendAntiFlicker,
            "Upscaler threads" => LocalizedStrings.BenchmarkLegendThreads,
            "Tile size" => LocalizedStrings.BenchmarkLegendTileSize,
            _ => group
        };

    private WpfBrush GetGroupBrush(string group)
        => _groupBrushes.TryGetValue(group, out var brush) ? brush : WpfBrushes.SkyBlue;

    private static WpfBrush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)(new BrushConverter().ConvertFromString(hex) ?? WpfBrushes.White);
        brush.Freeze();
        return brush;
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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class BenchmarkChartsWindowPlacement
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public WindowState WindowState { get; set; }
    }
}
