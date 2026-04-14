using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public enum FfmpegInstallLocationChoice
{
    None,
    CloseApp,
    InstallHere,
    ChooseFolder
}

public partial class FfmpegInstallLocationDialog : Window
{
    public FfmpegInstallLocationDialog()
    {
        InitializeComponent();
        DataContext = this;
        DialogTitle = LocalizedStrings.Get("FfmpegInstallLocationTitle");
        DialogMessage = LocalizedStrings.Get("FfmpegInstallLocationBody");
        InstallHereText = LocalizedStrings.Get("FfmpegInstallLocationHere");
        ChooseFolderText = LocalizedStrings.Get("FfmpegInstallLocationChoose");
        CloseAppText = LocalizedStrings.Get("FfmpegSetupCloseApp");
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    public string InstallHereText { get; }

    public string ChooseFolderText { get; }

    public string CloseAppText { get; }

    public FfmpegInstallLocationChoice Choice { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        if (Choice == FfmpegInstallLocationChoice.None)
        {
            Choice = FfmpegInstallLocationChoice.CloseApp;
        }

        base.OnClosed(e);
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegInstallLocationChoice.CloseApp;
        Close();
    }

    private void InstallHere_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegInstallLocationChoice.InstallHere;
        Close();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegInstallLocationChoice.ChooseFolder;
        Close();
    }
}
