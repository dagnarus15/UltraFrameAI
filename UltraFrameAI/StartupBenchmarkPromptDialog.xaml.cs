using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Animation;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class StartupBenchmarkPromptDialog : Window, INotifyPropertyChanged
{
    private bool _allowClose;
    private readonly StartupBenchmarkPromptKind _promptKind;
    private readonly IReadOnlyList<StartupBenchmarkGpuCandidate> _gpuCandidates;

    public StartupBenchmarkPromptDialog(IReadOnlyList<StartupBenchmarkGpuCandidate> gpuCandidates, StartupBenchmarkPromptKind promptKind)
    {
        InitializeComponent();
        _promptKind = promptKind;
        _gpuCandidates = gpuCandidates.ToArray();

        DataContext = this;
        UpdateLocalizedText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HardwareAssessmentLine> AssessmentLines { get; } = new();

    public bool ShouldRunBenchmark { get; private set; }

    public string PromptTitle { get; private set; } = string.Empty;

    public string PromptGreeting { get; private set; } = string.Empty;

    public string PromptDetails { get; private set; } = string.Empty;

    public string PromptBody { get; private set; } = string.Empty;

    public string PromptFootnote { get; private set; } = string.Empty;

    public UiLanguage CurrentLanguage => LocalizedStrings.CurrentLanguage;

    public string CurrentLanguageFlagPath => CurrentLanguage switch
    {
        UiLanguage.Russian => "pack://application:,,,/images/flag-ru.png",
        UiLanguage.German => "pack://application:,,,/images/flag-de.png",
        UiLanguage.Japanese => "pack://application:,,,/images/flag-ja.png",
        UiLanguage.Chinese => "pack://application:,,,/images/flag-zh.png",
        _ => "pack://application:,,,/images/flag-en.png"
    };

    public ImageSource BackgroundColorWheelImage { get; } = AppThemeManager.CreateColorWheelImage();

    public RelayCommand SetLanguageCommand => new(SetLanguage);

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        ShouldRunBenchmark = true;
        _allowClose = true;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ShouldRunBenchmark = false;
        _allowClose = true;
        DialogResult = false;
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (LanguagePopup.IsOpen)
        {
            _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
            return;
        }

        OpenPopup(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
    }

    private void LanguageChoice_Click(object sender, RoutedEventArgs e)
    {
        _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
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

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinElement(e.OriginalSource, LanguageButton))
        {
            return;
        }

        if (LanguagePopup.IsOpen && !IsWithinElement(e.OriginalSource, LanguagePopupBorder))
        {
            _ = ClosePopupAsync(LanguagePopup, LanguagePopupBorder, LanguagePopupScale, LanguagePopupTranslate);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
    }

    private void SetLanguage(object? parameter)
    {
        if (parameter is UiLanguage language)
        {
            LocalizedStrings.SetLanguage(language);
        }
        else if (parameter is string text && Enum.TryParse(text, true, out UiLanguage parsed))
        {
            LocalizedStrings.SetLanguage(parsed);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageFlagPath)));
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        RefreshAssessmentLines();

        switch (_promptKind)
        {
            case StartupBenchmarkPromptKind.NewHardware:
                PromptTitle = LocalizedStrings.StartupBenchmarkNewHardwareTitle;
                PromptGreeting = LocalizedStrings.StartupBenchmarkNewHardwareGreeting;
                PromptDetails = string.Empty;
                PromptBody = LocalizedStrings.StartupBenchmarkNewHardwareBody;
                PromptFootnote = LocalizedStrings.Get("StartupBenchmarkPromptFootnote");
                break;
            default:
                PromptTitle = LocalizedStrings.StartupBenchmarkPromptTitle;
                PromptGreeting = LocalizedStrings.StartupBenchmarkPromptGreeting;
                PromptDetails = LocalizedStrings.StartupBenchmarkPromptThanks;
                PromptBody = LocalizedStrings.StartupBenchmarkPromptBody;
                PromptFootnote = LocalizedStrings.Get("StartupBenchmarkPromptFootnote");
                break;
        }

        Title = PromptTitle;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptGreeting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptDetails)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptBody)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptFootnote)));
    }

    private void RefreshAssessmentLines()
    {
        AssessmentLines.Clear();
        foreach (var line in HardwareAssessmentBuilder.BuildStatic(_gpuCandidates).Lines)
        {
            AssessmentLines.Add(line);
        }
    }

    private static void OpenPopup(Popup popup, FrameworkElement content, ScaleTransform scale, TranslateTransform translate)
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

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = easing });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = easing });
    }

    private static async Task ClosePopupAsync(Popup popup, FrameworkElement content, ScaleTransform scale, TranslateTransform translate)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(100)) { EasingFunction = easing });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(100)) { EasingFunction = easing });

        await Task.Delay(110).ConfigureAwait(true);
        popup.IsOpen = false;
    }

    private static bool IsWithinElement(object source, DependencyObject element)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        for (var current = dependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, element))
            {
                return true;
            }
        }

        return false;
    }
}
