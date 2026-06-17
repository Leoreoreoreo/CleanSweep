using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CleanSweep.Converters;

/// <summary>true -> 1.0, false -> 0.0. Used to cross-fade section pages.</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
