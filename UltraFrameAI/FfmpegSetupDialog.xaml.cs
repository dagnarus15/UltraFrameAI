using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public enum FfmpegSetupChoice
{
    None,
    CloseApp,
    Download,
    ChooseFolder
}

public partial class FfmpegSetupDialog : Window
{
    public FfmpegSetupDialog()
    {
        InitializeComponent();
        DataContext = this;
        DialogTitle = LocalizedStrings.Get("FfmpegSetupTitle");
        DialogMessage = LocalizedStrings.Get("FfmpegSetupBody");
        DownloadText = LocalizedStrings.Get("FfmpegSetupDownload");
        ChooseFolderText = LocalizedStrings.Get("FfmpegSetupChooseFolder");
        CloseAppText = LocalizedStrings.Get("FfmpegSetupCloseApp");
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    public string DownloadText { get; }

    public string ChooseFolderText { get; }

    public string CloseAppText { get; }

    public FfmpegSetupChoice Choice { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        if (Choice == FfmpegSetupChoice.None)
        {
            Choice = FfmpegSetupChoice.CloseApp;
        }

        base.OnClosed(e);
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.CloseApp;
        Close();
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.Download;
        Close();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.ChooseFolder;
        Close();
    }
}
