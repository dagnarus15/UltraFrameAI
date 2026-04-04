using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaImageSource = System.Windows.Media.ImageSource;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace UltraFrameAI;

public partial class BackgroundColorDialog : Window, INotifyPropertyChanged
{
    private const double HueMarkerWidth = 12;
    private const double PaletteMarkerSize = 14;
    private readonly List<MediaColor> _presetColors =
    [
        (MediaColor)WpfColorConverter.ConvertFromString("#000000"),
        (MediaColor)WpfColorConverter.ConvertFromString("#FFFFFF"),
        (MediaColor)WpfColorConverter.ConvertFromString("#1A1F33"),
        (MediaColor)WpfColorConverter.ConvertFromString("#1A2436"),
        (MediaColor)WpfColorConverter.ConvertFromString("#132B2A"),
        (MediaColor)WpfColorConverter.ConvertFromString("#2B1F16"),
        (MediaColor)WpfColorConverter.ConvertFromString("#2A1726"),
        (MediaColor)WpfColorConverter.ConvertFromString("#231A38"),
        (MediaColor)WpfColorConverter.ConvertFromString("#18203D"),
        (MediaColor)WpfColorConverter.ConvertFromString("#142B3B")
    ];

    private bool _draggingPalette;
    private bool _draggingHue;
    private readonly MediaColor _initialColor;
    private readonly MediaColor _defaultColor;
    private MediaColor _selectedColor;
    private MediaBrush _selectedBackgroundBrush = WpfBrushes.Transparent;
    private MediaBrush _initialBackgroundBrush = WpfBrushes.Transparent;
    private MediaBrush _defaultBackgroundBrush = WpfBrushes.Transparent;
    private MediaImageSource? _paletteImageSource;
    private string _hexColorText = string.Empty;
    private double _hueMarkerX;
    private double _paletteMarkerX;
    private double _paletteMarkerY;
    private double _selectedHue;
    private double _selectedSaturation;
    private double _selectedValue;
    private bool _suppressHexUpdate;

