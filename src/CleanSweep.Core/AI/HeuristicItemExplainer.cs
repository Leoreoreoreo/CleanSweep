using CleanSweep.Core.Models;

namespace CleanSweep.Core.AI;

/// <summary>
/// A tiny offline dictionary for very common items. Used as a fallback when the
/// AI explainer is unavailable (no API key, network error), so users still get
/// useful guidance with no network call. Never throws, never blocks.
/// </summary>
public sealed class HeuristicItemExplainer : IItemExplainer
{
    public bool IsAiEnabled => false;

    public Task<ItemExplanation> ExplainAsync(CleanItem item, CancellationToken ct)
        => Task.FromResult(Explain(item));

    /// <summary>Synchronous best-effort explanation from a small built-in dictionary.</summary>
    public ItemExplanation Explain(CleanItem item)
    {
        var name = item.DisplayName.ToLowerInvariant();

        if (Has(name, "node_modules"))
            return Offline("A JavaScript dependency folder.", RiskLevel.Caution,
                "node_modules holds installed npm packages. It's rebuildable with `npm install` but only when you still have the project.",
                "Safe to remove for projects you're done with; keep it for active ones.");

        if (Has(name, "__pycache__") || Has(name, ".pytest_cache"))
            return Offline("Python bytecode / test cache.", RiskLevel.Safe,
                "Python regenerates these automatically the next time the code runs.",
                "Safe to delete.");

        if (Has(name, "recycle bin"))
            return Offline("The Windows Recycle Bin.", RiskLevel.Caution,
                "Emptying it permanently deletes everything currently in the bin.",
                "Make sure nothing in the bin is still wanted, then empty it.");

        if (Has(name, "cache"))
            return Offline("Cached data.", RiskLevel.Safe,
                "Caches are rebuilt on demand; clearing them only costs a one-time slowdown.",
                "Safe to delete.");

        if (Has(name, "temp") || Has(name, ".tmp"))
            return Offline("A temporary file or folder.", RiskLevel.Safe,
                "Temp data is meant to be disposable and is recreated as needed.",
                "Safe to delete.");

        if (Has(name, "crashdump") || Has(name, ".dmp"))
            return Offline("A crash dump.", RiskLevel.Safe,
                "Diagnostic snapshot from a crashed program — only useful for debugging that specific crash.",
                "Safe to delete unless you're investigating a crash.");

        if (Has(name, "obj") || Has(name, "bin") || Has(name, "target") || Has(name, ".gradle") || Has(name, "build"))
            return Offline("A build-output folder.", RiskLevel.Caution,
                "Compiled artifacts that your build tool regenerates from source.",
                "Safe to remove; your next build recreates it.");

        // Fall back to category-level guidance.
        return item.Category switch
        {
            CleanCategory.TempFiles    => Offline("Temporary files.", RiskLevel.Safe, "Disposable scratch data.", "Safe to delete."),
            CleanCategory.AppCache     => Offline("Application cache.", RiskLevel.Safe, "Rebuildable cached data.", "Safe to delete."),
            CleanCategory.Logs         => Offline("Log files.", RiskLevel.Safe, "Diagnostic logs not needed for the app to run.", "Safe to delete."),
            CleanCategory.Trash        => Offline("Trashed items.", RiskLevel.Caution, "Deleting empties the trash permanently.", "Confirm you don't need them first."),
            CleanCategory.BrowserCache => Offline("Browser cache.", RiskLevel.Safe, "Cached web content re-downloaded as you browse.", "Safe to delete."),
            CleanCategory.DevJunk      => Offline("Developer build junk.", RiskLevel.Caution, "Rebuildable dependency or output folder.", "Safe to remove; rebuild recreates it."),
            CleanCategory.PackageCache => Offline("Package-manager download cache.", RiskLevel.Safe, "Re-downloaded on the next install.", "Safe to delete."),
            CleanCategory.LargeFiles   => Offline("A large file.", RiskLevel.Risky, "This is your own data, not system junk — only you know if it matters.", "Review it before deleting; back it up if unsure."),
            CleanCategory.Duplicates   => Offline("A duplicate copy.", RiskLevel.Caution, "An identical copy exists elsewhere.", "Keep one copy; removing the others is usually safe."),
            _ => Offline("An item found by the scan.", RiskLevel.Unknown, "CleanSweep couldn't classify this offline.", "Review the path before deleting.")
        };
    }

    private static bool Has(string haystack, string token) => haystack.Contains(token, StringComparison.Ordinal);

    private static ItemExplanation Offline(string summary, RiskLevel risk, string reason, string recommendation)
        => new(summary, risk, reason, recommendation) { Source = "offline" };
}
