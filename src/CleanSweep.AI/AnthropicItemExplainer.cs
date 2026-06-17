using System.Collections.Concurrent;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using CleanSweep.Core;
using CleanSweep.Core.AI;
using CleanSweep.Core.Models;

namespace CleanSweep.AI;

/// <summary>
/// Explains scan items using the official Anthropic SDK with Claude structured
/// output. Degrades gracefully: with no API key (or on any error) it falls back
/// to an offline heuristic and never throws or blocks the UI. Results are cached
/// in-memory by category + normalized name so identical items don't re-call.
/// </summary>
public sealed class AnthropicItemExplainer : IItemExplainer
{
    // Default model. claude-haiku-4-5 is a cheaper/faster alternative — set the
    // CLEANSWEEP_AI_MODEL env var to switch.
    private static readonly string DefaultModel = "claude-opus-4-8";

    private readonly string? _apiKey;
    private readonly string _modelId;
    private readonly IItemExplainer _fallback;
    private readonly ConcurrentDictionary<string, ItemExplanation> _cache = new();
    private AnthropicClient? _client;

    public AnthropicItemExplainer(string? apiKey, string? modelId, IItemExplainer fallback)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _modelId = string.IsNullOrWhiteSpace(modelId) ? DefaultModel : modelId!;
        _fallback = fallback;
    }

    public bool IsAiEnabled => _apiKey is not null;

    public async Task<ItemExplanation> ExplainAsync(CleanItem item, CancellationToken ct)
    {
        var key = CacheKey(item);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // No API key → offline heuristic (never hits the network).
        if (_apiKey is null)
        {
            var offline = await _fallback.ExplainAsync(item, ct).ConfigureAwait(false);
            return _cache.GetOrAdd(key, offline);
        }

        try
        {
            var explanation = await CallClaudeAsync(item, ct).ConfigureAwait(false);
            return _cache.GetOrAdd(key, explanation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Network/auth/parse failure → fall back to the offline heuristic.
            var offline = await _fallback.ExplainAsync(item, ct).ConfigureAwait(false);
            return offline; // not cached: a transient failure shouldn't poison the cache
        }
    }

    private async Task<ItemExplanation> CallClaudeAsync(CleanItem item, CancellationToken ct)
    {
        var client = _client ??= new AnthropicClient { ApiKey = _apiKey };

        var parameters = new MessageCreateParams
        {
            Model = _modelId,
            MaxTokens = 400, // small: this is a quick classification
            System = SystemPrompt,
            Messages = [new() { Role = Role.User, Content = UserPrompt(item) }],
            // Structured output: force a compact, reliably-parseable JSON object.
            // No extended thinking is configured — kept fast on purpose.
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResponseSchema } },
        };

        var response = await client.Messages.Create(parameters, ct).ConfigureAwait(false);

        var json = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Empty AI response.");

        var dto = JsonSerializer.Deserialize<Dto>(json, JsonOpts)
                  ?? throw new InvalidOperationException("Unparseable AI response.");

        return new ItemExplanation(
            dto.Summary ?? "",
            ParseRisk(dto.Risk),
            dto.Reason ?? "",
            dto.Recommendation ?? "") { Source = "ai" };
    }

    private const string SystemPrompt =
        "You explain disk-cleanup items for a tool called CleanSweep. For the item described, " +
        "say what it is, whether it's safe to delete, why, and a recommendation. Be concise and " +
        "factual. Err toward caution for user data and large files. Respond only with the final answer.";

    private static string UserPrompt(CleanItem item) =>
        $"Name: {item.DisplayName}\n" +
        $"Full path: {item.Path}\n" +
        $"Category: {item.Category.DisplayName()}\n" +
        $"Size: {ByteSize.Human(item.SizeBytes)}\n" +
        $"Is a directory: {item.IsDirectory}";

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
        int dash = name.IndexOf('—'); // DevJunk uses "name  —  in parent"; key on the base name
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
