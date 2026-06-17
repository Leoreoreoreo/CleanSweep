using CleanSweep.Core.AI;

namespace CleanSweep.AI;

/// <summary>
/// Builds the app's <see cref="IItemExplainer"/>: an Anthropic-backed explainer
/// that reads ANTHROPIC_API_KEY (and an optional CLEANSWEEP_AI_MODEL override),
/// with the offline heuristic as its fallback. Safe to call with no key set.
/// </summary>
public static class ItemExplainerFactory
{
    public static AnthropicItemExplainer Create()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var model = Environment.GetEnvironmentVariable("CLEANSWEEP_AI_MODEL");
        return new AnthropicItemExplainer(apiKey, model, new HeuristicItemExplainer());
    }
}
