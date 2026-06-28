using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Executes tools deterministically with idempotency + audit. Scoped.</summary>
public sealed class ToolExecutor(
    PluginRegistry registry,
    IServiceProvider sp,
    IIdempotencyRepository idempotency,
    IDeadLetterRepository dlq,
    AuditLogger audit,
    BudgetEnforcer budget)
{
    public async Task<ToolResult> ExecuteAsync(ActionPlan plan, ExecutionContext ctx, CancellationToken ct)
    {
        var tool = registry.ResolveTool(plan.ToolId, sp);
        var input = new ToolInput(plan.ToolId, plan.ToolInput);

        // Diagnostic (inside ToolCalling loop): no idempotency, lightweight audit
        if (ctx.Mode == ExecutionMode.Diagnostic)
        {
            await budget.CheckToolCallAsync(plan.ToolId, ctx.RequestId, ct);
            var dr = await tool.ExecuteAsync(input, ctx, ct);
            if (!ctx.IsIncognito)
                await audit.WriteAsync(plan, ctx, dr.Status.ToString().ToLowerInvariant(), isDiagnostic: true, ct);
            return dr;
        }

        await budget.CountToolCallAsync(ctx.RequestId, ct);
        await budget.CheckToolCallAsync(plan.ToolId, ctx.RequestId, ct);

        // 1. Validate input schema
        var validation = tool.InputSchema.Evaluate(plan.ToolInput,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!validation.IsValid)
            throw new ToolInputValidationException(CollectErrors(validation));

        // 2. Idempotency
        if (!await idempotency.TryAcquireAsync(plan.IdempotencyKey, ct))
            return await idempotency.GetCachedResultAsync(plan.IdempotencyKey, ct);

        // 3. Execute
        ToolResult toolResult;
        try
        {
            toolResult = await tool.ExecuteAsync(input, ctx, ct);
        }
        catch
        {
            await idempotency.ReleaseAsync(plan.IdempotencyKey, ct);
            throw;
        }

        // 4. Cache + audit
        await idempotency.StoreResultAsync(plan.IdempotencyKey, toolResult, ct);
        if (!ctx.IsIncognito)
            await audit.WriteAsync(plan, ctx, toolResult.Status.ToString().ToLowerInvariant(), isDiagnostic: false, ct);

        return toolResult.Status switch
        {
            ToolResultStatus.Success => toolResult,
            ToolResultStatus.Retryable => throw new RetryableToolException(toolResult.ErrorMessage),
            _ => await FailAsync(plan, ctx, toolResult, ct)
        };
    }

    private static IEnumerable<string> CollectErrors(EvaluationResults results)
    {
        var list = new List<string>();
        void Walk(EvaluationResults r)
        {
            if (r.Errors is { Count: > 0 })
                list.AddRange(r.Errors.Select(kv => $"{r.InstanceLocation}: {kv.Value}"));
            if (r.Details is { Count: > 0 })
                foreach (var d in r.Details) Walk(d);
        }
        Walk(results);
        return list.Count > 0 ? list : ["schema validation failed"];
    }

    private async Task<ToolResult> FailAsync(ActionPlan plan, ExecutionContext ctx, ToolResult result, CancellationToken ct)
    {
        await dlq.WriteAsync(ctx.GroupId, plan.ToolId, plan.ToolInput.GetRawText(),
            result.ErrorMessage ?? "Tool failed", "terminal", ct);
        throw new TerminalToolException(result.ErrorMessage ?? "Tool failed",
            userMessage: "Akcja nie powiodła się.");
    }
}
