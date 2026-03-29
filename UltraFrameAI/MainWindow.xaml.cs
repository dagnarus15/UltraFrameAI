using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _isScanOverlayAnimating;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RootFolderTextBox.Focus();
            RootFolderTextBox.SelectAll();
        });
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

        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        OpenPopup(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);
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

    private void QueueGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Delete || sender is not System.Windows.Controls.DataGrid grid)
        {
            return;
        }

        var selectedItems = grid.SelectedItems.OfType<QueueItemViewModel>().ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        _viewModel.RemoveItems(selectedItems);
        e.Handled = true;
    }

    private void QueueGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateDeleteButtonStates();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = QueueGrid.SelectedItems.OfType<QueueItemViewModel>().ToArray();
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
        _viewModel.SetDeleteSelectedEnabled(QueueGrid.SelectedItems.Count > 0);
        _viewModel.SetDeleteAllEnabled(_viewModel.Items.Count > 0);
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _viewModel.SetDropTargetActive(false);
    }

    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource, LanguageButton) || IsWithinButton(e.OriginalSource, RecentFoldersButton))
        {
            return;
        }

        if (LanguagePopup.IsOpen)
        {
            await ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate).ConfigureAwait(true);
        }

        if (RecentFoldersPopup.IsOpen)
        {
            await ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate).ConfigureAwait(true);
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
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        var file = paths.FirstOrDefault(File.Exists);
        return file is null ? null : Path.GetDirectoryName(file);
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
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        for (var current = dependencyObject; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, button))
            {
                return true;
            }
        }

        return false;
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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
