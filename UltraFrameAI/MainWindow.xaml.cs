using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Diagnostics;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class MainWindow : Window
{
    private enum FfmpegDownloadAvailabilityResult
    {
        Available,
        NetworkError,
        SourceUnavailable
    }

    private readonly MainViewModel _viewModel = new();
    private bool _isAdditionalOverlayAnimating;
    private bool _isHandlingCustomTargetSelection;
    private string _lastConfirmedTarget = "1080p";
    private bool _isScanOverlayAnimating;
    private DateTime _scanOverlayShownAtUtc;
    private int _ffmpegOverlayDepth;
    private RenderWindow? _renderWindow;
    private bool _suppressClosePrompt;
    private bool _startupBenchmarkChecked;
    private const string FfmpegDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string FfmpegDownloadPageUrl = "https://www.gyan.dev/ffmpeg/builds/";
    private readonly string _ffmpegSetupLogPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg-setup.log");

    public MainWindow()
    {
        InitializeComponent();
        WindowCaptionColorManager.Attach(this);
        DataContext = _viewModel;
        _lastConfirmedTarget = _viewModel.SelectedTarget;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.QueueStateChanged += (_, _) => UpdateDeleteButtonStates();
        _viewModel.OutputConflictRequested += ViewModel_OutputConflictRequested;
        Closing += Window_Closing;
        SizeChanged += (_, _) => UpdateCardClips();
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RootFolderTextBox.Focus();
            RootFolderTextBox.SelectAll();
        });
    }

    public Task InitializeStartupAsync()
    {
        return _viewModel.InitializeAsync();
    }

    public Task ShowStartupBenchmarkPromptIfNeededAsync()
    {
        return MaybeOfferStartupBenchmarkAsync();
    }

    public async Task<bool> EnsureFfmpegAvailableAsync()
    {
        if (_viewModel.HasConfiguredFfmpegTools())
        {
            return true;
        }

        while (!_viewModel.HasConfiguredFfmpegTools())
        {
            var dialog = new FfmpegSetupDialog();

            await ShowOverlayDialogAsync(dialog).ConfigureAwait(true);
            switch (dialog.Choice)
            {
                case FfmpegSetupChoice.CloseApp:
                    return false;
                case FfmpegSetupChoice.Download:
                    if (await HandleFfmpegDownloadAsync().ConfigureAwait(true))
                    {
                        return true;
                    }
                    break;
                case FfmpegSetupChoice.ChooseFolder:
                    if (await PromptForFfmpegFolderAsync().ConfigureAwait(true))
                    {
                        return true;
                    }
                    break;
                default:
                    break;
            }
        }

        return true;
    }

    private void OpenFfmpegOverlay()
    {
        _ffmpegOverlayDepth++;
        FfmpegOverlayRoot.Visibility = Visibility.Visible;
    }

    private void CloseFfmpegOverlay()
    {
        _ffmpegOverlayDepth = Math.Max(0, _ffmpegOverlayDepth - 1);
        if (_ffmpegOverlayDepth == 0)
        {
            FfmpegOverlayRoot.Visibility = Visibility.Collapsed;
        }
    }

    private void CenterOverlayDialog(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;

        var hostWidth = ActualWidth > 0 ? ActualWidth : Width;
        var hostHeight = ActualHeight > 0 ? ActualHeight : Height;
        var dialogWidth = dialog.ActualWidth > 0 ? dialog.ActualWidth : (dialog.Width > 0 ? dialog.Width : dialog.MinWidth);
        var dialogHeight = dialog.ActualHeight > 0 ? dialog.ActualHeight : (dialog.Height > 0 ? dialog.Height : 240);

        dialog.Left = Left + Math.Max(0, (hostWidth - dialogWidth) / 2);
        dialog.Top = Top + Math.Max(0, (hostHeight - dialogHeight) / 2);
    }

    private async Task ShowOverlayDialogAsync(Window dialog)
    {
        var completion = new TaskCompletionSource<object?>();
        void OnClosed(object? sender, EventArgs e) => completion.TrySetResult(null);
        void OnLoaded(object? sender, RoutedEventArgs e) => CenterOverlayDialog(dialog);

        OpenFfmpegOverlay();
        dialog.Owner = this;
        dialog.Loaded += OnLoaded;
        dialog.Closed += OnClosed;
        dialog.Show();

        try
        {
            await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            dialog.Loaded -= OnLoaded;
            dialog.Closed -= OnClosed;
            CloseFfmpegOverlay();
        }
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

    private void CodecHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("CodecHelpTitle"), LocalizedStrings.Get("CodecHelpBody"));
    }

    private void FormatHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("FormatHelpTitle"), LocalizedStrings.Get("FormatHelpBody"));
    }

    private void ContainerHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("ContainerHelpTitle"), LocalizedStrings.Get("ContainerHelpBody"));
    }

    private void OverwriteExistingOutputHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("OverwriteExistingOutputHelpTitle"), LocalizedStrings.Get("OverwriteExistingOutputHelpBody"));
    }

    private void PreserveIncompleteOutputHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("PreserveIncompleteOutputHelpTitle"), LocalizedStrings.Get("PreserveIncompleteOutputHelpBody"));
    }

    private void EncoderPresetHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("EncoderPresetHelpTitle"), LocalizedStrings.Get("EncoderPresetHelpBody"));
    }

    private void FfmpegThreadsHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("FfmpegThreadsHelpTitle"), LocalizedStrings.Get("FfmpegThreadsHelpBody"));
    }

    private void UpscalerJobsHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("UpscalerJobsHelpTitle"), LocalizedStrings.Get("UpscalerJobsHelpBody"));
    }

    private void TileSizeHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("TileSizeHelpTitle"), LocalizedStrings.Get("TileSizeHelpBody"));
    }

    private async void FfmpegFolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        await PromptForFfmpegFolderAsync().ConfigureAwait(true);
    }

    private void RepairBrokenTimestampsHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("RepairBrokenTimestampsHelpTitle"), LocalizedStrings.Get("RepairBrokenTimestampsHelpBody"));
    }

    private void ExternalUpscalerHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("ExternalUpscalerHelpTitle"), LocalizedStrings.Get("ExternalUpscalerHelpBody"));
    }

    private void GeneralHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenHelpCenter(HelpCenterTab.HowTo);
    }

    private void SettingsOverlayHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenCodecFormatHelp(sender, LocalizedStrings.Get("SettingsOverviewHelpTitle"), LocalizedStrings.Get("SettingsOverviewHelpBody"));
    }

    private void TopHelpButton_Click(object sender, RoutedEventArgs e)
    {
        OpenHelpCenter(HelpCenterTab.HowTo);
    }

    private void TargetFormat_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isHandlingCustomTargetSelection ||
            sender is not System.Windows.Controls.ComboBox comboBox ||
            comboBox.SelectedItem is not TargetFormatOption option ||
            !option.IsCustomAction)
        {
            return;
        }

        var previousTarget = string.IsNullOrWhiteSpace(_lastConfirmedTarget)
            ? "1080p"
            : _lastConfirmedTarget;
        var initialHeight = ExtractTargetHeight(previousTarget);
        var dialog = new CustomTargetValueDialog(initialHeight)
        {
            Owner = this
        };

        _isHandlingCustomTargetSelection = true;
        try
        {
            if (dialog.ShowDialog() == true)
            {
                _viewModel.ApplyCustomTargetHeight(dialog.SelectedHeight);
                RestoreTargetComboSelection(_viewModel.SelectedTarget);
            }
            else
            {
                RestoreTargetComboSelection(previousTarget);
            }
        }
        finally
        {
            _isHandlingCustomTargetSelection = false;
        }
    }

    private void OpenCodecFormatHelp(object sender, string title, string body)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        if (CodecFormatHelpPopup.IsOpen
            && ReferenceEquals(CodecFormatHelpPopup.PlacementTarget, button))
        {
            CodecFormatHelpPopup.IsOpen = false;
            return;
        }

        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        _ = ClosePopupAsync(BrowseInputPopup, BrowseInputPopupBorder, BrowseInputPopupScale, BrowseInputPopupTranslate);
        _ = ClosePopupAsync(RecentFoldersPopup, RecentFoldersPopupBorder, RecentFoldersPopupScale, RecentFoldersPopupTranslate);

        CodecFormatHelpPopupTitle.Text = title;
        SetHelpPopupBody(body);
        CodecFormatHelpPopup.PlacementTarget = button;
        CodecFormatHelpPopup.IsOpen = true;
    }

    private static int ExtractTargetHeight(string target)
    {
        var digits = new string(target.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) && parsed >= 120
            ? parsed
            : 1080;
    }

    private void RestoreTargetComboSelection(string targetValue)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RestoreTargetComboSelection(MainTargetComboBox, targetValue);
            RestoreTargetComboSelection(SettingsTargetComboBox, targetValue);
        }, DispatcherPriority.Background);
    }

    private static void RestoreTargetComboSelection(System.Windows.Controls.ComboBox comboBox, string targetValue)
    {
        if (comboBox.ItemsSource is not IEnumerable<TargetFormatOption> options)
        {
            comboBox.SelectedValue = targetValue;
            return;
        }

        var match = options.FirstOrDefault(option => string.Equals(option.Value, targetValue, StringComparison.Ordinal));
        if (match is not null)
        {
            comboBox.SelectedItem = match;
        }
        else
        {
            comboBox.SelectedValue = targetValue;
        }
    }

    private void SetHelpPopupBody(string body)
    {
        CodecFormatHelpPopupBody.Inlines.Clear();

        var index = 0;
        while (index < body.Length)
        {
            var boldStart = body.IndexOf("**", index, StringComparison.Ordinal);
            if (boldStart < 0)
            {
                AppendHelpRun(body[index..], false);
                return;
            }

            if (boldStart > index)
            {
                AppendHelpRun(body[index..boldStart], false);
            }

            var boldEnd = body.IndexOf("**", boldStart + 2, StringComparison.Ordinal);
            if (boldEnd < 0)
            {
                AppendHelpRun(body[boldStart..], false);
                return;
            }

            AppendHelpRun(body[(boldStart + 2)..boldEnd], true);
            index = boldEnd + 2;
        }
    }

    private void AppendHelpRun(string text, bool isBold)
    {
        var segments = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                CodecFormatHelpPopupBody.Inlines.Add(new LineBreak());
            }

            if (segments[i].Length == 0)
            {
                continue;
            }

            var run = new Run(segments[i]);
            if (isBold)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            }

            CodecFormatHelpPopupBody.Inlines.Add(run);
        }
    }

    private void OpenHelpCenter(HelpCenterTab tab)
    {
        var dialog = new HelpCenterDialog(tab)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void BenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunStartupBenchmarkAsync(markCompletedOnSuccess: false);
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
        _viewModel.UpdateActionStates();
    }

    private void QueueSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleQueueSelectAll();
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
        _viewModel.UpdateActionStates();
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _viewModel.SetDropTargetActive(false);
    }

    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource, CodecHelpButton) ||
            IsWithinButton(e.OriginalSource, FormatHelpButton) ||
            IsWithinButton(e.OriginalSource, EncoderPresetHelpButton) ||
            IsWithinButton(e.OriginalSource, FfmpegThreadsHelpButton) ||
            IsWithinButton(e.OriginalSource, UpscalerJobsHelpButton) ||
            IsWithinButton(e.OriginalSource, TileSizeHelpButton) ||
            IsWithinButton(e.OriginalSource, SettingsCodecHelpButton) ||
            IsWithinButton(e.OriginalSource, SettingsFormatHelpButton) ||
            IsWithinButton(e.OriginalSource, SettingsContainerHelpButton) ||
            IsWithinButton(e.OriginalSource, SettingsOverlayHelpButton) ||
            IsWithinButton(e.OriginalSource, OverwriteExistingOutputHelpButton) ||
            IsWithinButton(e.OriginalSource, PreserveIncompleteOutputHelpButton) ||
            IsWithinButton(e.OriginalSource, RepairBrokenTimestampsHelpButton))
        {
            return;
        }

        if (CodecFormatHelpPopup.IsOpen && !IsWithinElement(e.OriginalSource, CodecFormatHelpPopupBorder))
        {
            CodecFormatHelpPopup.IsOpen = false;
        }

        if (IsWithinButton(e.OriginalSource, LanguageButton) ||
            IsWithinButton(e.OriginalSource, RecentFoldersButton) ||
            IsWithinButton(e.OriginalSource, SettingsButton) ||
            IsWithinButton(e.OriginalSource, BrowseInputButton))
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
                if (_viewModel.ConsumePendingRenderSessionResults() is { } results)
                {
                    var dialog = new RenderSessionResultsDialog(results)
                    {
                        Owner = this
                    };
                    dialog.ShowDialog();
                }
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTarget))
        {
            _lastConfirmedTarget = _viewModel.SelectedTarget;
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

    private async Task MaybeOfferStartupBenchmarkAsync()
    {
        if (_startupBenchmarkChecked)
        {
            return;
        }

        _startupBenchmarkChecked = true;
        if (!_viewModel.HasConfiguredFfmpegTools())
        {
            return;
        }

        if (!_viewModel.ShouldOfferStartupBenchmark)
        {
            return;
        }

        _viewModel.MarkStartupBenchmarkPromptShown();
        if (!_viewModel.HasDetectedGpuCandidates || string.IsNullOrWhiteSpace(_viewModel.GetStartupBenchmarkSourcePath()))
        {
            ShowStyledMessage(
                LocalizedStrings.StartupBenchmarkUnavailableBody,
                LocalizedStrings.StartupBenchmarkUnavailableTitle);
            return;
        }

        var promptDialog = new StartupBenchmarkPromptDialog(_viewModel.GetStartupBenchmarkGpuCandidates(), _viewModel.CurrentStartupBenchmarkPromptKind)
        {
            Owner = this
        };

        var choice = promptDialog.ShowDialog();
        if (choice != true || !promptDialog.ShouldRunBenchmark)
        {
            return;
        }

        await Dispatcher.Yield(DispatcherPriority.Background);
        await RunStartupBenchmarkAsync(markCompletedOnSuccess: true).ConfigureAwait(true);
    }

    private async Task<bool> ShowFfmpegMessageAsync(string title, string message)
    {
        var popup = new PopupMessageDialog(title, message);
        await ShowOverlayDialogAsync(popup).ConfigureAwait(true);
        return true;
    }

    private async Task<bool> PromptForFfmpegFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizedStrings.Get("FfmpegSetupBrowseDialogTitle"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return false;
        }

        if (_viewModel.TrySetFfmpegDirectory(dialog.SelectedPath, out var error))
        {
            return true;
        }

        await ShowFfmpegMessageAsync(
            LocalizedStrings.Get("FfmpegSetupTitle"),
            string.IsNullOrWhiteSpace(error) ? LocalizedStrings.Get("FfmpegSetupMissingTools") : error).ConfigureAwait(true);
        return false;
    }

    private string? PromptForInstallDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizedStrings.Get("FfmpegSetupInstallDialogTitle"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? Path.Combine(dialog.SelectedPath, "ffmpeg")
            : null;
    }

    private async Task<bool> HandleFfmpegDownloadAsync()
    {
        while (true)
        {
            var availability = await CheckFfmpegDownloadAvailabilityAsync().ConfigureAwait(true);
            if (availability == FfmpegDownloadAvailabilityResult.Available)
            {
                var locationDialog = new FfmpegInstallLocationDialog();

                await ShowOverlayDialogAsync(locationDialog).ConfigureAwait(true);
                if (locationDialog.Choice is FfmpegInstallLocationChoice.None or FfmpegInstallLocationChoice.CloseApp)
                {
                    if (locationDialog.Choice == FfmpegInstallLocationChoice.CloseApp)
                    {
                        return false;
                    }

                    return false;
                }

                var targetDirectory = locationDialog.Choice switch
                {
                    FfmpegInstallLocationChoice.InstallHere => Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
                    FfmpegInstallLocationChoice.ChooseFolder => PromptForInstallDirectory(),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return false;
                }

                return await DownloadAndInstallFfmpegAsync(targetDirectory).ConfigureAwait(true);
            }

            var failureKind = availability == FfmpegDownloadAvailabilityResult.NetworkError
                ? FfmpegDownloadFailureKind.NetworkError
                : FfmpegDownloadFailureKind.SourceUnavailable;
            var failureDialog = new FfmpegDownloadFailureDialog(failureKind);

            await ShowOverlayDialogAsync(failureDialog).ConfigureAwait(true);
            if (failureDialog.Action is FfmpegDownloadFailureAction.None or FfmpegDownloadFailureAction.CloseApp)
            {
                if (failureDialog.Action == FfmpegDownloadFailureAction.CloseApp)
                {
                    return false;
                }

                return false;
            }

            switch (failureDialog.Action)
            {
                case FfmpegDownloadFailureAction.Retry:
                    continue;
                case FfmpegDownloadFailureAction.ChooseFolder:
                    return await PromptForFfmpegFolderAsync().ConfigureAwait(true);
                case FfmpegDownloadFailureAction.OpenDownloadPage:
                    OpenFfmpegDownloadPage();
                    if (await PromptForFfmpegFolderAsync().ConfigureAwait(true))
                    {
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }
    }

    private async Task<FfmpegDownloadAvailabilityResult> CheckFfmpegDownloadAvailabilityAsync()
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, FfmpegDownloadUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
            return response.IsSuccessStatusCode
                ? FfmpegDownloadAvailabilityResult.Available
                : FfmpegDownloadAvailabilityResult.SourceUnavailable;
        }
        catch (TaskCanceledException)
        {
            return FfmpegDownloadAvailabilityResult.NetworkError;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            return FfmpegDownloadAvailabilityResult.NetworkError;
        }
        catch (HttpRequestException)
        {
            return FfmpegDownloadAvailabilityResult.SourceUnavailable;
        }
        catch
        {
            return FfmpegDownloadAvailabilityResult.NetworkError;
        }
    }

    private static void OpenFfmpegDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = FfmpegDownloadPageUrl,
            UseShellExecute = true
        });
    }

    private async Task<bool> DownloadAndInstallFfmpegAsync(string targetDirectory)
    {
        FfmpegInstallProgressDialog? progressDialog = null;
        try
        {
            LogFfmpegSetup($"Install requested. Target directory: {targetDirectory}");
            progressDialog = new FfmpegInstallProgressDialog
            {
                Owner = this
            };
            OpenFfmpegOverlay();
            progressDialog.UpdateProgress(5, LocalizedStrings.Get("FfmpegInstallProgressStarting"));
            progressDialog.Loaded += (_, _) => CenterOverlayDialog(progressDialog);
            progressDialog.Show();
            progressDialog.Activate();
            await Dispatcher.Yield(DispatcherPriority.Background);

            Directory.CreateDirectory(targetDirectory);
            LogFfmpegSetup($"Target directory ready: {targetDirectory}");

            var tempRoot = Path.Combine(Path.GetTempPath(), "UltraFrameAI", "ffmpeg-download", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            LogFfmpegSetup($"Temp root created: {tempRoot}");
            try
            {
                var zipPath = Path.Combine(tempRoot, "ffmpeg-release-essentials.zip");
                progressDialog.UpdateProgress(20, LocalizedStrings.Get("FfmpegInstallProgressDownloading"));
                LogFfmpegSetup($"Downloading archive to: {zipPath}");
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    using var response = await client.GetAsync(FfmpegDownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength;
                    await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
                    await using var output = File.Create(zipPath);
                    await CopyHttpContentWithProgressAsync(
                        input,
                        output,
                        totalBytes,
                        progressDialog,
                        20,
                        65).ConfigureAwait(true);
                }

                var extractDirectory = Path.Combine(tempRoot, "extract");
                progressDialog.UpdateProgress(72, LocalizedStrings.Get("FfmpegInstallProgressExtracting"), isIndeterminate: true);
                LogFfmpegSetup($"Extracting archive to: {extractDirectory}");
                ZipFile.ExtractToDirectory(zipPath, extractDirectory, true);

                var ffmpegPath = Directory.EnumerateFiles(extractDirectory, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                var ffprobePath = Directory.EnumerateFiles(extractDirectory, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(ffmpegPath) || string.IsNullOrWhiteSpace(ffprobePath))
                {
                    LogFfmpegSetup("Archive extraction completed, but ffmpeg.exe or ffprobe.exe was not found.");
                    throw new InvalidOperationException(LocalizedStrings.Get("FfmpegSetupMissingTools"));
                }

                progressDialog.UpdateProgress(84, LocalizedStrings.Get("FfmpegInstallProgressCopying"));
                LogFfmpegSetup($"Copying tools from '{ffmpegPath}' and '{ffprobePath}'");
                File.Copy(ffmpegPath, Path.Combine(targetDirectory, "ffmpeg.exe"), true);
                File.Copy(ffprobePath, Path.Combine(targetDirectory, "ffprobe.exe"), true);

                progressDialog.UpdateProgress(94, LocalizedStrings.Get("FfmpegInstallProgressFinalizing"), isIndeterminate: true);
                if (!_viewModel.TrySetFfmpegDirectory(targetDirectory, out var error))
                {
                    LogFfmpegSetup($"TrySetFfmpegDirectory failed for '{targetDirectory}'. Error: {error}");
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? LocalizedStrings.Get("FfmpegSetupMissingTools") : error);
                }
                LogFfmpegSetup($"FFmpeg directory configured successfully: {targetDirectory}");

                progressDialog.UpdateProgress(100, LocalizedStrings.Get("FfmpegInstallProgressDone"));
                await Task.Delay(180).ConfigureAwait(true);
                progressDialog.Close();
                progressDialog = null;

                var successMessage = string.Join(
                    Environment.NewLine,
                    [
                        LocalizedStrings.Get("FfmpegInstallSuccessTitle"),
                        string.Empty,
                        LocalizedStrings.Get("FfmpegInstallSuccessBody"),
                        string.Empty,
                        LocalizedStrings.Get("FfmpegInstallSuccessLicense"),
                        LocalizedStrings.Get("FfmpegInstallSuccessDisclaimer")
                    ]);

                await ShowFfmpegMessageAsync(
                    LocalizedStrings.Get("FfmpegInstallSuccessTitle"),
                    successMessage).ConfigureAwait(true);
                LogFfmpegSetup("Install flow completed successfully.");
                return true;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            LogFfmpegSetup("UnauthorizedAccessException during install.");
            await ShowFfmpegMessageAsync(
                LocalizedStrings.Get("FfmpegSetupCannotWriteTitle"),
                LocalizedStrings.Get("FfmpegSetupCannotWriteBody")).ConfigureAwait(true);
            return false;
        }
        catch (Exception ex)
        {
            LogFfmpegSetup($"Install failed with exception: {ex}");
            await ShowFfmpegMessageAsync(
                LocalizedStrings.Get("FfmpegSetupDownloadFailedTitle"),
                LocalizedStrings.Get("FfmpegSetupDownloadFailedBody") + Environment.NewLine + Environment.NewLine + ex.Message).ConfigureAwait(true);
            return false;
        }
        finally
        {
            try
            {
                if (progressDialog is not null)
                {
                    progressDialog.Close();
                }
            }
            catch
            {
            }
            CloseFfmpegOverlay();
        }
    }

    private static async Task CopyHttpContentWithProgressAsync(
        Stream input,
        Stream output,
        long? totalBytes,
        FfmpegInstallProgressDialog progressDialog,
        double progressStart,
        double progressEnd)
    {
        var buffer = new byte[81920];
        long totalRead = 0;
        var lastUiUpdate = DateTime.UtcNow;

        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(true);
            if (bytesRead <= 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(true);
            totalRead += bytesRead;

            var shouldUpdate = DateTime.UtcNow - lastUiUpdate >= TimeSpan.FromMilliseconds(80);
            if (!shouldUpdate && totalBytes.HasValue && totalRead < totalBytes.Value)
            {
                continue;
            }

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var fraction = Math.Clamp((double)totalRead / totalBytes.Value, 0d, 1d);
                var progress = progressStart + ((progressEnd - progressStart) * fraction);
                progressDialog.UpdateProgress(
                    progress,
                    $"{LocalizedStrings.Get("FfmpegInstallProgressDownloading")} {Math.Round(fraction * 100):0}%",
                    isIndeterminate: false);
            }
            else
            {
                progressDialog.UpdateProgress(
                    progressStart,
                    LocalizedStrings.Get("FfmpegInstallProgressDownloading"),
                    isIndeterminate: true);
            }

            lastUiUpdate = DateTime.UtcNow;
        }

        progressDialog.UpdateProgress(progressEnd, LocalizedStrings.Get("FfmpegInstallProgressDownloading"));
    }

    private void LogFfmpegSetup(string message)
    {
        try
        {
            File.AppendAllText(_ffmpegSetupLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private async Task RunStartupBenchmarkAsync(bool markCompletedOnSuccess)
    {
        try
        {
            if (!_viewModel.HasDetectedGpuCandidates || string.IsNullOrWhiteSpace(_viewModel.GetStartupBenchmarkSourcePath()))
            {
                ShowStyledMessage(
                    LocalizedStrings.StartupBenchmarkUnavailableBody,
                    LocalizedStrings.StartupBenchmarkUnavailableTitle);
                return;
            }

            var sourcePath = _viewModel.GetStartupBenchmarkSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "UltraFrameAI-startup-benchmark", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDir);
            try
            {
                var request = new StartupBenchmarkRequest(
                    sourcePath,
                    _viewModel.GetSelectedStartupBenchmarkGpuCandidates(),
                    _viewModel.GetResolvedFfmpegExecutablePath(),
                    _viewModel.GetResolvedFfprobeExecutablePath(),
                    outputDir,
                    5);

                var benchmarkWindow = new StartupBenchmarkWindow(request)
                {
                    Owner = this
                };

                var result = benchmarkWindow.ShowDialog();
                if (result == true && benchmarkWindow.Report is not null)
                {
                    _viewModel.StoreStartupBenchmarkAssessment(benchmarkWindow.Report);

                    var resultsDialog = new StartupBenchmarkResultsDialog(benchmarkWindow.Report)
                    {
                        Owner = this
                    };

                    if (resultsDialog.ShowDialog() == true && resultsDialog.ShouldApplyRecommendations)
                    {
                        _viewModel.ApplyStartupBenchmarkRecommendation(benchmarkWindow.Report.Recommendation);
                    }

                    if (markCompletedOnSuccess)
                    {
                        _viewModel.MarkStartupBenchmarkCompleted();
                    }
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, true);
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            ShowStyledMessage(
                ex.ToString(),
                LocalizedStrings.StartupBenchmarkProgressTitle);
        }
    }

    private void ShowStyledMessage(string message, string title)
    {
        var popup = new PopupMessageDialog(title, message)
        {
            Owner = this
        };
        popup.ShowDialog();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _suppressClosePrompt = true;
        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
        }
    }

    private async Task OpenScanOverlayAsync()
    {
        if (ScanOverlayRoot.Visibility == Visibility.Visible && !_isScanOverlayAnimating)
        {
            return;
        }

        _isScanOverlayAnimating = true;
        _scanOverlayShownAtUtc = DateTime.UtcNow;
        ScanOverlayRoot.Visibility = Visibility.Visible;
        ScanOverlayRoot.Opacity = 1;
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
        if (ScanOverlayRoot.Visibility != Visibility.Visible)
        {
            return;
        }

        var remainingVisible = (_scanOverlayShownAtUtc + TimeSpan.FromMilliseconds(260)) - DateTime.UtcNow;
        if (remainingVisible > TimeSpan.Zero)
        {
            await Task.Delay(remainingVisible).ConfigureAwait(true);
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
        ScanOverlayRoot.Opacity = 1;
        ScanPopupBorder.Opacity = 0;
        _isScanOverlayAnimating = false;
    }
}
