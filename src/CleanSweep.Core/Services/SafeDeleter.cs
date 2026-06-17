using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Services;

public sealed class DeleteOutcome
{
    public long FreedBytes { get; set; }
    public int DeletedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Deletes items, refusing any path that is - or contains - a protected
/// location. This is the last line of defence regardless of what a scan returns.
/// </summary>
public sealed class SafeDeleter
{
    private readonly List<string> _protected;
    private readonly StringComparison _cmp;
    private readonly char _sep = Path.DirectorySeparatorChar;

    public SafeDeleter(IPlatformPaths paths)
    {
        _cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _protected = paths.ProtectedPaths.Select(Normalize).ToList();
    }

    private string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        return path.TrimEnd(_sep, '/');
    }

    /// <summary>True when deleting this path would be unsafe.</summary>
    public bool IsProtected(string path)
    {
        // Empty/whitespace paths are never safe to delete. (On Windows GetFullPath
        // throws on whitespace; on Unix it doesn't, so guard before normalizing.)
        if (string.IsNullOrWhiteSpace(path)) return true;

        var p = Normalize(path);
        if (string.IsNullOrEmpty(p) || p.Length <= 3) return true;

        var root = Normalize(Path.GetPathRoot(p) ?? string.Empty);
        if (!string.IsNullOrEmpty(root) && p.Equals(root, _cmp)) return true;

        foreach (var prot in _protected)
        {
            if (p.Equals(prot, _cmp)) return true;             // exactly a protected dir
            if (prot.StartsWith(p + _sep, _cmp)) return true;  // p is an ancestor of a protected dir
            if (prot.StartsWith(p + '/', _cmp)) return true;
        }
        return false;
    }

    public DeleteOutcome Delete(IEnumerable<CleanItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        var outcome = new DeleteOutcome();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (IsProtected(item.Path))
            {
                outcome.SkippedCount++;
                outcome.Errors.Add($"Skipped protected path: {item.Path}");
                continue;
            }

            try
            {
                progress?.Report($"Removing {item.DisplayName}");
                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.Path)) Directory.Delete(item.Path, recursive: true);
                }
                else
                {
                    if (File.Exists(item.Path)) File.Delete(item.Path);
                }
                outcome.FreedBytes += item.SizeBytes;
                outcome.DeletedCount++;
            }
            catch (Exception ex)
            {
                outcome.SkippedCount++;
                outcome.Errors.Add($"{item.DisplayName}: {ex.Message}");
            }
        }
        return outcome;
    }
}
