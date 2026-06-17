using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Cleaning;

/// <summary>
/// Base for modules that simply list the immediate children of a set of
/// target directories, sizing each child so the user can pick what to remove.
/// </summary>
public abstract class DirectoryCleanupModule : ICleanupModule
{
    public abstract CleanCategory Category { get; }
    public string Name => Category.DisplayName();
    protected abstract IEnumerable<string> Targets(IPlatformPaths paths);

    public virtual CategoryResult Scan(ScanContext ctx)
    {
        var result = new CategoryResult { Category = Category };
        bool autoSelect = Category.AutoSelect();

        foreach (var dir in Targets(ctx.Paths).Distinct())
        {
            ctx.Cancellation.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) continue;
            ctx.Progress?.Report($"Scanning {dir}");

            foreach (var child in ctx.Scanner.EnumerateImmediateChildren(dir))
            {
                ctx.Cancellation.ThrowIfCancellationRequested();
                long size = ctx.Scanner.GetSize(child, ctx.Cancellation);
                if (size <= 0) continue;

                result.Items.Add(new CleanItem
                {
                    Path = child.FullPath,
                    DisplayName = child.Name,
                    SizeBytes = size,
                    Category = Category,
                    IsDirectory = child.IsDirectory,
                    LastModified = child.LastModified,
                    DefaultSelected = autoSelect
                });
            }
        }

        result.Items.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return result;
    }
}

public sealed class TempFilesModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.TempFiles;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.TempDirectories;
}

public sealed class AppCacheModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.AppCache;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.CacheDirectories;
}

public sealed class LogsModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.Logs;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.LogDirectories;
}

public sealed class BrowserCacheModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.BrowserCache;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.BrowserCacheDirectories;
}

public sealed class PackageCacheModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.PackageCache;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.PackageCacheDirectories;
}

/// <summary>macOS/Linux trash (~/.Trash). Windows uses <see cref="RecycleBinModule"/>.</summary>
public sealed class TrashDirModule : DirectoryCleanupModule
{
    public override CleanCategory Category => CleanCategory.Trash;
    protected override IEnumerable<string> Targets(IPlatformPaths p) => p.TrashDirectories;
}
