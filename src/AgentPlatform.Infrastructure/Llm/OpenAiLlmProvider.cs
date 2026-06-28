using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Llm;

public sealed class LlmProviderConfig(
    string providerId, ModelTier tier, string modelId, string endpoint, string? apiKey, PriceCard price)
{
    public string ProviderId { get; } = providerId;
    public ModelTier Tier { get; } = tier;
    public string ModelId { get; } = modelId;
    public string Endpoint { get; } = endpoint;
    public string? ApiKey { get; } = apiKey;
    public PriceCard Price { get; } = price;
}

/// <summary>OpenAI-compatible chat completions provider (OpenAI, Azure OpenAI, Ollama). Singleton per tier.</summary>
public sealed class OpenAiLlmProvider(HttpClient http, LlmProviderConfig cfg, ILogger<OpenAiLlmProvider> log)
    : ILlmProvider
{
    public string ProviderId => cfg.ProviderId;
    public ModelTier Tier => cfg.Tier;
    public PriceCard Price => cfg.Price;

    private static bool IsReasoningModel(string model) =>
        model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

    public async Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct)
    {
        var messages = req.Messages.Select(m => new Dictionary<string, object?>
        {
            ["role"] = m.Role == "tool" ? "user" : m.Role,
            ["content"] = m.Content
        }).ToList();

        var body = new Dictionary<string, object?>
        {
            ["model"] = cfg.ModelId,
            ["messages"] = messages,
        };

        // Reasoning-style models (gpt-5*, o1/o3/o4*) only accept the default temperature and
        // use max_completion_tokens. Reasoning tokens also consume the budget, so don't starve
        // output with a tiny cap — give a generous floor.
        if (IsReasoningModel(cfg.ModelId))
        {
            var floor = req.MaxTokens is { } m ? Math.Max(m, 2000) : 4000;
            body["max_completion_tokens"] = floor;
        }
        else
        {
            body["temperature"] = req.Temperature;
            if (req.MaxTokens is { } mt) body["max_tokens"] = mt;
            if (req.TopP is { } tp) body["top_p"] = tp;
        }

        using var msg = new HttpRequestMessage(HttpMethod.Post,
            cfg.Endpoint.TrimEnd('/') + "/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrEmpty(cfg.ApiKey))
            msg.Headers.Add("Authorization", $"Bearer {cfg.ApiKey}");

        try
        {
            using var resp = await http.SendAsync(msg, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var content = root.GetProperty("choices")[0].GetProperty("message")
                .TryGetProperty("content", out var c) ? c.GetString() : null;

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inTok = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
                outTok = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0;
            }
            return new LlmResult(content, inTok, outTok, 0, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "LLM call failed for model {Model}", cfg.ModelId);
            throw;
        }
    }
}