    public BackgroundColorDialog(MediaColor initialColor)
    {
        InitializeComponent();
        DataContext = this;
        PresetBrushes = new ObservableCollection<MediaBrush>(_presetColors.Select(CreateBrush));
        _initialColor = initialColor;
        _defaultColor = AppThemeManager.DefaultAppBackgroundColor;
        InitialBackgroundBrush = CreateBrush(initialColor);
        DefaultBackgroundBrush = CreateBrush(_defaultColor);
        SetSelectedColor(initialColor);
        Loaded += (_, _) => UpdateMarkersFromCurrentColor();
        SizeChanged += (_, _) => UpdateMarkersFromCurrentColor();
        Closing += BackgroundColorDialog_Closing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MediaBrush> PresetBrushes { get; }

    public MediaBrush InitialBackgroundBrush
    {
        get => _initialBackgroundBrush;
        private set
        {
            _initialBackgroundBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InitialBackgroundBrush)));
        }
    }

    public MediaBrush DefaultBackgroundBrush
    {
        get => _defaultBackgroundBrush;
        private set
        {
            _defaultBackgroundBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultBackgroundBrush)));
        }
    }

    public string InitialColorText => $"{LocalizedStrings.BackgroundColorPrevious}: {ToHex(_initialColor)}";

    public string DefaultColorText => $"{LocalizedStrings.BackgroundColorDefault}: {ToHex(_defaultColor)}";

    public MediaBrush SelectedBackgroundBrush
    {
        get => _selectedBackgroundBrush;
        private set
        {
            _selectedBackgroundBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBackgroundBrush)));
        }
    }

    public MediaImageSource? PaletteImageSource
    {
        get => _paletteImageSource;
        private set
        {
            _paletteImageSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PaletteImageSource)));
        }
    }

    public string HexColorText
    {
        get => _hexColorText;
        set
        {
            if (_hexColorText == value)
            {
                return;
            }

            _hexColorText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HexColorText)));

            if (_suppressHexUpdate)
            {
                return;
            }

            var normalized = NormalizeHexCandidate(value);
            if (normalized.Length == 7 && TryParseHexColor(normalized, out var parsed))
            {
                SetSelectedColor(parsed);
                UpdateMarkersFromCurrentColor();
            }
        }
    }

    public double HueMarkerX
    {
        get => _hueMarkerX;
        private set
        {
            _hueMarkerX = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HueMarkerX)));
        }
    }

    public double PaletteMarkerX
    {
        get => _paletteMarkerX;
        private set
        {
            _paletteMarkerX = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PaletteMarkerX)));
        }
    }

    public double PaletteMarkerY
    {
        get => _paletteMarkerY;
        private set
        {
            _paletteMarkerY = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PaletteMarkerY)));
        }
    }

    public MediaColor SelectedColor => _selectedColor;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        AppThemeManager.ApplyBackgroundColor(_initialColor);
        DialogResult = false;
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaBrush brush } && brush is SolidColorBrush solid)
        {
            SetSelectedColor(solid.Color);
            UpdateMarkersFromCurrentColor();
        }
    }

    private void InitialColorPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SetSelectedColor(_initialColor);
        UpdateMarkersFromCurrentColor();
    }

    private void DefaultColorPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SetSelectedColor(_defaultColor);
        UpdateMarkersFromCurrentColor();
    }

    private void PaletteBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingPalette = true;
        PaletteBorder.CaptureMouse();
        UpdateColorFromPalette(e.GetPosition(PaletteBorder));
    }

    private void PaletteBorder_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_draggingPalette)
        {
            return;
        }

        UpdateColorFromPalette(e.GetPosition(PaletteBorder));
    }

    private void PaletteBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingPalette = false;
        PaletteBorder.ReleaseMouseCapture();
    }

    private void HueBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueBorder.CaptureMouse();
        UpdateHue(e.GetPosition(HueBorder).X);
    }

    private void HueBorder_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_draggingHue)
        {
            return;
        }

        UpdateHue(e.GetPosition(HueBorder).X);
    }

    private void HueBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = false;
        HueBorder.ReleaseMouseCapture();
    }

    private void HexColorTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyHexColorIfValid();
    }

    private void HexColorTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyHexColorIfValid();
        e.Handled = true;
    }

    private void UpdateColorFromPalette(WpfPoint point)
    {
        var width = Math.Max(1, PaletteBorder.ActualWidth);
        var height = Math.Max(1, PaletteBorder.ActualHeight);
        _selectedSaturation = Math.Clamp(point.X / width, 0, 1);
        _selectedValue = 1.0 - Math.Clamp(point.Y / height, 0, 1);
        ApplyCurrentHsv();
        UpdatePaletteMarker(width, height);
    }

    private void UpdateHue(double x)
    {
        var width = Math.Max(1, HueBorder.ActualWidth);
        var normalized = Math.Clamp(x / width, 0, 1);
        _selectedHue = normalized * 360.0;
        PaletteImageSource = CreatePaletteImage(_selectedHue);
        ApplyCurrentHsv();
        UpdateHueMarker(width);
        UpdatePaletteMarker();
    }

    private void UpdateMarkersFromCurrentColor()
    {
        PaletteImageSource = CreatePaletteImage(_selectedHue);
        UpdateHueMarker();
        UpdatePaletteMarker();
    }

    private void UpdateHueMarker()
    {
        UpdateHueMarker(Math.Max(1, HueBorder.ActualWidth));
    }

    private void UpdateHueMarker(double width)
    {
        var normalized = _selectedHue / 360.0;
        HueMarkerX = Math.Clamp(normalized * width - HueMarkerWidth / 2.0, 0, Math.Max(0, width - HueMarkerWidth));
    }

    private void UpdatePaletteMarker()
    {
        UpdatePaletteMarker(Math.Max(1, PaletteBorder.ActualWidth), Math.Max(1, PaletteBorder.ActualHeight));
    }

    private void UpdatePaletteMarker(double width, double height)
    {
        PaletteMarkerX = Math.Clamp(_selectedSaturation * width - PaletteMarkerSize / 2.0, 0, Math.Max(0, width - PaletteMarkerSize));
        PaletteMarkerY = Math.Clamp((1.0 - _selectedValue) * height - PaletteMarkerSize / 2.0, 0, Math.Max(0, height - PaletteMarkerSize));
    }

    private void SetSelectedColor(MediaColor color)
    {
        _selectedColor = color;
        SelectedBackgroundBrush = CreateBrush(color);
        (_selectedHue, _selectedSaturation, _selectedValue) = ColorToHsv(color);
        PaletteImageSource = CreatePaletteImage(_selectedHue);
        UpdateHexColorText(color);
        AppThemeManager.ApplyBackgroundColor(color);
    }

    private void ApplyCurrentHsv()
    {
        SetSelectedColor(ColorFromHsv(_selectedHue, _selectedSaturation, _selectedValue));
    }

    private void BackgroundColorDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (DialogResult == true)
        {
            return;
        }

        AppThemeManager.ApplyBackgroundColor(_initialColor);
    }

    private static MediaImageSource CreatePaletteImage(double hue, int width = 512, int height = 220)
    {
        width = Math.Max(32, width);
        height = Math.Max(32, height);
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var value = 1.0 - y / (double)(height - 1);
            for (var x = 0; x < width; x++)
            {
                var saturation = x / (double)(width - 1);
                var color = ColorFromHsv(hue, saturation, value);
                var offset = y * stride + x * 4;
                pixels[offset + 0] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static MediaBrush CreateBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateHexColorText(MediaColor color)
    {
        _suppressHexUpdate = true;
        HexColorText = ToHex(color);
        _suppressHexUpdate = false;
    }

    private void ApplyHexColorIfValid()
    {
        if (_suppressHexUpdate)
        {
            return;
        }

        if (TryParseHexColor(HexColorText, out var parsed))
        {
            SetSelectedColor(parsed);
            UpdateMarkersFromCurrentColor();
            return;
        }

        UpdateHexColorText(_selectedColor);
    }

    private static bool TryParseHexColor(string? raw, out MediaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = NormalizeHexCandidate(raw);

        try
        {
            if (WpfColorConverter.ConvertFromString(normalized) is MediaColor parsed)
            {
                color = MediaColor.FromRgb(parsed.R, parsed.G, parsed.B);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string NormalizeHexCandidate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Trim();
        if (!normalized.StartsWith('#'))
        {
            normalized = "#" + normalized;
        }

        return normalized;
    }

    private static (double H, double S, double V) ColorToHsv(MediaColor color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var h = 0d;
        var delta = max - min;

        if (delta < 0.0001)
        {
            return (0, 0, max);
        }

        if (Math.Abs(max - r) < 0.0001)
        {
            h = ((g - b) / delta + (g < b ? 6 : 0)) * 60d;
        }
        else if (Math.Abs(max - g) < 0.0001)
        {
            h = ((b - r) / delta + 2) * 60d;
        }
        else
        {
            h = ((r - g) / delta + 4) * 60d;
        }

        var s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    private static MediaColor ColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        if (saturation <= 0.0001)
        {
            var gray = (byte)Math.Round(value * 255);
            return MediaColor.FromRgb(gray, gray, gray);
        }

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60d) % 2 - 1));
        var m = value - chroma;

        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return MediaColor.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static string ToHex(MediaColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
