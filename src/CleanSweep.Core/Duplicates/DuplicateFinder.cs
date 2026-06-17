using System.Security.Cryptography;

namespace CleanSweep.Core.Duplicates;

/// <summary>
/// Finds duplicate files in three escalating passes so we hash as little as
/// possible: group by exact size, then by a cheap partial hash (first 64&#160;KB),
/// and only fully hash files whose partial hashes already collide.
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    private const int PartialHashBytes = 64 * 1024;
    private const int BufferSize = 1 << 16;

    public Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IEnumerable<string> roots, DuplicateScanOptions options,
        IProgress<string>? progress, CancellationToken ct)
        => Task.Run(() => Find(roots, options, progress, ct), ct);

    private IReadOnlyList<DuplicateGroup> Find(
        IEnumerable<string> roots, DuplicateScanOptions options,
        IProgress<string>? progress, CancellationToken ct)
    {
        // Pass 1 — bucket candidate files by exact size.
        var bySize = new Dictionary<long, List<string>>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            if (!Directory.Exists(root)) continue;
            progress?.Report($"Scanning {root} for duplicates");
            Walk(root, 0, options, ct, (path, size) =>
            {
                if (size < options.MinFileSizeBytes) return;
                if (!bySize.TryGetValue(size, out var list)) bySize[size] = list = new();
                list.Add(path);
            });
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (size, files) in bySize)
        {
            ct.ThrowIfCancellationRequested();
            if (files.Count < 2) continue;

            // Pass 2 — split each size bucket by partial hash.
            foreach (var partialBucket in BucketByHash(files, PartialHashBytes, ct).Values)
            {
                if (partialBucket.Count < 2) continue;

                // A file no bigger than the partial window is already fully hashed,
                // so the partial bucket is a confirmed content group — skip pass 3.
                if (size <= PartialHashBytes)
                {
                    progress?.Report($"Found {partialBucket.Count} copies of a {size}-byte file");
                    groups.Add(BuildGroup(HashFile(partialBucket[0], long.MaxValue, ct) ?? "?", size, partialBucket));
                    continue;
                }

                // Pass 3 — fully hash only the partial-hash collisions.
                foreach (var (hash, dupes) in BucketByHash(partialBucket, long.MaxValue, ct))
                {
                    if (dupes.Count < 2) continue;
                    progress?.Report($"Found {dupes.Count} identical files ({hash[..8]}…)");
                    groups.Add(BuildGroup(hash, size, dupes));
                }
            }
        }

        groups.Sort((a, b) => b.ReclaimableBytes.CompareTo(a.ReclaimableBytes));
        return groups;
    }

    private static Dictionary<string, List<string>> BucketByHash(
        IEnumerable<string> files, long maxBytes, CancellationToken ct)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var hash = HashFile(file, maxBytes, ct);
            if (hash is null) continue; // unreadable -> skip silently
            if (!buckets.TryGetValue(hash, out var list)) buckets[hash] = list = new();
            list.Add(file);
        }
        return buckets;
    }

    private static DuplicateGroup BuildGroup(string hash, long size, List<string> paths)
        => new()
        {
            ContentHash = hash,
            FileSizeBytes = size,
            Files = paths.Select(ToFile).ToList()
        };

    private static DuplicateFile ToFile(string path)
    {
        DateTime? lm = null;
        long size = 0;
        try { var fi = new FileInfo(path); lm = fi.LastWriteTime; size = fi.Length; } catch { }
        return new DuplicateFile { Path = path, Name = Path.GetFileName(path), SizeBytes = size, LastModified = lm };
    }

    /// <summary>SHA-256 over up to <paramref name="maxBytes"/> of the file; null if unreadable.</summary>
    private static string? HashFile(string path, long maxBytes, CancellationToken ct)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                          BufferSize, FileOptions.SequentialScan);
            var buffer = new byte[BufferSize];
            long remaining = maxBytes;
            int read;
            while (remaining > 0 &&
                   (read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private void Walk(string dir, int depth, DuplicateScanOptions opt,
                      CancellationToken ct, Action<string, long> onFile)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > opt.MaxDepth) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                long size;
                try
                {
                    var fi = new FileInfo(file);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue; // skip symlinks
                    size = fi.Length;
                }
                catch { continue; }
                onFile(file, size);
            }
        }
        catch { /* unreadable directory — skip its files */ }

        List<string> subs;
        try { subs = Directory.EnumerateDirectories(dir).ToList(); }
        catch { return; }

        foreach (var sub in subs)
        {
            if (opt.SkipDirectoryNames.Contains(Path.GetFileName(sub))) continue;
            try { if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue; }
            catch { continue; }
            Walk(sub, depth + 1, opt, ct, onFile);
        }
    }
}
