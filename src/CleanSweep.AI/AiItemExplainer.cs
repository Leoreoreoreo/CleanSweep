using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using CleanSweep.Core;
using CleanSweep.Core.AI;
using CleanSweep.Core.Models;

namespace CleanSweep.AI;

/// <summary>
/// Explains scan items using whichever AI provider is configured: the official
/// Anthropic SDK (Claude) or any OpenAI-compatible endpoint (OpenAI, Gemini,
/// Groq, OpenRouter, local servers, …). Degrades gracefully to an offline
/// heuristic with no key or on any error; results are cached in-memory.
/// </summary>
public sealed class AiItemExplainer : IItemExplainer
{
    private AiSettings _settings;
    private readonly IItemExplainer _fallback;
    private readonly ConcurrentDictionary<string, ItemExplanation> _cache = new();
    private AnthropicClient? _anthropic;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AiItemExplainer(AiSettings settings, IItemExplainer fallback)
    {
        _settings = settings;
        _fallback = fallback;
    }

    public bool IsAiEnabled => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    /// <summary>Switches provider/key/model/base-url at runtime; clears the cache.</summary>
    public void Configure(AiSettings settings)
    {
        _settings = settings;
        _anthropic = null;
        _cache.Clear();
    }

    public async Task<ItemExplanation> ExplainAsync(CleanItem item, CancellationToken ct)
    {
        var key = CacheKey(item);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        if (!IsAiEnabled)
        {
            var offline = await _fallback.ExplainAsync(item, ct).ConfigureAwait(false);
            return _cache.GetOrAdd(key, offline);
        }

        try
        {
            var explanation = _settings.Provider == AiProvider.Anthropic
                ? await CallAnthropicAsync(item, ct).ConfigureAwait(false)
                : await CallOpenAiCompatibleAsync(item, ct).ConfigureAwait(false);
            return _cache.GetOrAdd(key, explanation);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Network/auth/parse failure -> offline heuristic (not cached).
            return await _fallback.ExplainAsync(item, ct).ConfigureAwait(false);
        }
    }

    // ---- Anthropic (official SDK, structured output) ----

    private async Task<ItemExplanation> CallAnthropicAsync(CleanItem item, CancellationToken ct)
    {
        var client = _anthropic ??= new AnthropicClient { ApiKey = _settings.ApiKey };
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "claude-opus-4-8" : _settings.Model!;

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 400,
            System = SystemPrompt,
            Messages = [new() { Role = Role.User, Content = UserPrompt(item) }],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResponseSchema } },
        };

        var response = await client.Messages.Create(parameters, ct).ConfigureAwait(false);
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        return Parse(text);
    }

    // ---- OpenAI-compatible (OpenAI / Gemini / Groq / OpenRouter / local …) ----

    private async Task<ItemExplanation> CallOpenAiCompatibleAsync(CleanItem item, CancellationToken ct)
    {
        var info = AiProviders.Get(_settings.Provider);
        var baseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? info.DefaultBaseUrl : _settings.BaseUrl!).TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? info.DefaultModel : _settings.Model!;

        var payload = new
        {
            model,
            max_tokens = 400,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt + " " + JsonInstruction },
                new { role = "user", content = UserPrompt(item) }
            },
            response_format = new { type = "json_object" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return Parse(content);
    }

    // ---- shared prompt + parsing ----

    private const string SystemPrompt =
        "You explain disk-cleanup items for a tool called CleanSweep. For the item described, " +
        "say what it is, whether it's safe to delete, why, and a recommendation. Be concise and " +
        "factual. Err toward caution for user data and large files.";

    private const string JsonInstruction =
        "Respond ONLY with a JSON object of exactly: " +
        "{\"summary\": string, \"risk\": \"safe\"|\"caution\"|\"risky\", \"reason\": string, \"recommendation\": string}.";

    private static string UserPrompt(CleanItem item) =>
        $"Name: {item.DisplayName}\n" +
        $"Full path: {item.Path}\n" +
        $"Category: {item.Category.DisplayName()}\n" +
        $"Size: {ByteSize.Human(item.SizeBytes)}\n" +
        $"Is a directory: {item.IsDirectory}";

    private static ItemExplanation Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Empty AI response.");
        var dto = JsonSerializer.Deserialize<Dto>(ExtractJson(content), JsonOpts)
                  ?? throw new InvalidOperationException("Unparseable AI response.");
        return new ItemExplanation(dto.Summary ?? "", ParseRisk(dto.Risk), dto.Reason ?? "", dto.Recommendation ?? "")
        { Source = "ai" };
    }

    /// <summary>Pulls the first {...} block out (handles ```json fences / stray prose).</summary>
    private static string ExtractJson(string s)
    {
        s = s.Trim();
        int a = s.IndexOf('{'), b = s.LastIndexOf('}');
        return a >= 0 && b > a ? s.Substring(a, b - a + 1) : s;
    }

    private static RiskLevel ParseRisk(string? risk) => (risk ?? "").Trim().ToLowerInvariant() switch
    {
        "safe" => RiskLevel.Safe,
        "caution" => RiskLevel.Caution,
        "risky" => RiskLevel.Risky,
        _ => RiskLevel.Unknown
    };

    private static string CacheKey(CleanItem item)
    {
        var name = item.DisplayName;
        int dash = name.IndexOf('—');
        if (dash > 0) name = name[..dash];
        return $"{(int)item.Category}|{name.Trim().ToLowerInvariant()}";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, JsonElement> ResponseSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            summary = new { type = "string" },
            risk = new { type = "string", @enum = new[] { "safe", "caution", "risky" } },
            reason = new { type = "string" },
            recommendation = new { type = "string" }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "risk", "reason", "recommendation" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    private sealed record Dto(string? Summary, string? Risk, string? Reason, string? Recommendation);
}
