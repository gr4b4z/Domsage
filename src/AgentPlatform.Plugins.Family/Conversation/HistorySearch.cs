using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Conversation;

/// <summary>
/// family.history.search — read-only (safe in all contexts). Full-text search over the user's
/// conversation messages (tsvector), with audit_log as a secondary source (survives resets).
/// </summary>
public sealed class HistorySearchTool(IConversationRepository conversations, IAuditLogRepository audit) : ITool
{
    public string ToolId => "family.history.search";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("query", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("query").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var query = input.Arguments.GetProperty("query").GetString() ?? "";
        var messages = await conversations.SearchMessagesAsync(ctx.UserId, query, 10, ct);
        var actions = await audit.SearchActionsAsync(ctx.GroupId, query, 10, ct);

        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new
        {
            messages = messages.Select(m => new { m.Role, m.Content, m.Intent }),
            actions = actions.Select(a => new { a.Intent, a.ToolId, a.TargetId, occurredAt = a.OccurredAt })
        }), null);
    }
}

/// <summary>conversation.search — same data as a ContextFirst slice (uses the user text as the query).</summary>
public sealed class ConversationSearchProvider(IConversationRepository repo) : IContextProvider
{
    public string ProviderId => "conversation.search";
    public ContextScope Scope => ContextScope.User;
    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var messages = await repo.SearchMessagesAsync(req.ExecutionContext.UserId, req.UserText, 10, ct);
        return new ContextSlice(ProviderId, Scope, new
        {
            matches = messages.Select(m => new { m.Role, m.Content })
        });
    }
}

public sealed class SearchHistoryHandler : IIntentHandler
{
    public string IntentId => "family.search_history";
    public PlannerMode Mode => PlannerMode.ToolCalling;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.history.search"];
    public string PromptTemplateId => "search_history";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
