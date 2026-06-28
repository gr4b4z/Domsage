using System.Text.Json;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Classifies a request into one or more intents using a lightweight (Small tier) LLM call.
/// Supports multi-intent, low-confidence clarify, and unknown fallback. Scoped.
/// MUST NOT perform tool calls or DB access.
/// </summary>
public sealed class IntentRouter(
    PluginRegistry registry,
    IServiceProvider sp,
    IUsageMeter meter,
    BudgetEnforcer budget,
    IConversationRepository conversations,
    ILogger<IntentRouter> log)
{
    private const double ClarifyThreshold = 0.55;

    public async Task<IReadOnlyList<IntentMatch>> ClassifyAsync(
        InputMessage msg, ExecutionContext ctx, CancellationToken ct)
    {
        var intentIds = registry.AllHandlers
            .Select(h => h.IntentId)
            .Where(id => id is not ("clarify" or "fallback"))
            .ToList();

        if (intentIds.Count == 0)
            return [new IntentMatch("fallback", 1.0, msg.Text, [])];

        var history = await RecentHistoryAsync(ctx, ct);

        var system =
            "You are an intent router for a household assistant. Classify the user message into " +
            "one or more of these intents (split multi-intent requests):\n" +
            string.Join("\n", intentIds.Select(i => "- " + i)) + "\n" +
            "Guidance: prefer the most specific add/create/list/mark intent that matches the words used. " +
            "Only choose an intent about a received or attached document/invoice when the user explicitly " +
            "says they received, forwarded or attached one. For 'what do I have / owe / need' questions, pick a list_* intent. " +
            history +
            "Return ONLY JSON: {\"intents\":[{\"intentId\":\"...\",\"confidence\":0.0-1.0,\"segment\":\"...\"}]}. " +
            "If none fit, return intentId \"fallback\".";

        var req = new LlmRequest("gpt-4o-mini", ModelTier.Small, 0.0, null, 300, null,
            [new("system", system), new("user", $"<user_message>{msg.Text}</user_message>")], null);

        LlmResult result;
        try
        {
            await budget.CheckLlmCallAsync(ctx.RequestId, ct);
            var provider = sp.GetRequiredKeyedService<ILlmProvider>(ModelTier.Small);
            result = await provider.CompleteAsync(req, ct);
            var cost = provider.Price.InputPerMillion * result.InputTokens / 1_000_000m
                     + provider.Price.OutputPerMillion * result.OutputTokens / 1_000_000m;
            await meter.RecordAsync(new UsageEvent(ctx.RequestId, ctx.UserId, ctx.GroupId,
                provider.ProviderId, provider.Tier, "intent_router", result.InputTokens,
                result.OutputTokens, result.CachedTokens, cost, "__router__"), ct);
            await budget.RecordSpendAsync(ctx.GroupId, cost, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Intent routing failed");
            return [new IntentMatch("fallback", 0.0, msg.Text, [])];
        }

        return Parse(result.Content, intentIds, msg.Text);
    }

    /// <summary>
    /// Recent conversation turns so the router can resolve elliptical follow-ups ("A pieczarki?",
    /// "a może jutro?") in context instead of mis-routing them as new commands. Skipped in incognito.
    /// </summary>
    private async Task<string> RecentHistoryAsync(ExecutionContext ctx, CancellationToken ct)
    {
        if (ctx.IsIncognito || !Guid.TryParse(ctx.ConversationId, out _)) return "";
        try
        {
            var msgs = await conversations.FetchWithinBudgetAsync(ctx.ConversationId, null, 600, ct);
            if (msgs.Count == 0) return "";
            // Repo returns newest-first; show the last few chronologically.
            var lines = msgs.Take(6).Reverse()
                .Select(m => $"{(m.Role == "user" ? "User" : "Assistant")}: {m.Content}");
            return "\nRecent conversation (context):\n" + string.Join("\n", lines) + "\n" +
                "A short follow-up (e.g. just a noun or 'a może…?') usually CONTINUES the topic above — " +
                "if the previous turn was a question/answer, classify the follow-up the same way (often 'fallback'), " +
                "NOT as a new add/create command.\n";
        }
        catch (Exception ex) { log.LogWarning(ex, "Router history fetch failed"); return ""; }
    }

    private IReadOnlyList<IntentMatch> Parse(string? content, List<string> known, string fullText)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [new IntentMatch("fallback", 0.0, fullText, [])];

        try
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            var json = start >= 0 && end > start ? content[start..(end + 1)] : content;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("intents", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [new IntentMatch("fallback", 0.0, fullText, [])];

            var matches = new List<IntentMatch>();
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.TryGetProperty("intentId", out var i) ? i.GetString() : null;
                var conf = e.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetDouble() : 0.0;
                var seg = e.TryGetProperty("segment", out var s) ? s.GetString() ?? fullText : fullText;
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (id == "fallback" || !known.Contains(id))
                    matches.Add(new IntentMatch("fallback", conf, seg, []));
                else if (conf < ClarifyThreshold)
                    matches.Add(new IntentMatch("clarify", conf, seg, [id]));
                else
                    matches.Add(new IntentMatch(id, conf, seg, []));
            }
            return matches.Count > 0 ? matches : [new IntentMatch("fallback", 0.0, fullText, [])];
        }
        catch
        {
            return [new IntentMatch("fallback", 0.0, fullText, [])];
        }
    }
}
