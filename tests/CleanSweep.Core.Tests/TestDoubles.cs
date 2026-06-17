using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Tests;

/// <summary>
/// A fully in-memory <see cref="IPlatformPaths"/> so engine tests are
/// deterministic and OS-independent. Every path points inside a temp tree the
/// test owns, so nothing on the real system is ever touched.
/// </summary>
public sealed class FakePlatformPaths : IPlatformPaths
{
    public string HomeDirectory { get; set; } = "";
    public List<string> Temp { get; } = new();
    public List<string> Cache { get; } = new();
    public List<string> Logs { get; } = new();
    public List<string> Trash { get; } = new();
    public List<string> Browser { get; } = new();
    public List<string> Package { get; } = new();
    public List<string> DevRoots { get; } = new();
    public List<string> Protected { get; } = new();

    public IEnumerable<string> TempDirectories => Temp;
    public IEnumerable<string> CacheDirectories => Cache;
    public IEnumerable<string> LogDirectories => Logs;
    public IEnumerable<string> TrashDirectories => Trash;
    public IEnumerable<string> BrowserCacheDirectories => Browser;
    public IEnumerable<string> PackageCacheDirectories => Package;
    public IEnumerable<string> DevSearchRoots => DevRoots;
    public IReadOnlyCollection<string> ProtectedPaths => Protected;
}

/// <summary>
/// A unique temporary directory tree, deleted on <see cref="Dispose"/>. Use the
/// helpers to synthesize the fake filesystem a test scans.
/// </summary>
public sealed class TempTree : IDisposable
{
    public string Root { get; }

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "cleansweep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Creates (and returns) a directory under the tree.</summary>
    public string Dir(params string[] parts)
    {
        var path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Creates a file of exactly <paramref name="sizeBytes"/> logical bytes.
    /// Uses SetLength so even "large" (≥100&#160;MB) files cost no real I/O.
    /// </summary>
    public string Write(string relativePath, long sizeBytes)
    {
        var path = Combine(relativePath.Split('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.SetLength(sizeBytes);
        return path;
    }

    private string Combine(string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = Root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
