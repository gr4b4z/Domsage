using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// DB-side trust boundary. The Action Plan is an untrusted proposal — scope, role and tool
/// permissions are re-checked server-side here, never trusted from the plan. Scoped.
/// </summary>
public sealed class ActionValidator(
    PluginRegistry registry,
    IServiceProvider sp,
    BudgetEnforcer budget)
{
    public async Task<ValidationResult> ValidateAsync(
        ActionPlan plan, IIntentHandler handler, ExecutionContext ctx, CancellationToken ct)
    {
        var tool = registry.ResolveTool(plan.ToolId, sp);

        // 1. Incognito guard — block side-effect tools.
        if (ctx.IsIncognito && tool.HasSideEffects)
            return new ValidationResult(false, plan, Rejected: true,
                RejectionReason: "Akcje niedostępne w trybie incognito.");

        // 2. Tool whitelist — plan may only invoke tools the handler declares.
        if (!handler.AllowedTools.Contains(plan.ToolId))
            throw new SecurityViolationException(
                $"Tool '{plan.ToolId}' is not in AllowedTools for intent '{handler.IntentId}'.");

        // 3. Scope + role re-check (server-side, from the role resolved out of the DB).
        var required = tool.RequiredScope;
        if (ctx.UserRole < required.MinimumRole)
            throw new SecurityViolationException(
                $"Role {ctx.UserRole} below required {required.MinimumRole} for tool '{plan.ToolId}'.");

        // 4. Budget / breaker.
        await budget.CheckRequestAsync(ctx.RequestId, ctx.GroupId, ct);

        // 5. Confirmation policy.
        bool needsConfirm = handler.Confirmation switch
        {
            ConfirmationPolicy.Required => true,
            ConfirmationPolicy.RequiredForHighImpact => plan.Confidence < 0.95,
            _ => false
        };

        return needsConfirm
            ? new ValidationResult(true, plan, ConfirmationPrompt: BuildPrompt(plan))
            : new ValidationResult(false, plan);
    }

    private static string BuildPrompt(ActionPlan plan) =>
        $"Czy potwierdzasz akcję „{plan.Intent}”{(plan.TargetId is not null ? $" ({plan.TargetId})" : "")}?";
}
