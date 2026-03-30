using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class RenderWindow : Window
{
    private const double PreviewDragThreshold = 5.0;
    private bool _closingAfterStop;
    private bool _suppressClosePrompt;
    private INotifyPropertyChanged? _notifyingDataContext;
    private RenderPreviewWindow? _originalPreviewWindow;
    private RenderPreviewWindow? _resultPreviewWindow;
    private RenderPreviewKind? _activePreviewDragKind;
    private System.Windows.Point _previewDragStart;
    private double _previewDragStartPanX;
    private double _previewDragStartPanY;
    private bool _previewDragMoved;

    public RenderWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => Focus());
        DataContextChanged += RenderWindow_DataContextChanged;
    }

    private void FileListButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListPopup.IsOpen)
        {
            FileListPopup.IsOpen = false;
            return;
        }

        FileListPopupBorder.Opacity = 1;
        FileListPopupScale.ScaleX = 1;
        FileListPopupScale.ScaleY = 1;
        FileListPopupTranslate.Y = 0;
        FileListPopup.IsOpen = true;
    }

    private void ResetRenderPreviewZoom_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.RenderPreviewZoom = 1.0;
        viewModel.RenderPreviewPanX = 0;
        viewModel.RenderPreviewPanY = 0;
    }

    private void OriginalPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginPreviewInteraction(RenderPreviewKind.Original, sender, e);
    }

    private void OriginalPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdatePreviewInteraction(RenderPreviewKind.Original, e);
    }

    private void OriginalPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPreviewInteraction(RenderPreviewKind.Original, e);
    }

    private void ResultPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginPreviewInteraction(RenderPreviewKind.Result, sender, e);
    }

    private void ResultPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdatePreviewInteraction(RenderPreviewKind.Result, e);
    }

    private void ResultPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPreviewInteraction(RenderPreviewKind.Result, e);
    }

    private void BeginPreviewInteraction(RenderPreviewKind kind, object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _activePreviewDragKind = kind;
        _previewDragMoved = false;

        if (viewModel.RenderPreviewZoom > 1.0)
        {
            _previewDragStart = e.GetPosition(this);
            _previewDragStartPanX = viewModel.RenderPreviewPanX;
            _previewDragStartPanY = viewModel.RenderPreviewPanY;

            if (sender is IInputElement inputElement)
            {
                inputElement.CaptureMouse();
            }
        }
    }

    private void UpdatePreviewInteraction(RenderPreviewKind kind, System.Windows.Input.MouseEventArgs e)
    {
        if (_activePreviewDragKind != kind || DataContext is not MainViewModel viewModel || viewModel.RenderPreviewZoom <= 1.0)
        {
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - _previewDragStart;
        if (!_previewDragMoved && (Math.Abs(delta.X) >= PreviewDragThreshold || Math.Abs(delta.Y) >= PreviewDragThreshold))
        {
            _previewDragMoved = true;
        }

        if (!_previewDragMoved)
        {
            return;
        }

        viewModel.RenderPreviewPanX = _previewDragStartPanX + delta.X;
        viewModel.RenderPreviewPanY = _previewDragStartPanY + delta.Y;
    }

    private void EndPreviewInteraction(RenderPreviewKind kind, MouseButtonEventArgs e)
    {
        if (_activePreviewDragKind != kind || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            if (_previewDragMoved)
            {
                return;
            }

            ShowPreviewWindow(kind, viewModel);
        }
        finally
        {
            _activePreviewDragKind = null;
            _previewDragMoved = false;
            ReleaseCapturedMouse();
        }
    }

    private void ReleaseCapturedMouse()
    {
        if (Mouse.Captured is IInputElement captured)
        {
            captured.ReleaseMouseCapture();
        }
    }

    private void ShowPreviewWindow(RenderPreviewKind kind, MainViewModel viewModel)
    {
        var source = kind == RenderPreviewKind.Original
            ? viewModel.RenderPreviewOriginalImage
            : viewModel.RenderPreviewResultImage;
        if (source is null)
        {
            return;
        }

        var existing = kind == RenderPreviewKind.Original ? _originalPreviewWindow : _resultPreviewWindow;
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            existing.Focus();
            return;
        }

        var window = new RenderPreviewWindow(source, kind)
        {
            Owner = this
        };

        window.Closed += (_, _) =>
        {
            if (kind == RenderPreviewKind.Original)
            {
                _originalPreviewWindow = null;
            }
            else
            {
                _resultPreviewWindow = null;
            }
        };

        if (kind == RenderPreviewKind.Original)
        {
            _originalPreviewWindow = window;
        }
        else
        {
            _resultPreviewWindow = window;
        }

        window.Show();
        window.Activate();
    }

    private void RenderWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_notifyingDataContext is not null)
        {
            _notifyingDataContext.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _notifyingDataContext = e.NewValue as INotifyPropertyChanged;
        if (_notifyingDataContext is not null)
        {
            _notifyingDataContext.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsBusy))
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && _closingAfterStop && !viewModel.IsBusy)
        {
            if (Dispatcher.CheckAccess())
            {
                _suppressClosePrompt = true;
                Close();
            }
            else
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _suppressClosePrompt = true;
                    Close();
                });
            }
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel || !viewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;
        var dialog = new RenderCloseDialog
        {
            Owner = this
        };

        var result = dialog.ShowDialog();
        if (result != true || dialog.Decision != RenderCloseDecision.StopRendering)
        {
            return;
        }

        _closingAfterStop = true;
        viewModel.CancelCommand.Execute(null);
    }
}
