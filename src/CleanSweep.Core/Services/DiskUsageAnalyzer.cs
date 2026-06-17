namespace CleanSweep.Core.Services;

/// <summary>One immediate child of an analyzed folder, with its recursive size.</summary>
public sealed record UsageEntry(string Name, string Path, long Bytes, bool IsDirectory);

/// <summary>
/// Measures where space goes inside a folder: the recursive size of each immediate
/// subfolder, plus the loose files that sit directly in the folder.
/// </summary>
public sealed class DiskUsageAnalyzer
{
    private readonly FileSystemScanner _scanner = new();

    public Task<IReadOnlyList<UsageEntry>> AnalyzeAsync(string root, IProgress<string>? progress, CancellationToken ct)
        => Task.Run<IReadOnlyList<UsageEntry>>(() =>
        {
            var entries = new List<UsageEntry>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return entries;

            long looseFiles = 0;
            foreach (var child in _scanner.EnumerateImmediateChildren(root))
            {
                ct.ThrowIfCancellationRequested();
                if (child.IsDirectory)
                {
                    // Don't follow junctions/symlinks - avoids double-counting redirected trees
                    // (e.g. the legacy "Application Data" -> AppData\Roaming junction on Windows).
                    try { if ((new DirectoryInfo(child.FullPath).Attributes & FileAttributes.ReparsePoint) != 0) continue; }
                    catch { continue; }

                    progress?.Report($"Measuring {child.Name}");
                    long size = _scanner.GetDirectorySize(child.FullPath, ct);
                    if (size > 0) entries.Add(new UsageEntry(child.Name, child.FullPath, size, IsDirectory: true));
                }
                else
                {
                    looseFiles += _scanner.GetFileSize(child.FullPath);
                }
            }

            if (looseFiles > 0)
                entries.Add(new UsageEntry("Loose files", root, looseFiles, IsDirectory: false));

            entries.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            return entries;
        }, ct);
}
