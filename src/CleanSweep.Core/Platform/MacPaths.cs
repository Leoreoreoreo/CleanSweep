namespace CleanSweep.Core.Platform;

/// <summary>macOS (and Linux fallback) directory layout.</summary>
public sealed class MacPaths : IPlatformPaths
{
    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string Home => HomeDirectory;

    public IEnumerable<string> TempDirectories => Clean(new[]
    {
        Path.GetTempPath(),          // $TMPDIR
        "/tmp",
        "/private/var/tmp",
    });

    public IEnumerable<string> CacheDirectories => Clean(new[]
    {
        Path.Combine(Home, "Library", "Caches"),
        Path.Combine(Home, ".cache"),
    });

    public IEnumerable<string> LogDirectories => Clean(new[]
    {
        Path.Combine(Home, "Library", "Logs"),
    });

    public IEnumerable<string> TrashDirectories => Clean(new[]
    {
        Path.Combine(Home, ".Trash"),
    });

    // On macOS browser caches live under ~/Library/Caches, which AppCache already
    // covers. Kept empty here to avoid double-counting the same bytes.
    public IEnumerable<string> BrowserCacheDirectories => Array.Empty<string>();

    public IEnumerable<string> PackageCacheDirectories => Clean(new[]
    {
        Path.Combine(Home, ".npm", "_cacache"),
        Path.Combine(Home, "Library", "Caches", "pip"),
        Path.Combine(Home, ".cache", "pip"),
        Path.Combine(Home, "Library", "Caches", "Yarn"),
    });

    public IEnumerable<string> DevSearchRoots => Clean(new[]
    {
        Path.Combine(Home, "Desktop"),
        Path.Combine(Home, "Documents"),
        Path.Combine(Home, "Projects"),
        Path.Combine(Home, "code"),
        Path.Combine(Home, "dev"),
    });

    public IReadOnlyCollection<string> ProtectedPaths => Clean(new[]
    {
        "/", "/System", "/Library", "/Applications", "/usr", "/bin", "/sbin", "/etc", "/private", "/var",
        Home,
        Path.Combine(Home, "Documents"),
        Path.Combine(Home, "Desktop"),
        Path.Combine(Home, "Pictures"),
        Path.Combine(Home, "Movies"),
        Path.Combine(Home, "Music"),
        Path.Combine(Home, "Library", "Mobile Documents"),  // iCloud Drive
        Path.Combine(Home, "Library", "Keychains"),
    }).ToArray();

    private static IEnumerable<string> Clean(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length > 1).Distinct();
}
