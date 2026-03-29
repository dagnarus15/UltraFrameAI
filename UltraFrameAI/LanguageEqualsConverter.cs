using System.Globalization;
using System.Windows.Data;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class LanguageEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not UiLanguage current)
        {
            return false;
        }

        if (parameter is UiLanguage expectedLanguage)
        {
            return current == expectedLanguage;
        }

        if (parameter is string expectedText && Enum.TryParse(expectedText, true, out UiLanguage parsed))
        {
            return current == parsed;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
        {
            if (parameter is UiLanguage expected)
            {
                return expected;
            }

            if (parameter is string expectedText && Enum.TryParse(expectedText, true, out UiLanguage parsed))
            {
                return parsed;
            }
        }

        return System.Windows.Data.Binding.DoNothing;
    }
}
