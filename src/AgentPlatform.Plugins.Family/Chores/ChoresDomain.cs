using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Chores;

/// <summary>family.chores.assign — assign a recurring chore (optionally with allowance) to a member.</summary>
public sealed class AssignChoreTool(IChoresRepository repo) : ITool
{
    public string ToolId => "family.chores.assign";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("title", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("assignedTo", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("allowanceAmount", new JsonSchemaBuilder().Type(SchemaValueType.Number)))
        .Required("title", "assignedTo").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var title = input.Arguments.GetProperty("title").GetString()!;
        var assignedTo = Guid.TryParse(input.Arguments.GetProperty("assignedTo").GetString(), out var a)
            ? a : Guid.Parse(ctx.UserId);
        decimal? allowance = input.Arguments.TryGetProperty("allowanceAmount", out var al) ? al.GetDecimal() : null;
        await repo.AddAsync(new Chore
        {
            GroupId = Guid.Parse(ctx.GroupId), AssignedTo = assignedTo, Title = title, AllowanceAmount = allowance
        }, ct);
        return new ToolResult(ToolResultStatus.Success, null, null, $"🧹 Przydzielono zadanie domowe: {title}.");
    }
}

public sealed class AssignChoreHandler : IIntentHandler
{
    public string IntentId => "family.assign_chore";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.chores.assign"];
    public string PromptTemplateId => "assign_chore";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
