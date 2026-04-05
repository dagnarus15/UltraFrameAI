using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaApplication = System.Windows.Application;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace UltraFrameAI;

public static class WindowCaptionColorManager
{
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
    }

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetBrushColor("CardBrush", out var captionColor))
        {
            captionColor = (MediaColor)MediaColorConverter.ConvertFromString("#111C33");
        }

        if (!TryGetBrushColor("TextPrimaryBrush", out var textColor))
        {
            textColor = Colors.White;
        }

        var darkModeEnabled = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkModeEnabled, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref darkModeEnabled, sizeof(int));

        var caption = ToColorRef(captionColor);
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));

        var border = ToColorRef(captionColor);
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));

        var text = ToColorRef(textColor);
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
    }

    private static bool TryGetBrushColor(string key, out MediaColor color)
    {
        if (MediaApplication.Current?.Resources[key] is SolidColorBrush brush)
        {
            color = brush.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static int ToColorRef(MediaColor color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
