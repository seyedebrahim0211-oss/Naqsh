using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Naqsh.Localization;

namespace Naqsh.App.Localization;

/// <summary>
/// XAML markup extension for localized text: <c>Text="{loc:Loc Monitor_Preview}"</c>. Returns a
/// binding to <see cref="LocalizationService"/>'s indexer, so text updates when the language
/// changes at runtime.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>Binds FlowDirection to the current language (RTL for Persian).</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocFlowExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(LocalizationService.IsRightToLeft))
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            Converter = RtlToFlowDirectionConverter.Instance,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

public sealed class RtlToFlowDirectionConverter : IValueConverter
{
    public static RtlToFlowDirectionConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
