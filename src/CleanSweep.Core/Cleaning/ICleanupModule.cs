using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Cleaning;

/// <summary>Shared state handed to every module during a scan.</summary>
public sealed class ScanContext
{
    public required IPlatformPaths Paths { get; init; }
    public required FileSystemScanner Scanner { get; init; }
    public IProgress<string>? Progress { get; init; }
    public CancellationToken Cancellation { get; init; }

    private IReadOnlyList<string> _excludedPaths = Array.Empty<string>();
    private string[] _excluded = Array.Empty<string>();

    /// <summary>Folders the user has chosen never to scan. Normalized once, on construction.</summary>
    public IReadOnlyList<string> ExcludedPaths
    {
        get => _excludedPaths;
        init { _excludedPaths = value ?? Array.Empty<string>(); _excluded = PathScope.Normalize(_excludedPaths); }
    }

    /// <summary>True if <paramref name="path"/> is, or sits under, an excluded folder. Thread-safe (read-only).</summary>
    public bool IsExcluded(string path) => _excluded.Length != 0 && PathScope.IsUnderAny(path, _excluded);
}

/// <summary>A unit of scanning logic for one <see cref="CleanCategory"/>.</summary>
public interface ICleanupModule
{
    string Name { get; }
    CleanCategory Category { get; }
    CategoryResult Scan(ScanContext ctx);
}

/// <summary>Magic paths that need special (non-filesystem) delete handling.</summary>
public static class Sentinels
{
    public const string RecycleBin = "::CleanSweep:RecycleBin::";
}
