using CleanSweep.AI;
using CleanSweep.Core.AI;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Tests;

public sealed class ItemExplainerTests
{
    private static CleanItem Item(string name, CleanCategory category, bool dir = false, long size = 1024) => new()
    {
        Path = $"/tmp/{name}",
        DisplayName = name,
        SizeBytes = size,
        Category = category,
        IsDirectory = dir
    };

    // ---- Offline heuristic fallback ----

    [Fact]
    public void Heuristic_explains_common_items_with_sensible_risk()
    {
        var h = new HeuristicItemExplainer();

        Assert.Equal(RiskLevel.Caution, h.Explain(Item("node_modules  -  in app", CleanCategory.DevJunk, dir: true)).Risk);
        Assert.Equal(RiskLevel.Safe, h.Explain(Item("__pycache__", CleanCategory.DevJunk, dir: true)).Risk);
        Assert.Equal(RiskLevel.Caution, h.Explain(Item("Recycle Bin - 12 item(s)", CleanCategory.Trash, dir: true)).Risk);
    }

    [Fact]
    public void Heuristic_errs_toward_caution_for_large_user_files()
    {
        var h = new HeuristicItemExplainer();
        var explanation = h.Explain(Item("vacation.mov", CleanCategory.LargeFiles, size: 500_000_000));
        Assert.Equal(RiskLevel.Risky, explanation.Risk);
    }

    [Fact]
    public void Heuristic_always_returns_an_offline_explanation()
    {
        var h = new HeuristicItemExplainer();
        var explanation = h.Explain(Item("something-unrecognized.dat", CleanCategory.TempFiles));
        Assert.False(string.IsNullOrWhiteSpace(explanation.Summary));
        Assert.Equal("offline", explanation.Source);
        Assert.False(explanation.FromAi);
        Assert.False(h.IsAiEnabled);
    }

    // ---- Graceful degradation when the API key is absent (no network) ----

    [Theory]
    [InlineData(AiProvider.Anthropic)]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Gemini)]
    public async Task With_no_api_key_explainer_is_disabled_and_uses_offline_fallback(AiProvider provider)
    {
        // No key -> never builds a client / never hits the network, for any provider.
        var explainer = new AiItemExplainer(new AiSettings(provider, null, null, null), new HeuristicItemExplainer());

        Assert.False(explainer.IsAiEnabled);

        var result = await explainer.ExplainAsync(
            Item("node_modules", CleanCategory.DevJunk, dir: true), CancellationToken.None);

        Assert.Equal("offline", result.Source);
        Assert.NotEqual(RiskLevel.Unknown, result.Risk);
    }

    [Fact]
    public async Task With_no_api_key_results_are_cached_per_item()
    {
        var explainer = new AiItemExplainer(new AiSettings(AiProvider.OpenAI, "", null, null), new CountingExplainer());
        var item = Item("temp.tmp", CleanCategory.TempFiles);

        var first = await explainer.ExplainAsync(item, CancellationToken.None);
        var second = await explainer.ExplainAsync(item, CancellationToken.None);

        Assert.Same(first, second); // second call served from cache
    }

    [Fact]
    public void Reconfigure_can_switch_provider_at_runtime()
    {
        var explainer = new AiItemExplainer(new AiSettings(AiProvider.Anthropic, null, null, null), new HeuristicItemExplainer());
        Assert.False(explainer.IsAiEnabled);

        explainer.Configure(new AiSettings(AiProvider.OpenAI, "sk-test", "gpt-4o-mini", null));
        Assert.True(explainer.IsAiEnabled); // key present -> enabled (no network call made here)
    }

    /// <summary>A stand-in fallback that returns a fresh instance per call, to prove caching.</summary>
    private sealed class CountingExplainer : IItemExplainer
    {
        public bool IsAiEnabled => false;
        public Task<ItemExplanation> ExplainAsync(CleanItem item, CancellationToken ct)
            => Task.FromResult(new ItemExplanation("stub", RiskLevel.Safe, "r", "rec") { Source = "offline" });
    }
}
