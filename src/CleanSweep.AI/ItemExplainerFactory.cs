using CleanSweep.Core.AI;

namespace CleanSweep.AI;

/// <summary>
/// Builds the app's explainer from environment defaults (ANTHROPIC_API_KEY or
/// OPENAI_API_KEY, plus optional CLEANSWEEP_AI_MODEL). Settings saved in-app
/// override these at runtime. The offline heuristic is always the fallback.
/// </summary>
public static class ItemExplainerFactory
{
    public static AiItemExplainer Create()
    {
        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("CLEANSWEEP_AI_MODEL");

        AiSettings settings =
            !string.IsNullOrWhiteSpace(anthropicKey) ? new AiSettings(AiProvider.Anthropic, anthropicKey, model, null)
          : !string.IsNullOrWhiteSpace(openAiKey)    ? new AiSettings(AiProvider.OpenAI, openAiKey, model, null)
          : new AiSettings(AiProvider.Anthropic, null, model, null);

        return new AiItemExplainer(settings, new HeuristicItemExplainer());
    }
}
