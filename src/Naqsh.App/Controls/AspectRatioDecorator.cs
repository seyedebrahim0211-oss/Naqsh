using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Naqsh.Localization;

namespace Naqsh.App.Controls;

/// <summary>Lays out its child at the largest size with the given aspect ratio, centred.</summary>
public sealed class AspectRatioDecorator : Decorator
{
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio),
        typeof(double),
        typeof(AspectRatioDecorator),
        new FrameworkPropertyMetadata(16.0 / 9.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var size = Fit(constraint);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = Fit(arrangeSize);
        var x = (arrangeSize.Width - size.Width) / 2;
        var y = (arrangeSize.Height - size.Height) / 2;
        Child?.Arrange(new Rect(new Point(x, y), size));
        return arrangeSize;
    }

    private Size Fit(Size available)
    {
        var ratio = AspectRatio > 0 ? AspectRatio : 16.0 / 9.0;
        var w = double.IsInfinity(available.Width) ? 640 : available.Width;
        var h = double.IsInfinity(available.Height) ? w / ratio : available.Height;
        return w / h > ratio ? new Size(h * ratio, h) : new Size(w, w / ratio);
    }
}

/// <summary>Milliseconds → localized seconds text ("1.5" → "۱٫۵" in Persian digits).</summary>
public sealed class MsToSecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ms = value is int i ? i : 0;
        var text = (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);
        return LocalizationService.Instance.Digits(text);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound value equals the parameter (for chip selection).</summary>
public sealed class EqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visibility from a bool, inverted.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapsed when the string is null or empty.</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
