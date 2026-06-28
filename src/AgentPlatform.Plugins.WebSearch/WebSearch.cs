using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.WebSearch;

// ── Key rotation ──────────────────────────────────────────────────────────────
public interface IKeyRotationStrategy { string Next(IReadOnlyList<string> keys); }

public sealed class RoundRobinKeyStrategy : IKeyRotationStrategy
{
    private int _counter = -1;
    public string Next(IReadOnlyList<string> keys) =>
        keys[(int)((uint)Interlocked.Increment(ref _counter) % (uint)keys.Count)];
}

public sealed class RandomKeyStrategy : IKeyRotationStrategy
{
    public string Next(IReadOnlyList<string> keys) => keys[Random.Shared.Next(keys.Count)];
}

// ── Providers ─────────────────────────────────────────────────────────────────
public sealed class BraveOptions { public List<string> ApiKeys { get; set; } = new(); }

public sealed class BraveSearchProvider(
    HttpClient http, IOptions<BraveOptions> options, IKeyRotationStrategy rotation,
    ILogger<BraveSearchProvider> log) : IWebSearchProvider
{
    private readonly IReadOnlyList<string> _keys = options.Value.ApiKeys;
    public string ProviderId => "brave";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (_keys.Count == 0) return [];
        var key = rotation.Next(_keys);
        var res = await Send(query, maxResults, key, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests && _keys.Count > 1)
        {
            log.LogWarning("Brave key rate-limited, retrying with next key");
            res.Dispose();
            res = await Send(query, maxResults, rotation.Next(_keys), ct);
        }
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        res.Dispose();
        if (!doc.RootElement.TryGetProperty("web", out var web) ||
            !web.TryGetProperty("results", out var arr)) return [];
        return arr.EnumerateArray().Select(r => new SearchResult(
            r.GetProperty("title").GetString() ?? "", r.GetProperty("url").GetString() ?? "",
            r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "")).ToList();
    }

    private Task<HttpResponseMessage> Send(string query, int maxResults, string key, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={maxResults}&country=pl");
        req.Headers.Add("X-Subscription-Token", key);
        req.Headers.Add("Accept", "application/json");
        return http.SendAsync(req, ct);
    }
}

public sealed class SearXngProvider(HttpClient http, string baseUrl) : IWebSearchProvider
{
    public string ProviderId => "searxng";
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&language=pl&safesearch=1";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return [];
        return arr.EnumerateArray().Take(maxResults).Select(r => new SearchResult(
            r.GetProperty("title").GetString() ?? "", r.GetProperty("url").GetString() ?? "",
            r.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "")).ToList();
    }
}

// ── Tool ──────────────────────────────────────────────────────────────────────
public sealed class WebSearchTool(IWebSearchProvider provider) : ITool
{
    public string ToolId => "web.search";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("query", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("maxResults", new JsonSchemaBuilder().Type(SchemaValueType.Integer).Minimum(1).Maximum(10)))
        .Required("query").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var query = input.Arguments.GetProperty("query").GetString()!;
        var max = input.Arguments.TryGetProperty("maxResults", out var n) ? n.GetInt32() : 5;
        try
        {
            var results = await provider.SearchAsync(query, max, ct);
            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(results), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // The search backend (SearXNG/Brave) is unreachable/misconfigured — degrade gracefully
            // instead of crashing the whole request with a raw HttpRequestException.
            return new ToolResult(ToolResultStatus.Failed, null, $"web search failed: {ex.Message}",
                "🌐 Nie mogę teraz przeszukać internetu — wyszukiwarka jest niedostępna.");
        }
    }
}

public sealed class AnswerQuestionHandler : IIntentHandler
{
    public string IntentId => "web.answer_question";
    public PlannerMode Mode => PlannerMode.ToolCalling;
    public string[] RequiredContextProviders => ["conversation.history"];
    public string[] AllowedTools => ["web.search"];
    public string PromptTemplateId => "answer_question";
    public ModelTier PreferredTier => ModelTier.Large;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
