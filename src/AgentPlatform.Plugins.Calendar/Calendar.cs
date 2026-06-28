using System.Globalization;
using System.Text.Json;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Calendar;

/// <summary>Gives the planner today's date/weekday so it can resolve "wtorek 9:00" / "jutro" to a concrete time.</summary>
public sealed class NowContextProvider : IContextProvider
{
    public string ProviderId => "calendar.now";
    public ContextScope Scope => ContextScope.User;

    public Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CalTime.Zone);
        return Task.FromResult(new ContextSlice(ProviderId, Scope, new
        {
            today = now.ToString("yyyy-MM-dd"),
            weekday = now.ToString("dddd", new CultureInfo("pl-PL")),
            localTime = now.ToString("HH:mm"),
            timeZone = "Europe/Warsaw"
        }));
    }
}

/// <summary>Resolves the backend for whichever calendar provider the user has connected.</summary>
internal static class CalendarResolver
{
    public static async Task<ICalendarBackend?> ForUserAsync(
        IEnumerable<IOAuthTokenProvider> providers, IEnumerable<ICalendarBackend> backends,
        string userId, CancellationToken ct)
    {
        foreach (var p in providers)
            if (await p.IsConnectedAsync(userId, ct))
            {
                var b = backends.FirstOrDefault(x => x.Provider == p.Provider);
                if (b is not null) return b;
            }
        return null;
    }
}

/// <summary>calendar.add_event — adds an event to the user's own calendar and (optionally) invites others.</summary>
public sealed class CalendarAddEventTool(
    IEnumerable<IOAuthTokenProvider> tokenProviders,
    IEnumerable<ICalendarBackend> backends,
    AppDbContext db) : ITool
{
    public string ToolId => "calendar.add_event";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("title", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("start", new JsonSchemaBuilder().Type(SchemaValueType.String).Description("ISO local wall-clock, e.g. 2026-07-01T09:00")),
            ("end", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("durationMinutes", new JsonSchemaBuilder().Type(SchemaValueType.Integer)),
            ("attendees", new JsonSchemaBuilder().Type(SchemaValueType.Array).Items(new JsonSchemaBuilder().Type(SchemaValueType.String))),
            ("shareWithGroup", new JsonSchemaBuilder().Type(SchemaValueType.Boolean)),
            ("location", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("description", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("title", "start")
        .Build();

    public string? ConfirmationPreview(JsonElement input)
    {
        var title = Str(input, "title");
        var startTxt = Str(input, "start");
        var when = DateTimeOffset.TryParse(startTxt, out _)
            ? TimeZoneInfo.ConvertTime(CalTime.ParseLocal(startTxt), CalTime.Zone).ToString("dd.MM HH:mm")
            : startTxt;
        var share = Bool(input, "shareWithGroup");
        var who = share ? " — zaproszę rodzinę"
            : StrArray(input, "attendees") is { Length: > 0 } a ? $" — zaproszę: {string.Join(", ", a)}" : "";
        return $"📅 Dodam do kalendarza: «{title}» {when}{who}.\nPotwierdzasz?";
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var a = input.Arguments;
        var title = Str(a, "title");
        var startTxt = Str(a, "start");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(startTxt))
            return new ToolResult(ToolResultStatus.Failed, null, "missing title/start", "❓ Czego i na kiedy mam dodać wpis?");

        var backend = await CalendarResolver.ForUserAsync(tokenProviders, backends, ctx.UserId, ct);
        if (backend is null)
            return new ToolResult(ToolResultStatus.Failed, null, "not connected",
                "📅 Najpierw połącz kalendarz: napisz /connect-google");

        DateTimeOffset start;
        try { start = CalTime.ParseLocal(startTxt); }
        catch { return new ToolResult(ToolResultStatus.Failed, null, "bad start", "❓ Nie zrozumiałem daty/godziny."); }

        var endTxt = Str(a, "end");
        DateTimeOffset end = !string.IsNullOrWhiteSpace(endTxt) && TryParse(endTxt, out var e)
            ? e
            : start.AddMinutes(Int(a, "durationMinutes", 60));

        var attendees = await ResolveAttendeesAsync(a, ctx, ct);
        var ev = new CalendarEvent(title, start, end, Str(a, "description"), Str(a, "location"), attendees);

        try
        {
            var link = await backend.AddEventAsync(ctx.UserId, ev, sendInvites: attendees.Count > 0, ct);
            var local = TimeZoneInfo.ConvertTime(start, CalTime.Zone);
            var invited = attendees.Count > 0 ? $" Zaproszenia wysłane ({attendees.Count})." : "";
            var data = JsonSerializer.SerializeToElement(new { title, start = local.ToString("o"), attendees, link });
            return new ToolResult(ToolResultStatus.Success, data, null,
                $"📅 Dodano: «{title}» {local:dd.MM HH:mm}.{invited}");
        }
        catch (CalendarNotConnectedException)
        {
            return new ToolResult(ToolResultStatus.Failed, null, "not connected", "📅 Najpierw połącz kalendarz: /connect-google");
        }
        catch (Exception ex)
        {
            return new ToolResult(ToolResultStatus.Failed, null, $"calendar failed: {ex.Message}",
                "📅 Nie udało się dodać wpisu — spróbuj ponownie później.");
        }
    }

    // shareWithGroup → all members' primary emails; named attendees resolved against members, emails passed through.
    private async Task<IReadOnlyList<string>> ResolveAttendeesAsync(JsonElement a, ExecutionContext ctx, CancellationToken ct)
    {
        var result = new List<string>();
        List<(string Name, string Email)> members = [];
        if (Guid.TryParse(ctx.GroupId, out var gid))
            members = await db.ChannelIdentities.AsNoTracking()
                .Where(ci => ci.ChannelId == "email" && ci.IsPrimary
                    && db.GroupMembers.Any(gm => gm.GroupId == gid && gm.UserId == ci.UserId))
                .Join(db.Users, ci => ci.UserId, u => u.Id, (ci, u) => new { u.DisplayName, ci.ExternalId })
                .Select(x => new ValueTuple<string, string>(x.DisplayName, x.ExternalId))
                .ToListAsync(ct);

        if (Bool(a, "shareWithGroup"))
            result.AddRange(members.Select(m => m.Email));

        foreach (var raw in StrArray(a, "attendees") ?? [])
        {
            var v = raw.Trim();
            if (v.Contains('@')) { result.Add(v.ToLowerInvariant()); continue; }
            var hit = members.FirstOrDefault(m =>
                m.Name.Contains(v, StringComparison.OrdinalIgnoreCase) || v.Contains(m.Name, StringComparison.OrdinalIgnoreCase));
            if (hit.Email is not null) result.Add(hit.Email);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryParse(string s, out DateTimeOffset o)
    {
        try { o = CalTime.ParseLocal(s); return true; } catch { o = default; return false; }
    }

    private static string Str(JsonElement a, string n) =>
        a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement a, string n, int d) =>
        a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
    private static bool Bool(JsonElement a, string n) =>
        a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static string[]? StrArray(JsonElement a, string n) =>
        a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : null;
}

/// <summary>calendar.agenda — reads the user's upcoming events ("co mam jutro?").</summary>
public sealed class CalendarAgendaTool(
    IEnumerable<IOAuthTokenProvider> tokenProviders, IEnumerable<ICalendarBackend> backends) : ITool
{
    public string ToolId => "calendar.agenda";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("day", new JsonSchemaBuilder().Type(SchemaValueType.String).Description("today | tomorrow | week")))
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var backend = await CalendarResolver.ForUserAsync(tokenProviders, backends, ctx.UserId, ct);
        if (backend is null)
            return new ToolResult(ToolResultStatus.Failed, null, "not connected", "📅 Najpierw połącz kalendarz: /connect-google");

        var day = (input.Arguments.TryGetProperty("day", out var d) ? d.GetString() ?? "today" : "today").ToLowerInvariant();
        var (from, to) = Range(day);

        try
        {
            var events = await backend.ListAsync(ctx.UserId, from, to, ct);
            var items = events.Select(e => new
            {
                title = e.Title,
                start = TimeZoneInfo.ConvertTime(e.Start, CalTime.Zone).ToString("yyyy-MM-dd HH:mm"),
                location = e.Location
            }).ToArray();
            var msg = items.Length == 0 ? "📅 Brak wydarzeń w tym okresie."
                : "📅 " + string.Join("; ", items.Select(i => $"{i.start} {i.title}"));
            return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { day, events = items }), null, msg);
        }
        catch (CalendarNotConnectedException)
        {
            return new ToolResult(ToolResultStatus.Failed, null, "not connected", "📅 Najpierw połącz kalendarz: /connect-google");
        }
        catch (Exception ex)
        {
            return new ToolResult(ToolResultStatus.Failed, null, $"calendar failed: {ex.Message}",
                "📅 Nie mogę teraz odczytać kalendarza — spróbuj później.");
        }
    }

    private static (DateTimeOffset From, DateTimeOffset To) Range(string day)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CalTime.Zone);
        var todayStart = ToUtc(nowLocal.Date);
        return day switch
        {
            "tomorrow" => (ToUtc(nowLocal.Date.AddDays(1)), ToUtc(nowLocal.Date.AddDays(2))),
            "week" => (DateTimeOffset.UtcNow, ToUtc(nowLocal.Date.AddDays(7))),
            _ => (todayStart, ToUtc(nowLocal.Date.AddDays(1)))
        };
    }

    private static DateTimeOffset ToUtc(DateTime localDate) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), CalTime.Zone), TimeSpan.Zero);
}

