using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Converts a ToolResult to human-readable text. Uses the LLM only as a fallback. Scoped.</summary>
public sealed class ResponseBuilder(IServiceProvider sp, IUsageMeter meter)
{
    public async Task<ResponseResult> BuildAsync(
        ToolResult toolResult, ActionPlan plan, ExecutionContext ctx, CancellationToken ct)
    {
        if (toolResult.HumanMessage is not null)
            return new ResponseResult(toolResult.HumanMessage, false, null, null);

        var provider = sp.GetKeyedService<ILlmProvider>(ModelTier.Small);
        if (provider is null)
            return new ResponseResult("✅ Gotowe.", false, null, null);

        var req = new LlmRequest("gpt-4o-mini", ModelTier.Small, 0.3, null, 150, null,
            [
                new("system", "Summarize this action result in one friendly Polish sentence."),
                new("user", $"Intent: {plan.Intent}\nResult: {JsonSerializer.Serialize(toolResult.Data)}")
            ], null);

        var result = await provider.CompleteAsync(req, ct);
        var cost = provider.Price.OutputPerMillion * result.OutputTokens / 1_000_000m
                 + provider.Price.InputPerMillion * result.InputTokens / 1_000_000m;
        await meter.RecordAsync(new UsageEvent(ctx.RequestId, ctx.UserId, ctx.GroupId,
            provider.ProviderId, provider.Tier, "response_builder", result.InputTokens,
            result.OutputTokens, result.CachedTokens, cost, plan.Intent), ct);

        return new ResponseResult(result.Content ?? "✅ Gotowe.", false, null, null);
    }
}
