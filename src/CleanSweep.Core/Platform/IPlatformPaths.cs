namespace CleanSweep.Core.Platform;

/// <summary>
/// Supplies the OS-specific directories CleanSweep scans, plus the set of
/// paths that must never be deleted. One implementation per supported OS.
/// </summary>
public interface IPlatformPaths
{
    string HomeDirectory { get; }

    IEnumerable<string> TempDirectories { get; }
    IEnumerable<string> CacheDirectories { get; }
    IEnumerable<string> LogDirectories { get; }
    IEnumerable<string> TrashDirectories { get; }
    IEnumerable<string> BrowserCacheDirectories { get; }
    IEnumerable<string> PackageCacheDirectories { get; }

    /// <summary>Roots searched recursively for dev junk and large files.</summary>
    IEnumerable<string> DevSearchRoots { get; }

    /// <summary>Directories that must never be deleted (and whose ancestors are off-limits too).</summary>
    IReadOnlyCollection<string> ProtectedPaths { get; }
}

public static class PlatformPaths
{
    /// <summary>Returns the path provider for the current OS.</summary>
    public static IPlatformPaths Current { get; } = Create();

    private static IPlatformPaths Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPaths();
        if (OperatingSystem.IsMacOS())   return new MacPaths();
        // Linux falls back to the mac-style (XDG/unix) layout which is close enough.
        return new MacPaths();
    }
}
