using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Conversation.Tools;

/// <summary>conversation.reset — closes the active conversation.</summary>
public sealed class ConversationResetTool(IConversationRepository repo) : ITool
{
    public string ToolId => "conversation.reset";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        await repo.CloseAsync(ctx.ConversationId, "user_reset", ct);
        return new ToolResult(ToolResultStatus.Success, null, null,
            "✅ Rozmowa zresetowana. Zaczynam od nowa.");
    }
}

/// <summary>conversation.save_summary — stores a compaction summary + cursor (no domain side effect).</summary>
public sealed class SaveConversationSummaryTool(IConversationRepository repo) : ITool
{
    public string ToolId => "conversation.save_summary";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("summary", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("lastMessageId", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("summary").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var summary = input.Arguments.GetProperty("summary").GetString()!;
        Guid cursor = input.Arguments.TryGetProperty("lastMessageId", out var l)
            && Guid.TryParse(l.GetString(), out var g) ? g : Guid.Empty;
        await repo.SaveSummaryAsync(ctx.ConversationId, summary, cursor, ct);
        return new ToolResult(ToolResultStatus.Success, null, null);
    }
}

/// <summary>user.remember_fact — stores a long-term fact (survives reset; blocked in incognito).</summary>
public sealed class RememberFactTool(IMemoryFactsRepository repo) : ITool
{
    public string ToolId => "user.remember_fact";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("key", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("value", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("category", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("scope", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("key", "value").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var key = input.Arguments.GetProperty("key").GetString()!;
        var value = input.Arguments.GetProperty("value").GetString()!;
        var category = input.Arguments.TryGetProperty("category", out var c) ? c.GetString() ?? "general" : "general";
        var scope = input.Arguments.TryGetProperty("scope", out var s) ? s.GetString() ?? "user" : "user";
        await repo.UpsertAsync(
            scope == "group" ? null : ctx.UserId, ctx.GroupId, scope, category, key, value, "explicit", ct);
        return new ToolResult(ToolResultStatus.Success, null, null, $"✅ Zapamiętałem: {key} = {value}.");
    }
}
