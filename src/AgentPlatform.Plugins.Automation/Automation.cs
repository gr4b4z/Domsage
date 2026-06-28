using System.Text.Json;
using AgentPlatform.Infrastructure.Automation;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Automation;

/// <summary>
/// automation.create — turns a natural-language rule ("sprawdzaj codziennie rano czy ma padać,
/// powiadom mnie") into a stored AutomationRuleEntity. The LLM (the handler) does the mapping once;
/// from then on the deterministic AutomationRunner executes it. Creating a rule is a side effect, so
/// it is confirmed first (the tool renders its own preview).
/// </summary>
public sealed class AutomationCreateTool(AppDbContext db) : ITool
{
    public string ToolId => "automation.create";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("description", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("tool_id", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("tool_input", new JsonSchemaBuilder().Type(SchemaValueType.Object)),
            ("condition_path", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("condition_op", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("condition_value", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("message", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("hour", new JsonSchemaBuilder().Type(SchemaValueType.Integer)),
            ("minute", new JsonSchemaBuilder().Type(SchemaValueType.Integer)),
            ("rrule", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("tool_id", "condition_path", "condition_op", "condition_value", "message")
        .Build();

    public string? ConfirmationPreview(JsonElement input)
    {
        var r = Parse(input);
        return $"📋 Utworzę regułę: codziennie o {r.Hour:00}:{r.Minute:00} sprawdzę „{r.ToolId}” " +
               $"i powiadomię, gdy {r.ConditionPath} {r.ConditionOp} {r.ConditionValue}.\n" +
               $"Treść: „{r.Message}”\nPotwierdzasz?";
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var r = Parse(input.Arguments);
        if (string.IsNullOrWhiteSpace(r.ToolId) || string.IsNullOrWhiteSpace(r.ConditionPath))
            return new ToolResult(ToolResultStatus.Failed, null, "incomplete rule",
                "❓ Nie zrozumiałem reguły — czego mam pilnować i kiedy?");

        const string tz = "Europe/Warsaw";
        var rule = new AutomationRuleEntity
        {
            UserId = Guid.Parse(ctx.UserId),
            GroupId = Guid.TryParse(ctx.GroupId, out var g) ? g : null,
            Description = r.Description,
            RRule = string.IsNullOrWhiteSpace(r.RRule) ? "FREQ=DAILY" : r.RRule,
            Timezone = tz,
            NextRunAt = AutomationSchedule.NextAtTime(
                Math.Clamp(r.Hour, 0, 23), Math.Clamp(r.Minute, 0, 59), tz, DateTimeOffset.UtcNow),
            ToolId = r.ToolId,
            ToolInput = r.ToolInput,
            ConditionPath = r.ConditionPath,
            ConditionOp = r.ConditionOp,
            ConditionValue = r.ConditionValue,
            MessageText = r.Message,
        };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync(ct);

        var data = JsonSerializer.SerializeToElement(new { id = rule.Id, nextRunAt = rule.NextRunAt });
        return new ToolResult(ToolResultStatus.Success, data, null,
            $"✅ Gotowe! Codziennie o {rule.NextRunAt.ToOffset(TimeSpan.FromHours(2)):HH:mm} sprawdzę i dam znać, gdy trzeba. " +
            "Powiesz „pokaż automatyzacje”, żeby je wylistować.");
    }

    private static RuleArgs Parse(JsonElement a) => new(
        Str(a, "description"), Str(a, "tool_id"),
        a.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object ? ti.GetRawText() : "{}",
        Str(a, "condition_path"), Def(Str(a, "condition_op"), ">="), Str(a, "condition_value"),
        Str(a, "message"), Int(a, "hour", 7), Int(a, "minute", 0), Str(a, "rrule"));

    private static string Str(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement a, string name, int dflt) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;
    private static string Def(string s, string dflt) => string.IsNullOrWhiteSpace(s) ? dflt : s;

    private sealed record RuleArgs(
        string Description, string ToolId, string ToolInput, string ConditionPath,
        string ConditionOp, string ConditionValue, string Message, int Hour, int Minute, string RRule);
}

/// <summary>automation.create — "sprawdzaj codziennie rano czy ma padać i daj znać, żebym wziął parasol".</summary>
public sealed class AutomationCreateHandler : IIntentHandler
{
    public string IntentId => "automation.create";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["automation.create"];
    public string PromptTemplateId => "automation_create";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;

    public string? Description =>
        "set up a recurring conditional alert ('sprawdzaj codziennie…', 'pilnuj…', 'powiadom mnie gdy…', " +
        "'co rano sprawdź… i daj znać') — schedule + a check + notify-if-condition";
}

/// <summary>The plugin — namespace "automation", no DB schema of its own (uses the core automation_rules table).</summary>
public sealed class AutomationPluginRegistration : IPluginRegistration
{
    public string Namespace => "automation";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ITool, AutomationCreateTool>();
        services.AddSingleton<IIntentHandler, AutomationCreateHandler>();
        services.AddScoped<ISlashCommand, ListAutomationsCommand>();
        services.AddScoped<ISlashCommand, DeleteAutomationCommand>();
    }
}
