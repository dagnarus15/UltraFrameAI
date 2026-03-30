using System.ComponentModel;
using System.Windows;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public enum RenderCloseDecision
{
    KeepRendering,
    StopRendering,
}

public partial class RenderCloseDialog : Window
{
    private bool _allowClose;

    public RenderCloseDialog()
    {
        InitializeComponent();
        MessageText = LocalizedStrings.CloseDuringRenderMessage;
        DataContext = this;
    }

    public string MessageText { get; init; }

    public RenderCloseDecision Decision { get; private set; } = RenderCloseDecision.KeepRendering;

    private void Keep_Click(object sender, RoutedEventArgs e) => Finish(RenderCloseDecision.KeepRendering);

    private void Stop_Click(object sender, RoutedEventArgs e) => Finish(RenderCloseDecision.StopRendering);

    private void Finish(RenderCloseDecision decision)
    {
        Decision = decision;
        _allowClose = true;
        DialogResult = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
    }
}
