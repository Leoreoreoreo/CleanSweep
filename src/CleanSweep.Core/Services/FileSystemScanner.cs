namespace CleanSweep.Core.Services;

/// <summary>A file or directory found while scanning.</summary>
public sealed record FileEntry(string FullPath, string Name, bool IsDirectory, DateTime? LastModified);

/// <summary>
/// Robust filesystem traversal: every access is guarded so locked or
/// permission-denied entries are skipped instead of aborting a scan.
/// </summary>
public sealed class FileSystemScanner
{
    public IEnumerable<FileEntry> EnumerateImmediateChildren(string dir)
    {
        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var p in entries)
        {
            FileEntry? entry = null;
            try
            {
                bool isDir = Directory.Exists(p);
                DateTime lm = isDir ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p);
                entry = new FileEntry(p, Path.GetFileName(p), isDir, lm);
            }
            catch
            {
                // unreadable entry - skip
            }
            if (entry is not null) yield return entry;
        }
    }

    public long GetSize(FileEntry entry, CancellationToken ct)
        => entry.IsDirectory ? GetDirectorySize(entry.FullPath, ct) : GetFileSize(entry.FullPath);

    public long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>Total bytes under a directory. Symlinks/junctions are not followed.</summary>
    public long GetDirectorySize(string dir, CancellationToken ct)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(dir);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        var info = new DirectoryInfo(sub);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    stack.Push(sub);
                }
            }
            catch
            {
                // unreadable directory - skip its subtree
            }
        }
        return total;
    }
}
