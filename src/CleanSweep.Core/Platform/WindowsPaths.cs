namespace CleanSweep.Core.Platform;

/// <summary>Windows directory layout.</summary>
public sealed class WindowsPaths : IPlatformPaths
{
    private static string Env(string v) => Environment.GetEnvironmentVariable(v) ?? string.Empty;
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IEnumerable<string> TempDirectories => Clean(new[]
    {
        Path.GetTempPath(),
        Env("TEMP"),
        Env("TMP"),
        Path.Combine(Env("WINDIR"), "Temp"),
    });

    public IEnumerable<string> CacheDirectories => Clean(new[]
    {
        Path.Combine(Local, "Microsoft", "Windows", "INetCache"),
        Path.Combine(Local, "Microsoft", "Windows", "Explorer"),   // thumbnail cache
        Path.Combine(Local, "Microsoft", "Windows", "WebCache"),
        Path.Combine(Local, "D3DSCache"),
        Path.Combine(Local, "NVIDIA", "DXCache"),
    });

    public IEnumerable<string> LogDirectories => Clean(new[]
    {
        Path.Combine(Local, "CrashDumps"),
    });

    // Recycle Bin is handled through the Shell API in RecycleBinModule, not as a plain directory.
    public IEnumerable<string> TrashDirectories => Array.Empty<string>();

    public IEnumerable<string> BrowserCacheDirectories => Clean(new[]
    {
        Path.Combine(Local, "Google", "Chrome", "User Data", "Default", "Cache"),
        Path.Combine(Local, "Google", "Chrome", "User Data", "Default", "Code Cache"),
        Path.Combine(Local, "Google", "Chrome", "User Data", "Default", "GPUCache"),
        Path.Combine(Local, "Microsoft", "Edge", "User Data", "Default", "Cache"),
        Path.Combine(Local, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
        Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
    });

    // Pure download caches only - these refill harmlessly. Restore stores like
    // .nuget\packages are intentionally excluded to avoid surprising re-downloads.
    public IEnumerable<string> PackageCacheDirectories => Clean(new[]
    {
        Path.Combine(Local, "npm-cache"),
        Path.Combine(Local, "pip", "Cache"),
        Path.Combine(Local, "NuGet", "v3-cache"),
        Path.Combine(Local, "Yarn", "Cache"),
    });

    public IEnumerable<string> DevSearchRoots => Clean(new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(HomeDirectory, "source"),
        Path.Combine(HomeDirectory, "Projects"),
        Path.Combine(HomeDirectory, "repos"),
    });

    public IReadOnlyCollection<string> ProtectedPaths => Clean(new[]
    {
        Env("SystemDrive") + Path.DirectorySeparatorChar,
        Env("WINDIR"),
        Env("ProgramFiles"),
        Env("ProgramFiles(x86)"),
        Env("ProgramData"),
        Path.Combine(Env("SystemDrive") + Path.DirectorySeparatorChar.ToString(), "Users"),
        HomeDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
    }).ToArray();

    private static IEnumerable<string> Clean(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length > 3).Distinct();
}
