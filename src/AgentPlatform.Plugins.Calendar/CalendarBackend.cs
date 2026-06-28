using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Calendar;

public sealed record CalendarEvent(
    string Title, DateTimeOffset Start, DateTimeOffset End,
    string? Description, string? Location, IReadOnlyList<string> Attendees);

/// <summary>Raised when the user hasn't connected the calendar provider yet (handled gracefully by the tool).</summary>
public sealed class CalendarNotConnectedException() : Exception("calendar not connected");

/// <summary>A non-success response from the calendar API — carries the provider's error message for diagnosis.</summary>
public sealed class GoogleApiException(int status, string body) : Exception($"Google {status}: {Extract(body)}")
{
    public int Status { get; } = status;

    private static string Extract(string body)
    {
        try
        {
            using var d = JsonDocument.Parse(body);
            if (d.RootElement.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m)) return m.GetString() ?? body;
                return e.ToString();
            }
        }
        catch { /* not JSON */ }
        return body.Length > 300 ? body[..300] : body;
    }
}

/// <summary>A calendar provider behind the provider-agnostic capability. Google now; Outlook is a drop-in second impl.</summary>
public interface ICalendarBackend
{
    string Provider { get; }
    Task<string?> AddEventAsync(string userId, CalendarEvent ev, bool sendInvites, CancellationToken ct);
    Task<IReadOnlyList<CalendarEvent>> ListAsync(string userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Timezone helpers shared by the calendar plugin (Europe/Warsaw wall-clock ⇄ UTC).</summary>
public static class CalTime
{
    public static TimeZoneInfo Zone
    {
        get { try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); } catch { return TimeZoneInfo.Utc; } }
    }

    /// <summary>Parses an ISO string. With an explicit offset → that instant; bare wall-clock → Europe/Warsaw local.</summary>
    public static DateTimeOffset ParseLocal(string s)
    {
        s = s.Trim();
        if (HasOffset(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return dto.ToUniversalTime();
        var local = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static bool HasOffset(string s) => s.EndsWith('Z') || Regex.IsMatch(s, @"[+\-]\d\d:?\d\d$");

    /// <summary>RFC3339 in Warsaw local offset (for Google's start/end.dateTime).</summary>
    public static string ToLocalRfc3339(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Zone).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
}

/// <summary>Google Calendar via the REST API, using the user's OAuth access token from the Google provider.</summary>
public sealed class GoogleCalendarBackend(IEnumerable<IOAuthTokenProvider> tokenProviders, IHttpClientFactory http) : ICalendarBackend
{
    public string Provider => "google";

    private IOAuthTokenProvider Google =>
        tokenProviders.First(p => p.Provider == "google");

    public async Task<string?> AddEventAsync(string userId, CalendarEvent ev, bool sendInvites, CancellationToken ct)
    {
        var token = await Google.GetAccessTokenAsync(userId, ct) ?? throw new CalendarNotConnectedException();

        var body = new Dictionary<string, object?>
        {
            ["summary"] = ev.Title,
            ["start"] = new { dateTime = CalTime.ToLocalRfc3339(ev.Start), timeZone = "Europe/Warsaw" },
            ["end"] = new { dateTime = CalTime.ToLocalRfc3339(ev.End), timeZone = "Europe/Warsaw" },
        };
        if (!string.IsNullOrWhiteSpace(ev.Description)) body["description"] = ev.Description;
        if (!string.IsNullOrWhiteSpace(ev.Location)) body["location"] = ev.Location;
        if (ev.Attendees.Count > 0) body["attendees"] = ev.Attendees.Select(e => new { email = e }).ToArray();

        var sendUpdates = sendInvites && ev.Attendees.Count > 0 ? "all" : "none";
        var url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events?sendUpdates={sendUpdates}";

        var client = http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new GoogleApiException((int)resp.StatusCode, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("htmlLink", out var link) ? link.GetString() : null;
    }

    public async Task<IReadOnlyList<CalendarEvent>> ListAsync(string userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var token = await Google.GetAccessTokenAsync(userId, ct) ?? throw new CalendarNotConnectedException();
        var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events"
                + $"?timeMin={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}"
                + $"&timeMax={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}"
                + "&singleEvents=true&orderBy=startTime&maxResults=25";

        var client = http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req, ct);
        var listText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new GoogleApiException((int)resp.StatusCode, listText);
        using var doc = JsonDocument.Parse(listText);

        var list = new List<CalendarEvent>();
        if (doc.RootElement.TryGetProperty("items", out var items))
            foreach (var it in items.EnumerateArray())
            {
                var title = it.TryGetProperty("summary", out var s) ? s.GetString() ?? "(bez tytułu)" : "(bez tytułu)";
                var start = ReadWhen(it, "start");
                var end = ReadWhen(it, "end");
                if (start is null) continue;
                list.Add(new CalendarEvent(title, start.Value, end ?? start.Value, null,
                    it.TryGetProperty("location", out var loc) ? loc.GetString() : null, []));
            }
        return list;
    }

    private static DateTimeOffset? ReadWhen(JsonElement ev, string field)
    {
        if (!ev.TryGetProperty(field, out var w)) return null;
        if (w.TryGetProperty("dateTime", out var dt) && DateTimeOffset.TryParse(dt.GetString(), out var o)) return o;
        if (w.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), out var day))
            return new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeSpan.Zero);
        return null;
    }
}
