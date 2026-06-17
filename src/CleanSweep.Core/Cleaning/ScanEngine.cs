using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Cleaning;

/// <summary>Runs every cleanup module and performs the actual deletion.</summary>
public sealed class ScanEngine
{
    private readonly IPlatformPaths _paths;
    private readonly FileSystemScanner _scanner;
    private readonly SafeDeleter _deleter;
    private readonly List<ICleanupModule> _modules;

    public ScanEngine(IPlatformPaths? paths = null)
    {
        _paths = paths ?? PlatformPaths.Current;
        _scanner = new FileSystemScanner();
        _deleter = new SafeDeleter(_paths);
        _modules = new List<ICleanupModule>
        {
            new TempFilesModule(),
            new AppCacheModule(),
            new LogsModule(),
            new BrowserCacheModule(),
            new PackageCacheModule(),
            new DevJunkModule(),
            new LargeFilesModule(),
        };
        if (OperatingSystem.IsWindows()) _modules.Add(new RecycleBinModule());
        else _modules.Add(new TrashDirModule());
    }

    /// <summary>Scans all categories concurrently and returns them sorted by category.</summary>
    public async Task<List<CategoryResult>> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var ctx = new ScanContext { Paths = _paths, Scanner = _scanner, Progress = progress, Cancellation = ct };
        var tasks = _modules.Select(m => Task.Run(() =>
        {
            try { return m.Scan(ctx); }
            catch (OperationCanceledException) { throw; }
            catch { return new CategoryResult { Category = m.Category }; }
        }, ct));

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => (int)r.Category).ToList();
    }

    /// <summary>Deletes the given items (handling the Recycle Bin specially on Windows).</summary>
    public Task<DeleteOutcome> CleanAsync(IEnumerable<CleanItem> items, IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var list = items.ToList();
            var outcome = new DeleteOutcome();

            if (OperatingSystem.IsWindows())
            {
                var recycle = list.Where(i => i.Path == Sentinels.RecycleBin).ToList();
                if (recycle.Count > 0)
                {
                    progress?.Report("Emptying Recycle Bin");
                    outcome.FreedBytes += RecycleBinModule.Empty();
                    outcome.DeletedCount += recycle.Count;
                    list = list.Where(i => i.Path != Sentinels.RecycleBin).ToList();
                }
            }

            var fileOutcome = _deleter.Delete(list, progress, ct);
            outcome.FreedBytes += fileOutcome.FreedBytes;
            outcome.DeletedCount += fileOutcome.DeletedCount;
            outcome.SkippedCount += fileOutcome.SkippedCount;
            outcome.Errors.AddRange(fileOutcome.Errors);
            return outcome;
        }, ct);
}
