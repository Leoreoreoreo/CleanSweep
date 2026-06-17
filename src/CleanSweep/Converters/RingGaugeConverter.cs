using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CleanSweep.Converters;

/// <summary>Turns a fraction (0..1) into a clockwise ring arc starting at 12 o'clock.</summary>
public sealed class RingGaugeConverter : IValueConverter
{
    private const double R = 62;   // radius
    private const double C = 75;   // centre (fits a 150x150 box)

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double frac = value is double d ? d : 0;
        var geometry = new StreamGeometry();
        if (frac <= 0) return geometry; // empty

        frac = Math.Min(frac, 0.9999);
        double angle = frac * 2 * Math.PI;
        var start = new Point(C, C - R);
        var end = new Point(C + R * Math.Sin(angle), C - R * Math.Cos(angle));

        using var ctx = geometry.Open();
        ctx.BeginFigure(start, false);
        ctx.ArcTo(end, new Size(R, R), 0, isLargeArc: frac > 0.5, SweepDirection.Clockwise);
        return geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
