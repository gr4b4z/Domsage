using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Light internal sanity check before an ActionPlan leaves the Planner.
/// Distinct from ActionValidator (the DB-side trust boundary).
/// </summary>
public sealed class PlanResponseValidator
{
    public ActionPlan Validate(ActionPlan plan, IIntentHandler handler, ExecutionContext ctx)
    {
        if (plan.Intent == "clarify") return plan;

        var ok = plan.Confidence is >= 0.0 and <= 1.0
                 && !string.IsNullOrWhiteSpace(plan.ToolId)
                 && !string.IsNullOrWhiteSpace(plan.IdempotencyKey)
                 && plan.Mode == handler.Mode;

        return ok ? plan : PlanParser.Clarify(handler, ctx, plan.PromptVersion);
    }
}
