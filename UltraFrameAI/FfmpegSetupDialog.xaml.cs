using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public enum FfmpegSetupChoice
{
    None,
    Download,
    ChooseFolder,
    ContinueWithout
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
        ContinueWithoutText = LocalizedStrings.Get("FfmpegSetupContinueWithout");
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    public string DownloadText { get; }

    public string ChooseFolderText { get; }

    public string ContinueWithoutText { get; }

    public FfmpegSetupChoice Choice { get; private set; }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.Download;
        DialogResult = true;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.ChooseFolder;
        DialogResult = true;
    }

    private void ContinueWithout_Click(object sender, RoutedEventArgs e)
    {
        Choice = FfmpegSetupChoice.ContinueWithout;
        DialogResult = false;
    }
}
