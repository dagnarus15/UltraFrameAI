using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    public RenderPreviewWindow(ImageSource? previewSource, RenderPreviewKind kind)
    {
        InitializeComponent();
        DataContext = this;
        PreviewSource = previewSource;
        PreviewLabel = kind == RenderPreviewKind.Original
            ? LocalizedStrings.Get("RenderPreviewOriginal")
            : LocalizedStrings.Get("RenderPreviewResult");
        Title = PreviewLabel;
        Loaded += (_, _) => Dispatcher.BeginInvoke(Focus);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PreviewLabel { get; }

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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
