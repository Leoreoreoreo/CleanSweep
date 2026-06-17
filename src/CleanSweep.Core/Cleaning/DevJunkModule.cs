using CleanSweep.Core.Models;

namespace CleanSweep.Core.Cleaning;

/// <summary>Finds build-output and dependency directories under the dev roots.</summary>
public sealed class DevJunkModule : ICleanupModule
{
    public string Name => CleanCategory.DevJunk.DisplayName();
    public CleanCategory Category => CleanCategory.DevJunk;

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".pytest_cache", "bin", "obj",
        "target", ".gradle", "build", ".next", ".turbo", ".parcel-cache"
    };

    private const int MaxDepth = 7;

    public CategoryResult Scan(ScanContext ctx)
    {
        var result = new CategoryResult { Category = Category };
        foreach (var root in ctx.Paths.DevSearchRoots.Distinct())
        {
            if (!Directory.Exists(root)) continue;
            ctx.Progress?.Report($"Searching {root} for build junk");
            Walk(root, 0, ctx, result);
        }
        result.Items.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return result;
    }

    private void Walk(string dir, int depth, ScanContext ctx, CategoryResult result)
    {
        ctx.Cancellation.ThrowIfCancellationRequested();
        if (depth > MaxDepth) return;

        List<string> subs;
        try { subs = Directory.EnumerateDirectories(dir).ToList(); }
        catch { return; }

        foreach (var sub in subs)
        {
            ctx.Cancellation.ThrowIfCancellationRequested();
            try { if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue; }
            catch { continue; }

            var name = Path.GetFileName(sub);
            if (JunkNames.Contains(name))
            {
                long size = ctx.Scanner.GetDirectorySize(sub, ctx.Cancellation);
                if (size > 0)
                {
                    DateTime? lm = null;
                    try { lm = Directory.GetLastWriteTimeUtc(sub); } catch { }
                    result.Items.Add(new CleanItem
                    {
                        Path = sub,
                        DisplayName = $"{name}  —  in {ParentName(sub)}",
                        SizeBytes = size,
                        Category = Category,
                        IsDirectory = true,
                        LastModified = lm,
                        DefaultSelected = false
                    });
                }
                // Never descend into a junk directory.
            }
            else
            {
                Walk(sub, depth + 1, ctx, result);
            }
        }
    }

    private static string ParentName(string path)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent) ? "" : Path.GetFileName(parent);
        }
        catch { return ""; }
    }
}
