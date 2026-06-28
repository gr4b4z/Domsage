using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Audit;

/// <summary>Writes append-only audit entries. Scoped — one per pipeline run.</summary>
public sealed class AuditLogger(IAuditLogRepository repo)
{
    public Task WriteAsync(ActionPlan plan, ExecutionContext ctx, string result,
        bool isDiagnostic, CancellationToken ct,
        string? errorMessage = null, int inputTokens = 0, int outputTokens = 0,
        decimal costUsd = 0m, string[]? contextFetched = null)
    {
        var entry = new AuditEntry(
            UserId: ctx.UserId,
            GroupId: ctx.GroupId,
            GroupType: ctx.GroupType,
            Intent: plan.Intent,
            PlannerMode: plan.Mode == PlannerMode.ContextFirst ? "context_first" : "tool_calling",
            ToolId: plan.ToolId,
            TargetId: plan.TargetId,
            Result: isDiagnostic ? "diagnostic" : result,
            ErrorMessage: errorMessage,
            IdempotencyKey: plan.IdempotencyKey,
            PromptVersion: plan.PromptVersion,
            ModelTier: plan.ModelTier.ToString(),
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CostUsd: costUsd,
            DiagnosticSteps: plan.DiagnosticSteps,
            ContextFetched: contextFetched);
        return repo.WriteAsync(entry, ct);
    }
}
