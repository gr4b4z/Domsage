using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Shopping;

/// <summary>family.shopping.add — add to the one shared household list.</summary>
public sealed class AddShoppingItemTool(IShoppingRepository repo) : ITool
{
    public string ToolId => "family.shopping.add";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("name", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("quantity", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("name").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var name = input.Arguments.GetProperty("name").GetString()!;
        var qty = input.Arguments.TryGetProperty("quantity", out var q) ? q.GetString() : null;
        await repo.AddItemAsync(new ShoppingItem
        {
            GroupId = Guid.Parse(ctx.GroupId), Name = name, Quantity = qty, AddedBy = Guid.Parse(ctx.UserId)
        }, ct);
        return new ToolResult(ToolResultStatus.Success, null, null,
            $"🛒 Dodano do listy: {name}{(qty is null ? "" : $" ({qty})")}.");
    }
}

/// <summary>family.shopping.list — show the shared list.</summary>
public sealed class ListShoppingTool(IShoppingRepository repo) : ITool
{
    public string ToolId => "family.shopping.list";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Guest);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var items = await repo.ListNeededAsync(Guid.Parse(ctx.GroupId), ct);
        if (items.Count == 0)
            return new ToolResult(ToolResultStatus.Success, null, null, "Lista zakupów jest pusta. 🛒");
        var lines = items.Select(i => $"• {i.Name}{(i.Quantity is null ? "" : $" ({i.Quantity})")}");
        return new ToolResult(ToolResultStatus.Success, null, null, "Lista zakupów:\n" + string.Join("\n", lines));
    }
}

/// <summary>family.shopping.mark_bought — check off an item; notify active watchers (not the buyer).</summary>
public sealed class MarkShoppingBoughtTool(
    IShoppingRepository repo, IGroupDirectory dir, INotificationService notifier) : ITool
{
    public string ToolId => "family.shopping.mark_bought";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("name", new JsonSchemaBuilder().Type(SchemaValueType.String))).Required("name").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var name = input.Arguments.GetProperty("name").GetString()!;
        var groupId = Guid.Parse(ctx.GroupId);
        var item = await repo.MarkBoughtAsync(groupId, name, Guid.Parse(ctx.UserId), ct);
        if (item is null)
            return new ToolResult(ToolResultStatus.Success, null, null, $"Nie znalazłem „{name}” na liście.");

        var remaining = await repo.ListNeededAsync(groupId, ct);
        var remainingText = remaining.Count == 0 ? "Lista pusta — wszystko kupione! ✅"
            : "Zostało: " + string.Join(", ", remaining.Select(r => r.Name));

        // Notify only opt-in watchers (default: nobody), never the buyer.
        var watchers = (await repo.ActiveWatchersAsync(groupId, ct)).Select(g => g.ToString())
            .Where(u => u != ctx.UserId).ToList();
        if (watchers.Count > 0)
        {
            var members = await dir.GetMembersAsync(ctx.GroupId, ct);
            var buyer = members.FirstOrDefault(m => m.UserId == ctx.UserId)?.DisplayName ?? "Ktoś";
            await notifier.NotifyUsersAsync(watchers, new LiveEvent(
                "shopping.bought", $"🛒 {buyer} kupił(a): {item.Name}", remainingText), ct);
        }

        return new ToolResult(ToolResultStatus.Success, null, null, $"✅ Kupione „{item.Name}”. {remainingText}");
    }
}

/// <summary>
/// family.shopping.notify_trip — "jadę na zakupy, powiadom Agatę i Olę": add named members as
/// watchers for a few hours (TTL). Default 4h.
/// </summary>
public sealed class NotifyTripTool(IShoppingRepository repo, IGroupDirectory dir) : ITool
{
    public string ToolId => "family.shopping.notify_trip";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("with", new JsonSchemaBuilder().Type(SchemaValueType.Array).Items(new JsonSchemaBuilder().Type(SchemaValueType.String))),
            ("hours", new JsonSchemaBuilder().Type(SchemaValueType.Number)))
        .Required("with").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var names = input.Arguments.TryGetProperty("with", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : new List<string>();
        var hours = input.Arguments.TryGetProperty("hours", out var h) && h.ValueKind == JsonValueKind.Number
            ? Math.Clamp(h.GetDouble(), 0.5, 24) : 4;
        var until = DateTimeOffset.UtcNow.AddHours(hours);

        var resolved = await dir.ResolveByNamesAsync(ctx.GroupId, names, ct);
        var groupId = Guid.Parse(ctx.GroupId);
        foreach (var m in resolved)
            await repo.AddWatcherAsync(groupId, Guid.Parse(m.UserId), until, ct);

        if (resolved.Count == 0)
            return new ToolResult(ToolResultStatus.Success, null, null,
                "Nie rozpoznałem żadnej z tych osób w domu. Nikogo nie powiadomię.");
        var who = string.Join(", ", resolved.Select(r => r.DisplayName));
        return new ToolResult(ToolResultStatus.Success, null, null,
            $"🔔 Będę powiadamiać {who} o tym co kupujesz (przez ~{hours:0}h).");
    }
}

