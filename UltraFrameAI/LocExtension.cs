using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    private static readonly LocalizationProxy Proxy = new();

    public string Key { get; set; } = string.Empty;

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Proxy,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}

internal sealed class LocalizationProxy : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationProxy()
    {
        LocalizedStrings.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string this[string key] => LocalizedStrings.Get(key);
}