public sealed class CalendarAddEventHandler : IIntentHandler
{
    public string IntentId => "calendar.add_event";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["calendar.now"];
    public string[] AllowedTools => ["calendar.add_event"];
    public string PromptTemplateId => "calendar_add_event";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;
    public string? Description =>
        "add a calendar event / meeting and optionally invite people ('dodaj do kalendarza', 'umów spotkanie', 'zapisz wizytę … i zaproś …')";
}

public sealed class CalendarAgendaHandler : IIntentHandler
{
    public string IntentId => "calendar.agenda";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => ["calendar.now"];
    public string[] AllowedTools => ["calendar.agenda"];
    public string PromptTemplateId => "calendar_agenda";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
    public bool PhraseResult => true;
    public string? Description => "read upcoming calendar events ('co mam jutro?', 'jaki mam plan na weekend', 'co w kalendarzu')";
}

/// <summary>Provider-agnostic calendar capability. Google backend now; Outlook is a second ICalendarBackend later.</summary>
public sealed class CalendarPluginRegistration : IPluginRegistration
{
    public string Namespace => "calendar";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient();
        services.AddScoped<ICalendarBackend, GoogleCalendarBackend>();
        services.AddScoped<ITool, CalendarAddEventTool>();
        services.AddScoped<ITool, CalendarAgendaTool>();
        services.AddSingleton<IIntentHandler, CalendarAddEventHandler>();
        services.AddSingleton<IIntentHandler, CalendarAgendaHandler>();
        services.AddScoped<IContextProvider, NowContextProvider>();
    }
}
