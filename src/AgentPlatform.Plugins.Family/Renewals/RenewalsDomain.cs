using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Renewals;

public sealed class AddRenewalTool(IRenewalsRepository repo) : ITool
{
    public string ToolId => "family.renewals.add";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("category", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("label", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("expiresOn", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("leadDays", new JsonSchemaBuilder().Type(SchemaValueType.Integer)))
        .Required("label", "expiresOn").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var a = input.Arguments;
        var label = a.GetProperty("label").GetString()!;
        var category = a.TryGetProperty("category", out var c) ? c.GetString() ?? "other" : "other";
        var expires = DateOnly.TryParse(a.GetProperty("expiresOn").GetString(), out var d)
            ? d : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
        var lead = a.TryGetProperty("leadDays", out var l) ? l.GetInt32() : 30;
        await repo.AddAsync(new Renewal
        {
            GroupId = Guid.Parse(ctx.GroupId), Category = category, Label = label, ExpiresOn = expires, LeadDays = lead
        }, ct);
        return new ToolResult(ToolResultStatus.Success, null, null,
            $"🔔 Dodano przypomnienie: {label} wygasa {expires:yyyy-MM-dd} (powiadomię {lead} dni wcześniej).");
    }
}

public sealed class UpcomingRenewalsProvider(IRenewalsRepository repo) : IContextProvider
{
    public string ProviderId => "group.renewals";
    public ContextScope Scope => ContextScope.Group;
    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var items = await repo.ListUpcomingAsync(Guid.Parse(req.ExecutionContext.GroupId), ct);
        return new ContextSlice(ProviderId, Scope, new
        {
            renewals = items.Select(r => new { r.Label, r.Category, expiresOn = r.ExpiresOn.ToString("yyyy-MM-dd") })
        });
    }
}

public sealed class AddRenewalHandler : IIntentHandler
{
    public string IntentId => "family.add_renewal";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.renewals.add"];
    public string PromptTemplateId => "add_renewal";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
