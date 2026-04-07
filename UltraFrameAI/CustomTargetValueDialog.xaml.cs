using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public partial class CustomTargetValueDialog : Window
{
    private static readonly Regex DigitsOnlyRegex = new("^[0-9]+$");

    public CustomTargetValueDialog(int initialHeight)
    {
        InitializeComponent();
        SelectedHeight = Math.Max(120, initialHeight);
        Loaded += (_, _) =>
        {
            ValueTextBox.Text = SelectedHeight.ToString();
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public int SelectedHeight { get; private set; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ValueTextBox.Text.Trim(), out var parsed) || parsed < 120)
        {
            var popup = new PopupMessageDialog(
                LocalizedStrings.CustomTargetDialogTitle,
                LocalizedStrings.CustomTargetDialogValidation)
            {
                Owner = this
            };
            popup.ShowDialog();
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
            return;
        }

        SelectedHeight = parsed;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ValueTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
    }
}
