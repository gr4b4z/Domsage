using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Thin coordinator. Owns every LLM call via PlanningStrategy. Scoped.</summary>
public sealed class Planner(PromptBuilder promptBuilder, PlanningStrategy strategy)
{
    public async Task<ActionPlan> PlanAsync(
        InputMessage msg, IIntentHandler handler, AgentContext ctx, ExecutionContext exec, CancellationToken ct)
    {
        var prompt = await promptBuilder.BuildAsync(handler, msg, ctx, ct);
        return await strategy.ExecuteAsync(prompt, handler, msg, exec, ct);
    }

    public Task<string> BuildClarifyQuestionAsync(
        Contracts.IntentMatch match, ExecutionContext ctx, CancellationToken ct) =>
        Task.FromResult(match.MissingSlots.Count > 0
            ? $"Potrzebuję więcej informacji: {string.Join(", ", match.MissingSlots)}."
            : "Możesz doprecyzować, o co dokładnie chodzi?");
}
