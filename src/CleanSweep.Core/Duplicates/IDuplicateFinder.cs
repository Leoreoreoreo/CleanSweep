namespace CleanSweep.Core.Duplicates;

/// <summary>Tunables that bound a duplicate scan so it stays fast and safe.</summary>
public sealed class DuplicateScanOptions
{
    /// <summary>Files smaller than this are ignored (tiny dupes aren't worth it).</summary>
    public long MinFileSizeBytes { get; init; } = 1024 * 1024; // 1 MB

    /// <summary>How deep below each root to descend.</summary>
    public int MaxDepth { get; init; } = 10;

    /// <summary>Directory names skipped wholesale - huge/system/rebuildable trees.</summary>
    public IReadOnlySet<string> SkipDirectoryNames { get; init; } = DefaultSkipDirs;

    public static readonly IReadOnlySet<string> DefaultSkipDirs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".svn", ".hg", "obj", "bin", "target", "build",
            ".gradle", ".cache", ".next", ".turbo", "Library", "AppData",
            "$Recycle.Bin", "Windows", "Program Files", "Program Files (x86)",
            "System Volume Information", "__pycache__",
        };
}

/// <summary>Finds groups of byte-for-byte identical files under a set of roots.</summary>
public interface IDuplicateFinder
{
    Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IEnumerable<string> roots,
        DuplicateScanOptions options,
        IProgress<string>? progress,
        CancellationToken ct);
}
