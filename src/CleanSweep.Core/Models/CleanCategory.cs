namespace CleanSweep.Core.Models;

/// <summary>The kinds of reclaimable items CleanSweep knows how to find.</summary>
public enum CleanCategory
{
    TempFiles,
    AppCache,
    Logs,
    Trash,
    BrowserCache,
    DevJunk,
    PackageCache,
    LargeFiles,
    Duplicates
}

public static class CleanCategoryExtensions
{
    public static string DisplayName(this CleanCategory c) => c switch
    {
        CleanCategory.TempFiles    => "Temporary Files",
        CleanCategory.AppCache     => "Application Caches",
        CleanCategory.Logs         => "Log Files",
        CleanCategory.Trash        => "Trash / Recycle Bin",
        CleanCategory.BrowserCache => "Browser Caches",
        CleanCategory.DevJunk      => "Developer Junk",
        CleanCategory.PackageCache => "Package Caches",
        CleanCategory.LargeFiles   => "Large & Old Files",
        CleanCategory.Duplicates   => "Duplicate Files",
        _ => c.ToString()
    };

    public static string Glyph(this CleanCategory c) => c switch
    {
        CleanCategory.TempFiles    => "\U0001F9F9", // broom
        CleanCategory.AppCache     => "\U0001F4E6", // package
        CleanCategory.Logs         => "\U0001F4C4", // page
        CleanCategory.Trash        => "\U0001F5D1", // wastebasket
        CleanCategory.BrowserCache => "\U0001F310", // globe
        CleanCategory.DevJunk      => "\U0001F6E0", // hammer+wrench
        CleanCategory.PackageCache => "\U0001F4DA", // books
        CleanCategory.LargeFiles   => "\U0001F4C2", // open folder
        CleanCategory.Duplicates   => "\U0001F46F", // twins / duplicate
        _ => "•"
    };

    public static string Description(this CleanCategory c) => c switch
    {
        CleanCategory.TempFiles    => "Temporary files left behind by the system and apps.",
        CleanCategory.AppCache     => "Cached data apps can rebuild on demand.",
        CleanCategory.Logs         => "Diagnostic logs that are safe to clear.",
        CleanCategory.Trash        => "Items waiting in the trash / recycle bin.",
        CleanCategory.BrowserCache => "Cached web content from your browsers.",
        CleanCategory.DevJunk      => "Build output and dependency folders (node_modules, __pycache__, bin/obj…).",
        CleanCategory.PackageCache => "Download caches for npm, pip, NuGet and friends.",
        CleanCategory.LargeFiles   => "Big files you may have forgotten about (review before deleting).",
        CleanCategory.Duplicates   => "Identical files found in more than one place (keep one, remove the rest).",
        _ => string.Empty
    };

    /// <summary>Whether items in this category should be pre-checked after a scan.
    /// User-data oriented categories are never auto-selected.</summary>
    public static bool AutoSelect(this CleanCategory c) => c switch
    {
        CleanCategory.LargeFiles => false,
        CleanCategory.DevJunk    => false,
        CleanCategory.Duplicates => false,
        _ => true
    };
}
