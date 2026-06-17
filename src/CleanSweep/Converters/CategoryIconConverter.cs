using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using CleanSweep.Core.Models;

namespace CleanSweep.Converters;

/// <summary>Maps a <see cref="CleanCategory"/> to a vector icon geometry resource.</summary>
public sealed class CategoryIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CleanCategory c) return null;
        var key = c switch
        {
            CleanCategory.TempFiles    => "IconClock",
            CleanCategory.AppCache     => "IconBox",
            CleanCategory.Logs         => "IconDocument",
            CleanCategory.Trash        => "IconTrash",
            CleanCategory.BrowserCache => "IconGlobe",
            CleanCategory.DevJunk      => "IconWrench",
            CleanCategory.PackageCache => "IconBox",
            CleanCategory.LargeFiles   => "IconFolder",
            CleanCategory.Duplicates   => "IconCopy",
            _ => "IconDocument"
        };
        return Application.Current is { } app && app.TryGetResource(key, null, out var res) ? res : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
