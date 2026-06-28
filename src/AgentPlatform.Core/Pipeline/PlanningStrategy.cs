using System.Text.Json;
using AgentPlatform.Core.Budget;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Selects mode and manages the LLM call(s). ContextFirst = one call. ToolCalling = bounded loop
/// with the circuit breaker enforced on every iteration. Scoped.
/// </summary>
public sealed class PlanningStrategy(
    IServiceProvider sp,
    IUsageMeter meter,
    BudgetEnforcer budget,
    PlanParser parser,
    PlanResponseValidator responseValidator,
    ToolExecutor toolExecutor)
{
    public async Task<ActionPlan> ExecuteAsync(
        PromptBuilder.BuiltPrompt prompt, IIntentHandler handler, InputMessage msg,
        ExecutionContext ctx, CancellationToken ct)
    {
        return handler.Mode == PlannerMode.ContextFirst
            ? await ContextFirstAsync(prompt, handler, ctx, ct)
            : await ToolCallingAsync(prompt, handler, msg, ctx, ct);
    }

    private async Task<ActionPlan> ContextFirstAsync(
        PromptBuilder.BuiltPrompt prompt, IIntentHandler handler, ExecutionContext ctx, CancellationToken ct)
    {
        var result = await CallAsync(prompt.Request, handler, ctx, ct);
        var plan = parser.Parse(result.Content, handler, ctx, prompt.Version);
        return responseValidator.Validate(plan, handler, ctx);
    }

    private async Task<ActionPlan> ToolCallingAsync(
        PromptBuilder.BuiltPrompt prompt, IIntentHandler handler, InputMessage msg,
        ExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<LlmMessage>(prompt.Request.Messages);
        var toolSchemas = ResolveToolSchemas(handler);
        var diagnosticCtx = ctx with { Mode = ExecutionMode.Diagnostic };
        int steps = 0;

        while (true)
        {
            await budget.CheckIterationAsync(ctx.RequestId, ct);

            var req = prompt.Request with { Messages = messages, Tools = toolSchemas };
            var result = await CallAsync(req, handler, ctx, ct);

            if (result.ToolCall is { } call)
            {
                steps++;
                var toolPlan = new ActionPlan(
                    Intent: handler.IntentId, Mode: handler.Mode, Scope: ContextScope.Group,
                    TargetId: null, ToolId: call.ToolName, Confidence: 1.0,
                    RequiresConfirmation: false,
                    IdempotencyKey: $"{ctx.RequestId}:diag:{steps}",
                    PromptVersion: prompt.Version, ModelTier: handler.PreferredTier,
                    DiagnosticSteps: steps, ToolInput: call.Arguments);

                var toolResult = await toolExecutor.ExecuteAsync(toolPlan, diagnosticCtx, ct);
                messages.Add(new LlmMessage("assistant", $"[tool_call {call.ToolName}]"));
                messages.Add(new LlmMessage("tool",
                    JsonSerializer.Serialize(toolResult.Data ?? default)));
                continue;
            }

            var plan = parser.Parse(result.Content, handler, ctx, prompt.Version) with { DiagnosticSteps = steps };
            return responseValidator.Validate(plan, handler, ctx);
        }
    }

    private IReadOnlyList<ToolSchema> ResolveToolSchemas(IIntentHandler handler)
    {
        var tools = sp.GetServices<ITool>().Where(t => handler.AllowedTools.Contains(t.ToolId));
        return tools.Select(t => new ToolSchema(
            t.ToolId, $"Tool {t.ToolId}", JsonSerializer.Serialize(t.InputSchema))).ToList();
    }

    private async Task<LlmResult> CallAsync(
        LlmRequest req, IIntentHandler handler, ExecutionContext ctx, CancellationToken ct)
    {
        await budget.CheckLlmCallAsync(ctx.RequestId, ct);
        var provider = sp.GetRequiredKeyedService<ILlmProvider>(handler.PreferredTier);
        var result = await provider.CompleteAsync(req, ct);

        var cost = provider.Price.InputPerMillion * result.InputTokens / 1_000_000m
                 + provider.Price.OutputPerMillion * result.OutputTokens / 1_000_000m
                 + provider.Price.CachedPerMillion * result.CachedTokens / 1_000_000m;

        await meter.RecordAsync(new UsageEvent(
            ctx.RequestId, ctx.UserId, ctx.GroupId, provider.ProviderId, provider.Tier,
            req.ModelId, result.InputTokens, result.OutputTokens, result.CachedTokens, cost,
            handler.IntentId), ct);
        await budget.RecordSpendAsync(ctx.GroupId, cost, ct);

        return result;
    }
}
