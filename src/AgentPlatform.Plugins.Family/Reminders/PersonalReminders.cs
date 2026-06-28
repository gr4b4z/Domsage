using System.Globalization;
using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Plugins.Family.Reminders;

/// <summary>
/// datetime.now — gives the planner the current wall-clock time in the user's zone so it can
/// resolve relative phrases ("jutro o 14", "za godzinę", "w poniedziałek") to an absolute local time.
/// </summary>
public sealed class NowContextProvider(AppDbContext core) : IContextProvider
{
    public string ProviderId => "datetime.now";
    public ContextScope Scope => ContextScope.User;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var tzId = await ResolveTimezoneAsync(core, req.ExecutionContext.UserId, ct);
        var tz = FamilyTime.Zone(tzId);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);

        var data = new
        {
            timezone = tzId,
            now = nowLocal.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            today = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tomorrow = nowLocal.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dayOfWeek = nowLocal.DateTime.DayOfWeek.ToString(),
        };
        return new ContextSlice(ProviderId, Scope, data);
    }

    internal static async Task<string> ResolveTimezoneAsync(AppDbContext core, string userId, CancellationToken ct)
    {
        if (Guid.TryParse(userId, out var uid))
        {
            var tz = await core.Users.AsNoTracking()
                .Where(u => u.Id == uid).Select(u => u.Timezone).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(tz)) return tz!;
        }
        return "Europe/Warsaw";
    }
}

internal static class FamilyTime
{
    /// <summary>IANA id → TimeZoneInfo (DST-aware), falling back to Europe/Warsaw then UTC.</summary>
    public static TimeZoneInfo Zone(string ianaId)
    {
        foreach (var id in new[] { ianaId, "Europe/Warsaw" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}

/// <summary>
/// family.reminders.create — schedules a one-off (or recurring) personal reminder. The "at" value is
/// a wall-clock local time in the user's zone; it is converted to a UTC instant (DST-safe) and stored
/// as a scheduler job. When it fires the user is pushed a notification (web SSE + their messaging channel).
/// </summary>
public sealed class CreateReminderTool(AppDbContext core, ISchedulerService scheduler) : ITool
{
    public string ToolId => "family.reminders.create";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder()
        .Type(SchemaValueType.Object)
        .Properties(
            ("text", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("at", new JsonSchemaBuilder().Type(SchemaValueType.String)
                .Description("Local wall-clock time, ISO 8601 without offset, e.g. 2026-06-29T14:00")),
            ("rrule", new JsonSchemaBuilder().Type(SchemaValueType.String)
                .Description("Optional recurrence, e.g. FREQ=DAILY / FREQ=WEEKLY / FREQ=MONTHLY")))
        .Required("text", "at")
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var text = input.Arguments.GetProperty("text").GetString()?.Trim();
        var atRaw = input.Arguments.GetProperty("at").GetString();
        if (string.IsNullOrWhiteSpace(text))
            return new ToolResult(ToolResultStatus.Failed, null, "empty text", "❓ Co mam przypomnieć?");
        if (!DateTime.TryParse(atRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new ToolResult(ToolResultStatus.Failed, null, "bad time", "❓ Nie zrozumiałem terminu przypomnienia.");

        string? rrule = input.Arguments.TryGetProperty("rrule", out var r)
            && r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString())
                ? r.GetString()!.ToUpperInvariant() : null;

        var tzId = await NowContextProvider.ResolveTimezoneAsync(core, ctx.UserId, ct);
        var tz = FamilyTime.Zone(tzId);

        // Treat the parsed value as a wall-clock local time, regardless of any kind/offset it carried.
        var localWall = DateTime.SpecifyKind(
            new DateTime(parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0),
            DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localWall, tz);
        var nextRunAt = new DateTimeOffset(utc, TimeSpan.Zero);

        if (rrule is null && nextRunAt <= DateTimeOffset.UtcNow.AddSeconds(5))
            return new ToolResult(ToolResultStatus.Failed, null, "past",
                "Ten termin już minął — podaj przyszłą godzinę.");

        var jobId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new SchedulerJob(
            Id: jobId,
            GroupId: null,               // personal — only this user is notified
            UserId: ctx.UserId,
            JobType: "reminder",
            PayloadJson: JsonSerializer.Serialize(new { text }),
            RRule: rrule,
            Timezone: tzId,
            NextRunAt: nextRunAt), ct);

        var when = localWall.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        var repeat = rrule switch
        {
            "FREQ=DAILY" => " (codziennie)",
            "FREQ=WEEKLY" => " (co tydzień)",
            "FREQ=MONTHLY" => " (co miesiąc)",
            _ => ""
        };
        return new ToolResult(ToolResultStatus.Success,
            JsonSerializer.SerializeToElement(new { id = jobId, at = nextRunAt, rrule }), null,
            $"⏰ Przypomnę Ci {when}{repeat}: {text}");
    }
}

/// <summary>family.set_reminder — "przypomnij mi jutro o 14 odebrać córkę ze szkoły".</summary>
public sealed class SetReminderHandler : IIntentHandler
{
    public string IntentId => "family.set_reminder";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["datetime.now"];
    public string[] AllowedTools => ["family.reminders.create"];
    public string PromptTemplateId => "set_reminder";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
