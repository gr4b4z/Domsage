using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Turns a tool's structured result into a natural, question-aware answer (Small-tier LLM). Used by
/// handlers with <see cref="IIntentHandler.PhraseResult"/> = true (weather, web answers). Strictly
/// grounded: the model may only use the supplied data — it does not add facts. Falls back to the tool's
/// own message on any failure, so a reply is never lost. Scoped.
/// </summary>
public sealed class ResultPhraser(
    IServiceProvider sp, IUsageMeter meter, BudgetEnforcer budget, ILogger<ResultPhraser> log)
{
    public async Task<string> PhraseAsync(string question, string toolData, string fallback, ExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            await budget.CheckLlmCallAsync(ctx.RequestId, ct);
            var provider = sp.GetRequiredKeyedService<ILlmProvider>(ModelTier.Small);

            var system =
                "Jesteś domowym asystentem. Odpowiedz po polsku — naturalnie, zwięźle i wprost NA PYTANIE użytkownika. " +
                "Korzystaj WYŁĄCZNIE z danych w <data> (to fakty z narzędzia); NIE dodawaj niczego spoza nich i nie zmyślaj. " +
                "Bez wstępów typu „oto dane”. Jeśli dane nie zawierają odpowiedzi, powiedz to krótko.";
            var user = $"<pytanie>{question}</pytanie>\n<data>{toolData}</data>";

            var req = new LlmRequest("gpt-4o-mini", ModelTier.Small, 0.4, null, 500, null,
                [new("system", system), new("user", user)], null);

            var result = await provider.CompleteAsync(req, ct);
            var cost = provider.Price.InputPerMillion * result.InputTokens / 1_000_000m
                     + provider.Price.OutputPerMillion * result.OutputTokens / 1_000_000m;
            await meter.RecordAsync(new UsageEvent(ctx.RequestId, ctx.UserId, ctx.GroupId,
                provider.ProviderId, provider.Tier, "phrase", result.InputTokens,
                result.OutputTokens, result.CachedTokens, cost, "__phrase__"), ct);
            await budget.RecordSpendAsync(ctx.GroupId, cost, ct);

            return string.IsNullOrWhiteSpace(result.Content) ? fallback : result.Content!.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Result phrasing failed; using the tool's own message");
            return fallback;
        }
    }
}
