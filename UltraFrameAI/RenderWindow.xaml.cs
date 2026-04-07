using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class RenderWindow : Window
{
    private const double PreviewDragThreshold = 5.0;
    private bool _closingAfterStop;
    private bool _suppressClosePrompt;
    private INotifyPropertyChanged? _notifyingDataContext;
    private RenderPreviewKind? _activePreviewDragKind;
    private System.Windows.Point _previewDragStart;
    private double _previewDragStartPanX;
    private double _previewDragStartPanY;
    private bool _previewDragMoved;

    public RenderWindow()
    {
        InitializeComponent();
        WindowCaptionColorManager.Attach(this);
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => Focus());
        DataContextChanged += RenderWindow_DataContextChanged;
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (LanguagePopup.IsOpen)
        {
            _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
            return;
        }

        _ = ClosePopupAsync(FileListPopup, FileListPopupBorder, FileListPopupScale, FileListPopupTranslate);
        OpenPopup(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
    }

    private void LanguageChoice_Click(object sender, RoutedEventArgs e)
    {
        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
    }

    private void TopHelpButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HelpCenterDialog(HelpCenterTab.HowTo)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackgroundColorDialog(AppThemeManager.CurrentBackgroundColor)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            AppThemeManager.ApplyBackgroundColor(dialog.SelectedColor);
        }
    }

    private void FileListButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListPopup.IsOpen)
        {
            FileListPopup.IsOpen = false;
            return;
        }

        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
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

    private void CompareCurrentFrame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        ShowComparePreviewWindows(viewModel);
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

        source = CreateStaticPreviewSource(source);

        var timestamp = viewModel.CurrentFrameTimestampText;
        var window = new RenderPreviewWindow(source, kind, timestamp)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        ApplyPreviewWindowSize(window, source, SystemParameters.WorkArea.Width * 0.78, SystemParameters.WorkArea.Height * 0.78);
        window.Show();
        window.Activate();
    }

    private void ShowComparePreviewWindows(MainViewModel viewModel)
    {
        var original = viewModel.RenderPreviewOriginalImage;
        var result = viewModel.RenderPreviewResultImage;
        if (original is null || result is null)
        {
            return;
        }

        var originalSource = CreateStaticPreviewSource(original);
        var resultSource = CreateStaticPreviewSource(result);
        var timestamp = viewModel.CurrentFrameTimestampText;
        var originalWindow = new RenderPreviewWindow(originalSource, RenderPreviewKind.Original, timestamp)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        var resultWindow = new RenderPreviewWindow(resultSource, RenderPreviewKind.Result, timestamp)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        ArrangeCompareWindows(originalWindow, resultWindow, originalSource, resultSource);

        originalWindow.Show();
        resultWindow.Show();
        resultWindow.Activate();
        originalWindow.Activate();
    }

    private static void ArrangeCompareWindows(RenderPreviewWindow leftWindow, RenderPreviewWindow rightWindow, ImageSource leftSource, ImageSource rightSource)
    {
        var workArea = SystemParameters.WorkArea;
        const double gap = 16;
        const double horizontalMargin = 32;
        const double verticalMargin = 32;

        var availableWidth = Math.Max(640, workArea.Width - horizontalMargin * 2 - gap);
        var slotWidth = Math.Max(320, availableWidth / 2.0);
        var maxHeight = Math.Max(420, workArea.Height - verticalMargin * 2);

        ApplyPreviewWindowSize(leftWindow, leftSource, slotWidth, maxHeight);
        ApplyPreviewWindowSize(rightWindow, rightSource, slotWidth, maxHeight);

        var totalWidth = leftWindow.Width + gap + rightWindow.Width;
        var availableTotalWidth = workArea.Width - horizontalMargin * 2;
        if (totalWidth > availableTotalWidth)
        {
            slotWidth = Math.Max(280, (availableTotalWidth - gap) / 2.0);
            ApplyPreviewWindowSize(leftWindow, leftSource, slotWidth, maxHeight);
            ApplyPreviewWindowSize(rightWindow, rightSource, slotWidth, maxHeight);
            totalWidth = leftWindow.Width + gap + rightWindow.Width;
        }

        var startX = Math.Max(workArea.Left + horizontalMargin, workArea.Left + (workArea.Width - totalWidth) / 2.0);
        var top = Math.Max(workArea.Top + verticalMargin, workArea.Top + (workArea.Height - Math.Max(leftWindow.Height, rightWindow.Height)) / 2.0);

        leftWindow.Left = startX;
        leftWindow.Top = top;
        rightWindow.Left = leftWindow.Left + leftWindow.Width + gap;
        rightWindow.Top = top;
    }

    private static void ApplyPreviewWindowSize(RenderPreviewWindow window, ImageSource source, double maxWidth, double maxHeight)
    {
        const double frameChromeWidth = 48;
        const double frameChromeHeight = 150;

        var sourceWidth = Math.Max(1.0, source.Width);
        var sourceHeight = Math.Max(1.0, source.Height);
        var aspect = sourceWidth / sourceHeight;

        var contentMaxWidth = Math.Max(540, maxWidth - frameChromeWidth);
        var contentMaxHeight = Math.Max(380, maxHeight - frameChromeHeight);

        var contentWidth = contentMaxWidth;
        var contentHeight = contentWidth / aspect;
        if (contentHeight > contentMaxHeight)
        {
            contentHeight = contentMaxHeight;
            contentWidth = contentHeight * aspect;
        }

        window.Width = Math.Max(window.MinWidth, contentWidth + frameChromeWidth);
        window.Height = Math.Max(window.MinHeight, contentHeight + frameChromeHeight);
    }

    private static ImageSource CreateStaticPreviewSource(ImageSource source)
    {
        if (source is not BitmapSource bitmapSource)
        {
            return source;
        }

        try
        {
            var width = bitmapSource.PixelWidth;
            var height = bitmapSource.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return source;
            }

            var renderTarget = new RenderTargetBitmap(
                width,
                height,
                bitmapSource.DpiX,
                bitmapSource.DpiY,
                PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(bitmapSource, new Rect(0, 0, width, height));
            }

            renderTarget.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            var snapshot = new BitmapImage();
            snapshot.BeginInit();
            snapshot.CacheOption = BitmapCacheOption.OnLoad;
            snapshot.StreamSource = stream;
            snapshot.EndInit();
            snapshot.Freeze();
            return snapshot;
        }
        catch
        {
            return source;
        }
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

    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource, LanguageButton) || IsWithinButton(e.OriginalSource, FileListButton) || IsWithinButton(e.OriginalSource, TopHelpButton))
        {
            return;
        }

        if (LanguagePopup.IsOpen && !IsWithinElement(e.OriginalSource, LanguagePopupBorder))
        {
            await ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate).ConfigureAwait(true);
        }

        if (FileListPopup.IsOpen && !IsWithinElement(e.OriginalSource, FileListPopupBorder))
        {
            await ClosePopupAsync(FileListPopup, FileListPopupBorder, FileListPopupScale, FileListPopupTranslate).ConfigureAwait(true);
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

        if (CanCloseDuringPreRenderWarnings(viewModel))
        {
            e.Cancel = true;
            _closingAfterStop = true;
            viewModel.CancelCommand.Execute(null);
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

    private static bool CanCloseDuringPreRenderWarnings(MainViewModel viewModel)
    {
        return viewModel.IsRenderMode
               && string.Equals(viewModel.CurrentStage, LocalizedStrings.LogPreparing, StringComparison.Ordinal)
               && viewModel.OverallProgress <= 0.0001;
    }

    private static void OpenPopup(
        System.Windows.Controls.Primitives.Popup popup,
        System.Windows.FrameworkElement content,
        System.Windows.Media.ScaleTransform scale,
        System.Windows.Media.TranslateTransform translate)
    {
        if (popup.IsOpen)
        {
            return;
        }

        content.Opacity = 0;
        scale.ScaleX = 0.96;
        scale.ScaleY = 0.96;
        translate.Y = 10;
        popup.IsOpen = true;

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        content.BeginAnimation(OpacityProperty, fadeIn);

        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private static async Task ClosePopupAsync(
        System.Windows.Controls.Primitives.Popup popup,
        System.Windows.FrameworkElement content,
        System.Windows.Media.ScaleTransform scale,
        System.Windows.Media.TranslateTransform translate)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        content.BeginAnimation(OpacityProperty, fadeOut);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(10, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        });

        await Task.Delay(110).ConfigureAwait(true);
        popup.IsOpen = false;
    }

    private static bool IsWithinButton(object source, System.Windows.Controls.Button button)
    {
        return IsWithinElement(source, button);
    }

    private static bool IsWithinElement(object source, DependencyObject element)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        for (var current = dependencyObject; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, element))
            {
                return true;
            }
        }

        return false;
    }
}
