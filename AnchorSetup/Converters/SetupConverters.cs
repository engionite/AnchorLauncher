using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AnchorSetup.ViewModels;

namespace AnchorSetup.Converters;

/// <summary>Visible when the bound <see cref="WizardStep"/> equals the step named in ConverterParameter.</summary>
public sealed class StepVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? param, CultureInfo c)
        => value is WizardStep s && param is string name &&
           string.Equals(s.ToString(), name, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>bool → Visibility. ConverterParameter="invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? param, CultureInfo c)
    {
        var b = value is bool x && x;
        if (param is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
