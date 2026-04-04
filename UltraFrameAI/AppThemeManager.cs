using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaImageSource = System.Windows.Media.ImageSource;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace UltraFrameAI;

public static class AppThemeManager
{
    private static readonly MediaColor DefaultBackgroundColor = (MediaColor)WpfColorConverter.ConvertFromString("#0B1220");

    static AppThemeManager()
    {
        CurrentBackgroundColor = DefaultBackgroundColor;
    }

    public static event EventHandler? ThemeChanged;

    public static MediaColor DefaultAppBackgroundColor => DefaultBackgroundColor;

    public static MediaColor CurrentBackgroundColor { get; private set; }

    public static string CurrentBackgroundColorHex => CurrentBackgroundColor.ToString(CultureInfo.InvariantCulture);

    public static MediaBrush CreatePreviewBrush()
    {
        var brush = new SolidColorBrush(CurrentBackgroundColor);
        brush.Freeze();
        return brush;
    }

    public static MediaImageSource CreateColorWheelImage(int size = 44)
    {
        size = Math.Max(16, size);
        var stride = size * 4;
        var pixels = new byte[size * stride];
        var center = (size - 1) / 2.0;
        var radius = size / 2.0 - 1;
        var innerRadius = radius * 0.40;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius || distance < innerRadius)
                {
                    continue;
                }

                var angle = Math.Atan2(dy, dx);
                var hue = (angle * 180.0 / Math.PI + 360.0) % 360.0;
                var color = HslToColor(hue, 0.95, 0.58);
                var offset = y * stride + x * 4;
                pixels[offset + 0] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static void ApplyBackgroundColor(MediaColor color)
    {
        CurrentBackgroundColor = color;

        if (WpfApplication.Current?.Resources is not WpfResourceDictionary resources)
        {
            ThemeChanged?.Invoke(null, EventArgs.Empty);
            return;
        }

        SetBrush(resources, "AppBgBrush", color);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyBackgroundColor(string? colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            try
            {
                if (WpfColorConverter.ConvertFromString(colorHex) is MediaColor parsed)
                {
                    ApplyBackgroundColor(parsed);
                    return;
                }
            }
            catch
            {
            }
        }

        ApplyBackgroundColor(DefaultBackgroundColor);
    }

    public static MediaColor GetColorForHue(double hue)
    {
        hue = ((hue % 360) + 360) % 360;
        return HslToColor(hue, 1.0, 0.5);
    }

    public static void InitializeMutableResources()
    {
        if (WpfApplication.Current?.Resources is not WpfResourceDictionary resources)
        {
            return;
        }

        if (resources["AppBgBrush"] is SolidColorBrush existing)
        {
            resources["AppBgBrush"] = existing.IsFrozen ? existing.CloneCurrentValue() : existing;
        }
        else
        {
            resources["AppBgBrush"] = new SolidColorBrush(CurrentBackgroundColor);
        }

    }

    private static void SetBrush(WpfResourceDictionary resources, string key, MediaColor color)
    {
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static (double H, double S, double L) RgbToHsl(MediaColor color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var h = 0d;
        var l = (max + min) / 2d;
        var delta = max - min;
        if (delta < 0.0001)
        {
            return (0, 0, l);
        }

        var s = l > 0.5 ? delta / (2d - max - min) : delta / (max + min);
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

        return (h, s, l);
    }

    private static MediaColor HslToColor(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        if (s < 0.0001)
        {
            var gray = (byte)Math.Round(l * 255);
            return MediaColor.FromRgb(gray, gray, gray);
        }

        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60d) % 2 - 1));
        var m = l - c / 2;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return MediaColor.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
