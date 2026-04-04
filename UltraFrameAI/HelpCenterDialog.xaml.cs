using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class HelpCenterDialog : Window, INotifyPropertyChanged
{
    public HelpCenterDialog(HelpCenterTab initialTab = HelpCenterTab.HowTo)
    {
        InitializeComponent();
        SelectedTab = initialTab;

        foreach (var entry in BuildFaqEntries())
        {
            FaqEntries.Add(entry);
        }

        foreach (var entry in DonationSupportInfo.GetEntries())
        {
            DonationEntries.Add(entry);
        }

        foreach (var entry in HelpCenterInfo.GetVersions())
        {
            Versions.Add(entry);
        }

        foreach (var entry in HelpCenterInfo.GetContactLinks())
        {
            ContactLinks.Add(entry);
        }

        foreach (var entry in HelpCenterInfo.GetSourceLinks())
        {
            SourceLinks.Add(entry);
        }

        DataContext = this;
    }

    public HelpCenterTab SelectedTab { get; private set; }

    public bool IsHowToVisible => SelectedTab == HelpCenterTab.HowTo;

    public bool IsFaqVisible => SelectedTab == HelpCenterTab.Faq;

    public bool IsLinksVisible => SelectedTab == HelpCenterTab.Links;

    public bool IsSupportVisible => SelectedTab == HelpCenterTab.Support;

    public ObservableCollection<HelpFaqEntry> FaqEntries { get; } = new();

    public ObservableCollection<DonationSupportEntry> DonationEntries { get; } = new();

    public ObservableCollection<HelpVersionEntry> Versions { get; } = new();

    public ObservableCollection<HelpLinkEntry> ContactLinks { get; } = new();

    public ObservableCollection<HelpLinkEntry> SourceLinks { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void HowTo_Click(object sender, RoutedEventArgs e) => SetTab(HelpCenterTab.HowTo);

    private void Faq_Click(object sender, RoutedEventArgs e) => SetTab(HelpCenterTab.Faq);

    private void Links_Click(object sender, RoutedEventArgs e) => SetTab(HelpCenterTab.Links);

    private void Donate_Click(object sender, RoutedEventArgs e) => SetTab(HelpCenterTab.Support);

    private void SetTab(HelpCenterTab tab)
    {
        if (SelectedTab == tab)
        {
            return;
        }

        SelectedTab = tab;
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(IsHowToVisible));
        OnPropertyChanged(nameof(IsFaqVisible));
        OnPropertyChanged(nameof(IsLinksVisible));
        OnPropertyChanged(nameof(IsSupportVisible));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static IReadOnlyList<HelpFaqEntry> BuildFaqEntries()
    {
        return new[]
        {
            new HelpFaqEntry(LocalizedStrings.Get("HelpFaqQuestion1"), LocalizedStrings.Get("HelpFaqAnswer1")),
            new HelpFaqEntry(LocalizedStrings.Get("HelpFaqQuestion2"), LocalizedStrings.Get("HelpFaqAnswer2")),
            new HelpFaqEntry(LocalizedStrings.Get("HelpFaqQuestion3"), LocalizedStrings.Get("HelpFaqAnswer3")),
            new HelpFaqEntry(LocalizedStrings.Get("HelpFaqQuestion4"), LocalizedStrings.Get("HelpFaqAnswer4")),
            new HelpFaqEntry(LocalizedStrings.Get("HelpFaqQuestion5"), LocalizedStrings.Get("HelpFaqAnswer5"))
        };
    }
}
