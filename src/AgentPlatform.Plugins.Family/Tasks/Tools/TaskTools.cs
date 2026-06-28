using System.Text.Json;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Tasks.Tools;

/// <summary>family.tasks.create — adds a household task.</summary>
public sealed class CreateTaskTool(ITasksRepository repo) : ITool
{
    public string ToolId => "family.tasks.create";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder()
        .Type(SchemaValueType.Object)
        .Properties(
            ("title", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("dueDate", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("title")
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var title = input.Arguments.GetProperty("title").GetString()!;
        DateOnly? due = input.Arguments.TryGetProperty("dueDate", out var d)
            && DateOnly.TryParse(d.GetString(), out var dd) ? dd : null;

        var id = await repo.CreateAsync(new TaskItem
        {
            GroupId = Guid.Parse(ctx.GroupId),
            Title = title,
            DueDate = due,
            CreatedBy = Guid.Parse(ctx.UserId),
        }, ct);

        return new ToolResult(ToolResultStatus.Success,
            JsonSerializer.SerializeToElement(new { id, title }), null,
            $"✅ Dodano zadanie: {title}.");
    }
}

/// <summary>family.tasks.mark_done — completes a task.</summary>
public sealed class MarkTaskDoneTool(ITasksRepository repo) : ITool
{
    public string ToolId => "family.tasks.mark_done";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder()
        .Type(SchemaValueType.Object)
        .Properties(("taskId", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("taskId")
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        if (!Guid.TryParse(input.Arguments.GetProperty("taskId").GetString(), out var id))
            return new ToolResult(ToolResultStatus.Failed, null, "bad id", "❓ Nieprawidłowe zadanie.");
        var ok = await repo.MarkDoneAsync(id, Guid.Parse(ctx.UserId), ct);
        return new ToolResult(ToolResultStatus.Success, null, null,
            ok ? "✅ Zadanie wykonane." : "Zadanie było już wykonane.");
    }
}
