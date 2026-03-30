using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _isAdditionalOverlayAnimating;
    private bool _isScanOverlayAnimating;
    private RenderWindow? _renderWindow;
    private bool _isClosingAfterStop;
    private bool _suppressClosePrompt;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.QueueStateChanged += (_, _) => UpdateDeleteButtonStates();
        _viewModel.OutputConflictRequested += ViewModel_OutputConflictRequested;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
        Closing += Window_Closing;
        SizeChanged += (_, _) => UpdateCardClips();
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RootFolderTextBox.Focus();
            RootFolderTextBox.SelectAll();
        });
    }

    private void UpdateCardClips()
    {
        ApplyRoundedClip(QueuePanelBorder, 18);
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button)
        {
            return;
        }

        if (LanguagePopup.IsOpen)
        {
            _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
            return;
        }

        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
        OpenPopup(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
    }

    private void LanguageChoice_Click(object sender, RoutedEventArgs e)
    {
        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
    }

    private void RecentFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentFoldersPopup.IsOpen)
        {
            _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
            return;
        }

        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        OpenPopup(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
    }

    private void AdditionalButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdditionalOverlayRoot.Visibility == Visibility.Visible)
        {
            _ = CloseAdditionalOverlayAsync();
            return;
        }

        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
        AdditionalOverlayRoot.Visibility = Visibility.Visible;
    }

    private async void AdditionalClose_Click(object sender, RoutedEventArgs e)
    {
        await CloseAdditionalOverlayAsync().ConfigureAwait(true);
    }

    private async void RecentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not RecentFolderItem item)
        {
            return;
        }

        _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
        await _viewModel.LoadRootFolderAsync(item.FolderPath).ConfigureAwait(true);
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        UpdateDropState(e);
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        UpdateDropState(e);
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        _viewModel.SetDropTargetActive(false);
        if (TryGetDropTarget(e) is not string dropTarget)
        {
            return;
        }

        await _viewModel.LoadRootFolderAsync(dropTarget).ConfigureAwait(true);
    }

    private void BrowseInputButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrowseInputPopup.IsOpen)
        {
            _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
            return;
        }

        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
        OpenPopup(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
    }

    private void BrowseFolderChoice_Click(object sender, RoutedEventArgs e)
    {
        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _viewModel.BrowseRootFolderCommand.Execute(null);
    }

    private void BrowseFileChoice_Click(object sender, RoutedEventArgs e)
    {
        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _viewModel.BrowseRootFileCommand.Execute(null);
    }

    private void BenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var sourcePath = _viewModel.SelectedItem?.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            sourcePath = string.Empty;
        }

        var benchmarkWindow = new BenchmarkWindow(sourcePath)
        {
            Owner = this
        };

        benchmarkWindow.ShowDialog();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _viewModel.Items.Where(item => item.IsChecked && !item.IsBusy).ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        _viewModel.RemoveItems(selectedItems);
        UpdateDeleteButtonStates();
    }

    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowDeleteAllConfirmation();
    }

    private void QueueItemCheckChanged(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => _viewModel.NotifyQueueSelectionChanged()));
    }

    private async void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        await CloseDeleteOverlayAsync().ConfigureAwait(true);
        _viewModel.CancelDeleteConfirmation();
    }

    private async void DeleteConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ConfirmDeleteAll();
        await CloseDeleteOverlayAsync().ConfigureAwait(true);
        _viewModel.CancelDeleteConfirmation();
        UpdateDeleteButtonStates();
    }

    private void DeleteOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DeleteOverlayRoot.Visibility != Visibility.Visible)
        {
            return;
        }

        if (TryFindResource("DeletePopupIn") is Storyboard openStoryboard)
        {
            openStoryboard.Begin(this, true);
        }
    }

    private async Task CloseDeleteOverlayAsync()
    {
        if (DeleteOverlayRoot.Visibility != Visibility.Visible)
        {
            return;
        }

        if (TryFindResource("DeletePopupOut") is Storyboard closeStoryboard)
        {
            closeStoryboard.Begin(this, true);
            await Task.Delay(110).ConfigureAwait(true);
            return;
        }

        await Task.Delay(110).ConfigureAwait(true);
    }

    private void UpdateDeleteButtonStates()
    {
        var anySelectedDeletable = _viewModel.Items.Any(item => item.IsChecked && !item.IsBusy);
        var anyDeletable = _viewModel.Items.Any(item => !item.IsBusy);

        _viewModel.SetDeleteSelectedEnabled(anySelectedDeletable);
        _viewModel.SetDeleteAllEnabled(anyDeletable);
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _viewModel.SetDropTargetActive(false);
    }

    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource, LanguageButton) ||
            IsWithinButton(e.OriginalSource, RecentFoldersButton) ||
            IsWithinButton(e.OriginalSource, SettingsButton) ||
            IsWithinButton(e.OriginalSource, BrowseInputButton))
        {
            return;
        }

        if (AdditionalOverlayRoot.Visibility == Visibility.Visible && !IsWithinElement(e.OriginalSource, AdditionalOverlayBorder))
        {
            await CloseAdditionalOverlayAsync().ConfigureAwait(true);
        }

        if (LanguagePopup.IsOpen)
        {
            await ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate).ConfigureAwait(true);
        }

        if (RecentFoldersPopup.IsOpen)
        {
            await ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate).ConfigureAwait(true);
        }

        if (BrowseInputPopup.IsOpen && !IsWithinElement(e.OriginalSource, BrowseInputPopupBorder))
        {
            await ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate).ConfigureAwait(true);
        }

    }

    private void UpdateDropState(System.Windows.DragEventArgs e)
    {
        var canDrop = TryGetDropTarget(e) is not null;
        _viewModel.SetDropTargetActive(canDrop);
        e.Effects = canDrop ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private static string? TryGetDropTarget(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return null;
        }

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }

            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
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

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        content.BeginAnimation(OpacityProperty, fadeIn);

        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        content.BeginAnimation(OpacityProperty, fadeOut);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
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

    private void AdditionalOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (AdditionalOverlayRoot.Visibility != Visibility.Visible || _isAdditionalOverlayAnimating)
        {
            return;
        }

        _isAdditionalOverlayAnimating = true;
        AdditionalOverlayRoot.Opacity = 0;
        AdditionalOverlayBorder.Opacity = 0;
        AdditionalOverlayScale.ScaleX = 0.96;
        AdditionalOverlayScale.ScaleY = 0.96;
        AdditionalOverlayTranslate.Y = 14;

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        AdditionalOverlayRoot.BeginAnimation(OpacityProperty, fadeIn);
        AdditionalOverlayBorder.BeginAnimation(OpacityProperty, fadeIn);
        AdditionalOverlayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        AdditionalOverlayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        AdditionalOverlayTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        _ = Task.Delay(140).ContinueWith(_ => Dispatcher.Invoke(() => _isAdditionalOverlayAnimating = false));
    }

    private async Task CloseAdditionalOverlayAsync()
    {
        if (AdditionalOverlayRoot.Visibility != Visibility.Visible || _isAdditionalOverlayAnimating)
        {
            return;
        }

        _isAdditionalOverlayAnimating = true;

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        AdditionalOverlayRoot.BeginAnimation(OpacityProperty, fadeOut);
        AdditionalOverlayBorder.BeginAnimation(OpacityProperty, fadeOut);
        AdditionalOverlayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        AdditionalOverlayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        AdditionalOverlayTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(14, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });

        await Task.Delay(110).ConfigureAwait(true);
        AdditionalOverlayRoot.Visibility = Visibility.Collapsed;
        AdditionalOverlayRoot.Opacity = 1;
        _isAdditionalOverlayAnimating = false;
    }

    private static void ApplyRoundedClip(System.Windows.FrameworkElement element, double radius)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ViewModel_PropertyChanged(sender, e)));
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsScanOverlayVisible))
        {
            if (_viewModel.IsScanOverlayVisible)
            {
                await OpenScanOverlayAsync().ConfigureAwait(true);
            }
            else
            {
                await CloseScanOverlayAsync().ConfigureAwait(true);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            UpdateDeleteButtonStates();
            if (_isClosingAfterStop && !_viewModel.IsBusy)
            {
                _suppressClosePrompt = true;
                Close();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsRenderMode))
        {
            if (_viewModel.IsRenderMode)
            {
                ShowRenderWindow();
            }
            else
            {
                CloseRenderWindow();
            }
        }
    }

    private void ShowRenderWindow()
    {
        if (_renderWindow is null || !_renderWindow.IsLoaded)
        {
            _renderWindow = new RenderWindow
            {
                Owner = this,
                DataContext = _viewModel
            };
            _renderWindow.Closed += RenderWindow_Closed;
            _renderWindow.Show();
        }
        else
        {
            if (_renderWindow.WindowState == WindowState.Minimized)
            {
                _renderWindow.WindowState = WindowState.Normal;
            }

            _renderWindow.Activate();
            _renderWindow.Show();
        }

        Hide();
    }

    private void CloseRenderWindow()
    {
        if (_renderWindow is null)
        {
            Show();
            Activate();
            return;
        }

        if (_renderWindow.IsVisible)
        {
            _renderWindow.Close();
        }

        _renderWindow = null;
        Show();
        Activate();
    }

    private void RenderWindow_Closed(object? sender, EventArgs e)
    {
        _renderWindow = null;
        if (!_suppressClosePrompt)
        {
            Show();
            Activate();
        }
    }

    private async Task<OutputConflictDecision> ViewModel_OutputConflictRequested(OutputConflictRequest request)
    {
        var owner = _renderWindow ?? (Window?)this;
        var dialog = new OutputConflictDialog(request)
        {
            Owner = owner
        };

        var result = dialog.ShowDialog();
        await Task.Yield();
        return result == true ? dialog.Decision : OutputConflictDecision.Cancel;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_suppressClosePrompt)
        {
            return;
        }

        if (!_viewModel.IsBusy)
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

        _isClosingAfterStop = true;
        _viewModel.CancelCommand.Execute(null);
    }

    private async Task OpenScanOverlayAsync()
    {
        if (_isScanOverlayAnimating)
        {
            return;
        }

        _isScanOverlayAnimating = true;
        ScanOverlayRoot.Visibility = Visibility.Visible;
        ScanOverlayRoot.Opacity = 0;
        ScanPopupBorder.Opacity = 0;
        ScanPopupScale.ScaleX = 0.96;
        ScanPopupScale.ScaleY = 0.96;
        ScanPopupTranslate.Y = 14;

        if (TryFindResource("ScanPopupIn") is Storyboard openStoryboard)
        {
            openStoryboard.Begin(this, true);
        }

        if (TryFindResource("ScanPulseLoop") is Storyboard pulseStoryboard)
        {
            pulseStoryboard.Begin(this, true);
        }

        await Task.Delay(130).ConfigureAwait(true);
        _isScanOverlayAnimating = false;
    }

    private async Task CloseScanOverlayAsync()
    {
        if (_isScanOverlayAnimating)
        {
            return;
        }

        _isScanOverlayAnimating = true;

        if (TryFindResource("ScanPopupOut") is Storyboard closeStoryboard)
        {
            closeStoryboard.Begin(this, true);
        }
        else
        {
            ScanOverlayRoot.Opacity = 0;
            ScanPopupBorder.Opacity = 0;
        }

        await Task.Delay(110).ConfigureAwait(true);
        ScanOverlayRoot.Visibility = Visibility.Collapsed;
        _isScanOverlayAnimating = false;
    }
}
