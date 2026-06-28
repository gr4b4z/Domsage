using System.Text.Json;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Queries;

// Read-only "list / what do I have" tools. HasSideEffects = false → safe in incognito.

public sealed class ListPaymentsTool(IPaymentsRepository repo) : ITool
{
    public string ToolId => "family.payments.list";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Guest);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();
    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var due = await repo.ListDueAsync(Guid.Parse(ctx.GroupId), ct);
        if (due.Count == 0)
            return new ToolResult(ToolResultStatus.Success, null, null, "Nie masz nic do zapłacenia. 🎉");
        var lines = due.Select(p => $"• {p.Creditor}: {p.Amount} {p.Currency} (termin {p.DueDate:yyyy-MM-dd})");
        return new ToolResult(ToolResultStatus.Success,
            JsonSerializer.SerializeToElement(due.Select(p => new { p.Creditor, p.Amount, p.Currency, dueDate = p.DueDate.ToString("yyyy-MM-dd") })),
            null, "Do zapłacenia:\n" + string.Join("\n", lines));
    }
}

public sealed class ListTasksTool(ITasksRepository repo) : ITool
{
    public string ToolId => "family.tasks.list";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Guest);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();
    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var open = await repo.ListOpenAsync(Guid.Parse(ctx.GroupId), ct);
        if (open.Count == 0)
            return new ToolResult(ToolResultStatus.Success, null, null, "Brak otwartych zadań. ✅");
        var lines = open.Select(t => $"• {t.Title}{(t.DueDate is { } d ? $" (do {d:yyyy-MM-dd})" : "")}");
        return new ToolResult(ToolResultStatus.Success, null, null, "Zadania:\n" + string.Join("\n", lines));
    }
}

public sealed class ListPaymentsHandler : IIntentHandler
{
    public string IntentId => "family.list_payments";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["today.payments"];
    public string[] AllowedTools => ["family.payments.list"];
    public string PromptTemplateId => "list_payments";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class ListTasksHandler : IIntentHandler
{
    public string IntentId => "family.list_tasks";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["today.tasks"];
    public string[] AllowedTools => ["family.tasks.list"];
    public string PromptTemplateId => "list_tasks";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class ListShoppingHandler : IIntentHandler
{
    public string IntentId => "family.list_shopping";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["group.shopping"];
    public string[] AllowedTools => ["family.shopping.list"];
    public string PromptTemplateId => "list_shopping";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
