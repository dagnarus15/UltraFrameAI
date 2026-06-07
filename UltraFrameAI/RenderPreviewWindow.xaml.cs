using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class RenderPreviewWindow : Window, INotifyPropertyChanged
{
    private System.Windows.Point _dragStart;
    private double _startPanX;
    private double _startPanY;
    private bool _isDragging;
    private double _previewZoom = 1.0;
    private double _previewPanX;
    private double _previewPanY;

    public RenderPreviewWindow(ImageSource? previewSource, RenderPreviewKind kind, string? frameTimestampText = null)
    {
        InitializeComponent();
        WindowCaptionColorManager.Attach(this);
        DataContext = this;
        PreviewSource = previewSource;
        PreviewLabel = kind == RenderPreviewKind.Original
            ? LocalizedStrings.Get("RenderPreviewOriginal")
            : LocalizedStrings.Get("RenderPreviewResult");
        FrameTimestampText = string.IsNullOrWhiteSpace(frameTimestampText) ? string.Empty : frameTimestampText.Trim();
        Title = PreviewLabel;
        Loaded += (_, _) => Dispatcher.BeginInvoke(Focus);
        Closed += (_, _) => RestoreOwnerActivation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PreviewLabel { get; }

    public string FrameTimestampText { get; }

    public ImageSource? PreviewSource { get; }

    public double PreviewZoom
    {
        get => _previewZoom;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (Math.Abs(_previewZoom - clamped) < double.Epsilon)
            {
                return;
            }

            _previewZoom = clamped;
            if (Math.Abs(clamped - 1.0) < double.Epsilon)
            {
                PreviewPanX = 0;
                PreviewPanY = 0;
            }

            OnPropertyChanged(nameof(PreviewZoom));
            OnPropertyChanged(nameof(RenderPreviewZoomText));
        }
    }

    public string RenderPreviewZoomText => $"{PreviewZoom * 100:0}%";

    public double PreviewPanX
    {
        get => _previewPanX;
        set
        {
            if (Math.Abs(_previewPanX - value) < double.Epsilon)
            {
                return;
            }

            _previewPanX = value;
            OnPropertyChanged(nameof(PreviewPanX));
        }
    }

    public double PreviewPanY
    {
        get => _previewPanY;
        set
        {
            if (Math.Abs(_previewPanY - value) < double.Epsilon)
            {
                return;
            }

            _previewPanY = value;
            OnPropertyChanged(nameof(PreviewPanY));
        }
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PreviewZoom <= 1.0 || PreviewSource is null)
        {
            return;
        }

        _isDragging = true;
        _dragStart = e.GetPosition(this);
        _startPanX = PreviewPanX;
        _startPanY = PreviewPanY;
        PreviewImage.CaptureMouse();
    }

    private void Preview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        PreviewPanX = _startPanX + (current.X - _dragStart.X);
        PreviewPanY = _startPanY + (current.Y - _dragStart.Y);
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        PreviewImage.ReleaseMouseCapture();
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = e.Delta > 0 ? 0.15 : -0.15;
        PreviewZoom += step;
        e.Handled = true;
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        PreviewZoom = 1.0;
        PreviewPanX = 0;
        PreviewPanY = 0;
    }

    private void SaveFrame_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewSource is null)
        {
            return;
        }

        var bitmap = GetBitmapForSave(PreviewSource);
        if (bitmap is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizedStrings.Get("RenderPreviewSaveFrame"),
            Filter = "PNG image|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = BuildDefaultSaveFileName()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }

    private string BuildDefaultSaveFileName()
    {
        var baseName = PreviewLabel.Replace(' ', '_');
        if (!string.IsNullOrWhiteSpace(FrameTimestampText))
        {
            baseName += $"_{FrameTimestampText}";
        }

        return baseName + ".png";
    }

    private static BitmapSource? GetBitmapForSave(ImageSource source)
    {
        if (source is BitmapSource bitmapSource)
        {
            return bitmapSource;
        }

        try
        {
            var width = (int)Math.Max(1, Math.Round(source.Width));
            var height = (int)Math.Max(1, Math.Round(source.Height));
            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(source, new Rect(0, 0, width, height));
            }

            renderTarget.Render(visual);
            renderTarget.Freeze();
            return renderTarget;
        }
        catch
        {
            return null;
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RestoreOwnerActivation()
    {
        var ownerWindow = Owner;
        if (ownerWindow is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var siblingPreview = System.Windows.Application.Current.Windows
                .OfType<RenderPreviewWindow>()
                .FirstOrDefault(window => !ReferenceEquals(window, this)
                    && ReferenceEquals(window.Owner, ownerWindow)
                    && window.IsVisible);

            if (siblingPreview is not null)
            {
                siblingPreview.Activate();
                return;
            }

            if (ownerWindow.WindowState == WindowState.Minimized)
            {
                ownerWindow.WindowState = WindowState.Normal;
            }

            ownerWindow.Show();
            ownerWindow.Activate();
            ownerWindow.Focus();
        });
    }
}
