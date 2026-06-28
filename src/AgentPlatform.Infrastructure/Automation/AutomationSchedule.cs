using NodaTime;

namespace AgentPlatform.Infrastructure.Automation;

/// <summary>DST-safe schedule math for automation rules (NodaTime-backed, like the reminder scheduler).</summary>
public static class AutomationSchedule
{
    /// <summary>The next time it is <paramref name="hour"/>:<paramref name="minute"/> local in the given zone.</summary>
    public static DateTimeOffset NextAtTime(int hour, int minute, string timezone, DateTimeOffset from)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) ?? DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
        var nowLocal = Instant.FromDateTimeOffset(from).InZone(zone).LocalDateTime;
        var todayAt = new LocalDateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, hour, minute);
        var target = todayAt > nowLocal ? todayAt : todayAt.PlusDays(1);
        // UTC: Npgsql rejects a zoned offset for 'timestamp with time zone'.
        return zone.AtLeniently(target).ToDateTimeOffset().ToUniversalTime();
    }
}
