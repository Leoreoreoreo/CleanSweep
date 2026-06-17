using CleanSweep.Core.Models;

namespace CleanSweep.Core.Cleaning;

/// <summary>Finds individually large files (default ≥100 MB) under the dev roots and Downloads.</summary>
public sealed class LargeFilesModule : ICleanupModule
{
    public string Name => CleanCategory.LargeFiles.DisplayName();
    public CleanCategory Category => CleanCategory.LargeFiles;

    private const long ThresholdBytes = 100L * 1024 * 1024; // 100 MB
    private const int MaxDepth = 8;
    private const int MaxResults = 200;

    public CategoryResult Scan(ScanContext ctx)
    {
        var found = new List<CleanItem>();
        var roots = ctx.Paths.DevSearchRoots
            .Append(Path.Combine(ctx.Paths.HomeDirectory, "Downloads"))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            ctx.Progress?.Report($"Scanning {root} for large files");
            Walk(root, 0, ctx, found);
        }

        found.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        var result = new CategoryResult { Category = Category };
        result.Items.AddRange(found.Take(MaxResults));
        return result;
    }

    private void Walk(string dir, int depth, ScanContext ctx, List<CleanItem> found)
    {
        ctx.Cancellation.ThrowIfCancellationRequested();
        if (depth > MaxDepth) return;
        if (ctx.IsExcluded(dir)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                ctx.Cancellation.ThrowIfCancellationRequested();
                long size;
                DateTime lm;
                try { var fi = new FileInfo(file); size = fi.Length; lm = fi.LastWriteTimeUtc; }
                catch { continue; }

                if (size >= ThresholdBytes)
                {
                    found.Add(new CleanItem
                    {
                        Path = file,
                        DisplayName = Path.GetFileName(file),
                        SizeBytes = size,
                        Category = Category,
                        IsDirectory = false,
                        LastModified = lm,
                        DefaultSelected = false
                    });
                }
            }
        }
        catch { /* unreadable directory */ }

        List<string> subs;
        try { subs = Directory.EnumerateDirectories(dir).ToList(); }
        catch { return; }

        foreach (var sub in subs)
        {
            try { if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue; }
            catch { continue; }
            Walk(sub, depth + 1, ctx, found);
        }
    }
}
