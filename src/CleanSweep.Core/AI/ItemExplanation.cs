namespace CleanSweep.Core.AI;

/// <summary>How risky it is to delete an item, per the explainer.</summary>
public enum RiskLevel { Safe, Caution, Risky, Unknown }

/// <summary>
/// A short, structured "what is this?" explanation for a scan item - what it is,
/// whether it's safe to delete, why, and a recommendation.
/// </summary>
public sealed record ItemExplanation(string Summary, RiskLevel Risk, string Reason, string Recommendation)
{
    /// <summary>Where it came from: "ai", "offline" (heuristic), or "disabled".</summary>
    public string Source { get; init; } = "ai";

    public bool FromAi => Source == "ai";
}
