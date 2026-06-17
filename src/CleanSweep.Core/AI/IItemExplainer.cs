using CleanSweep.Core.Models;

namespace CleanSweep.Core.AI;

/// <summary>Produces a short explanation of what a scan item is and whether it's safe to delete.</summary>
public interface IItemExplainer
{
    /// <summary>True when live AI explanations are available (an API key is configured).</summary>
    bool IsAiEnabled { get; }

    Task<ItemExplanation> ExplainAsync(CleanItem item, CancellationToken ct);
}
