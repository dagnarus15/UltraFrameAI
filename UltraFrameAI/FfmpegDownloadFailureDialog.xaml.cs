using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public enum FfmpegDownloadFailureAction
{
    None,
    CloseApp,
    Retry,
    ChooseFolder,
    OpenDownloadPage
}

public enum FfmpegDownloadFailureKind
{
    NetworkError,
    SourceUnavailable
}

public partial class FfmpegDownloadFailureDialog : Window
{
    public FfmpegDownloadFailureDialog(FfmpegDownloadFailureKind kind)
    {
        InitializeComponent();
        DataContext = this;

        if (kind == FfmpegDownloadFailureKind.NetworkError)
        {
            DialogTitle = LocalizedStrings.Get("FfmpegDownloadNetworkTitle");
            DialogMessage = LocalizedStrings.Get("FfmpegDownloadNetworkBody");
            PrimaryText = LocalizedStrings.Get("FfmpegDownloadRetry");
            SecondaryText = LocalizedStrings.Get("FfmpegSetupChooseFolder");
        }
        else
        {
            DialogTitle = LocalizedStrings.Get("FfmpegDownloadUnavailableTitle");
            DialogMessage = LocalizedStrings.Get("FfmpegDownloadUnavailableBody");
            PrimaryText = LocalizedStrings.Get("FfmpegDownloadOpenPage");
            SecondaryText = LocalizedStrings.Get("FfmpegSetupChooseFolder");
        }

        Kind = kind;
        CloseAppText = LocalizedStrings.Get("FfmpegSetupCloseApp");
    }

    public FfmpegDownloadFailureKind Kind { get; }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    public string PrimaryText { get; }

    public string SecondaryText { get; }

    public string CloseAppText { get; }

    public FfmpegDownloadFailureAction Action { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        if (Action == FfmpegDownloadFailureAction.None)
        {
            Action = FfmpegDownloadFailureAction.CloseApp;
        }

        base.OnClosed(e);
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        Action = FfmpegDownloadFailureAction.CloseApp;
        Close();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Action = Kind == FfmpegDownloadFailureKind.NetworkError
            ? FfmpegDownloadFailureAction.Retry
            : FfmpegDownloadFailureAction.OpenDownloadPage;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Action = FfmpegDownloadFailureAction.ChooseFolder;
        Close();
    }
}