/// <summary>family.shopping.watch — standing opt-in/out: "powiadamiaj mnie o zakupach" / "przestań".</summary>
public sealed class WatchShoppingTool(IShoppingRepository repo) : ITool
{
    public string ToolId => "family.shopping.watch";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("enabled", new JsonSchemaBuilder().Type(SchemaValueType.Boolean))).Required("enabled").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var enabled = input.Arguments.GetProperty("enabled").GetBoolean();
        var groupId = Guid.Parse(ctx.GroupId);
        var userId = Guid.Parse(ctx.UserId);
        if (enabled) await repo.AddWatcherAsync(groupId, userId, null, ct);
        else await repo.RemoveWatcherAsync(groupId, userId, ct);
        return new ToolResult(ToolResultStatus.Success, null, null,
            enabled ? "🔔 Będę Cię powiadamiać, gdy ktoś robi zakupy." : "🔕 Wyłączyłem powiadomienia o zakupach.");
    }
}

// ── UI-facing tools (invoked deterministically via /api/action; no handler) ───

/// <summary>family.shopping.board — checklist data (needed + bought-12h, with buyer name).</summary>
public sealed class ShoppingBoardTool(IShoppingRepository repo, IGroupDirectory dir) : ITool
{
    public string ToolId => "family.shopping.board";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Guest);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var items = await repo.BoardAsync(Guid.Parse(ctx.GroupId), ct);
        var members = await dir.GetMembersAsync(ctx.GroupId, ct);
        string? name(Guid? id) => id is null ? null : members.FirstOrDefault(m => m.UserId == id.ToString())?.DisplayName;
        var data = JsonSerializer.SerializeToElement(new
        {
            items = items.Select(i => new { id = i.Id, name = i.Name, quantity = i.Quantity, status = i.Status, boughtBy = name(i.BoughtBy) })
        });
        return new ToolResult(ToolResultStatus.Success, data, null);
    }
}

/// <summary>family.shopping.check — tap-to-check by id; first-wins + notify watchers.</summary>
public sealed class CheckShoppingTool(
    IShoppingRepository repo, IGroupDirectory dir, INotificationService notifier) : ITool
{
    public string ToolId => "family.shopping.check";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("itemId", new JsonSchemaBuilder().Type(SchemaValueType.String))).Required("itemId").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        if (!Guid.TryParse(input.Arguments.GetProperty("itemId").GetString(), out var itemId))
            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { ok = false }), null);
        var gid = Guid.Parse(ctx.GroupId);
        var item = await repo.MarkBoughtByIdAsync(gid, itemId, Guid.Parse(ctx.UserId), ct);
        if (item is null)
            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { ok = false }), null);

        var remaining = await repo.ListNeededAsync(gid, ct);
        var watchers = (await repo.ActiveWatchersAsync(gid, ct)).Select(g => g.ToString()).Where(u => u != ctx.UserId).ToList();
        if (watchers.Count > 0)
        {
            var members = await dir.GetMembersAsync(ctx.GroupId, ct);
            var buyer = members.FirstOrDefault(m => m.UserId == ctx.UserId)?.DisplayName ?? "Ktoś";
            var rem = remaining.Count == 0 ? "Lista pusta — wszystko kupione! ✅"
                : "Zostało: " + string.Join(", ", remaining.Select(r => r.Name));
            await notifier.NotifyUsersAsync(watchers, new LiveEvent("shopping.bought", $"🛒 {buyer} kupił(a): {item.Name}", rem), ct);
        }
        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { ok = true }), null);
    }
}

/// <summary>family.shopping.uncheck — undo a check by id.</summary>
public sealed class UncheckShoppingTool(IShoppingRepository repo) : ITool
{
    public string ToolId => "family.shopping.uncheck";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("itemId", new JsonSchemaBuilder().Type(SchemaValueType.String))).Required("itemId").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        if (!Guid.TryParse(input.Arguments.GetProperty("itemId").GetString(), out var itemId))
            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { ok = false }), null);
        var item = await repo.UncheckAsync(Guid.Parse(ctx.GroupId), itemId, ct);
        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { ok = item is not null }), null);
    }
}

// ── Providers + handlers ──────────────────────────────────────────────────────
public sealed class ShoppingListProvider(IShoppingRepository repo) : IContextProvider
{
    public string ProviderId => "group.shopping";
    public ContextScope Scope => ContextScope.Group;
    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var items = await repo.ListNeededAsync(Guid.Parse(req.ExecutionContext.GroupId), ct);
        return new ContextSlice(ProviderId, Scope, new { items = items.Select(i => new { i.Name, i.Quantity }) });
    }
}

public sealed class AddShoppingItemHandler : IIntentHandler
{
    public string IntentId => "family.add_shopping_item";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.shopping.add"];
    public string PromptTemplateId => "add_shopping_item";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class MarkShoppingBoughtHandler : IIntentHandler
{
    public string IntentId => "family.mark_shopping_bought";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["group.shopping"];
    public string[] AllowedTools => ["family.shopping.mark_bought"];
    public string PromptTemplateId => "mark_shopping_bought";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class NotifyTripHandler : IIntentHandler
{
    public string IntentId => "family.notify_shopping_trip";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.shopping.notify_trip"];
    public string PromptTemplateId => "notify_shopping_trip";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}

public sealed class WatchShoppingHandler : IIntentHandler
{
    public string IntentId => "family.set_shopping_notifications";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.shopping.watch"];
    public string PromptTemplateId => "set_shopping_notifications";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
