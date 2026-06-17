namespace CleanSweep.AI;

public enum AiProvider { Anthropic, OpenAI, Gemini, Custom }

/// <summary>Static metadata for a supported AI provider.</summary>
public sealed record AiProviderInfo(
    AiProvider Provider,
    string DisplayName,
    string DefaultBaseUrl,   // OpenAI-compatible base; empty for the Anthropic SDK
    string DefaultModel,
    bool UsesAnthropicSdk,
    bool BaseUrlEditable,
    string ModelHint);

/// <summary>The provider catalog shown in Settings.</summary>
public static class AiProviders
{
    public static readonly IReadOnlyList<AiProviderInfo> All = new[]
    {
        new AiProviderInfo(AiProvider.Anthropic, "Anthropic (Claude)", "", "claude-opus-4-8", true, false,
            "claude-opus-4-8 · claude-haiku-4-5"),
        new AiProviderInfo(AiProvider.OpenAI, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", false, false,
            "gpt-4o-mini · gpt-4o"),
        new AiProviderInfo(AiProvider.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai",
            "gemini-2.0-flash", false, false, "gemini-2.0-flash · gemini-1.5-pro"),
        new AiProviderInfo(AiProvider.Custom, "Custom (OpenAI-compatible)", "https://api.openai.com/v1", "", false, true,
            "any model id your endpoint serves"),
    };

    public static AiProviderInfo Get(AiProvider provider)
    {
        foreach (var info in All) if (info.Provider == provider) return info;
        return All[0];
    }

    public static AiProvider Parse(string? name)
        => Enum.TryParse<AiProvider>(name, out var p) ? p : AiProvider.Anthropic;
}

/// <summary>A complete, runtime-configurable AI setting.</summary>
public sealed record AiSettings(AiProvider Provider, string? ApiKey, string? Model, string? BaseUrl);
