using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace AgentPlatform.Infrastructure.Scheduler;

/// <summary>Hangfire-backed scheduler. Stores jobs in scheduler_jobs; recurrence is DST-safe via NodaTime.</summary>
public sealed class HangfireSchedulerService(
    AppDbContext db,
    IBackgroundJobClient hangfire) : ISchedulerService
{
    public async Task ScheduleAsync(SchedulerJob job, CancellationToken ct)
    {
        db.SchedulerJobs.Add(new SchedulerJobEntity
        {
            Id = job.Id,
            GroupId = Guid.TryParse(job.GroupId, out var g) ? g : null,
            UserId = Guid.TryParse(job.UserId, out var u) ? u : null,
            JobType = job.JobType,
            Payload = job.PayloadJson,
            RRule = job.RRule,
            Timezone = job.Timezone,
            NextRunAt = job.NextRunAt,
            Status = "active",
        });
        await db.SaveChangesAsync(ct);

        var delay = job.NextRunAt - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        hangfire.Schedule<ReminderDispatcher>(d => d.FireAsync(job.Id, CancellationToken.None), delay);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        await db.SchedulerJobs.Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "cancelled"), ct);
    }

    /// <summary>Computes the next occurrence in the job's zone (DST-safe). Supports DAILY/WEEKLY/MONTHLY.</summary>
    public static DateTimeOffset NextOccurrence(string rrule, string timezone, DateTimeOffset from)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) ?? DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
        var local = Instant.FromDateTimeOffset(from).InZone(zone).LocalDateTime;
        var freq = ParseFreq(rrule);
        var next = freq switch
        {
            "DAILY" => local.PlusDays(1),
            "WEEKLY" => local.PlusWeeks(1),
            "MONTHLY" => local.PlusMonths(1),
            "YEARLY" => local.PlusYears(1),
            _ => local.PlusDays(1)
        };
        return zone.AtLeniently(next).ToDateTimeOffset();
    }

    private static string ParseFreq(string rrule)
    {
        foreach (var part in rrule.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("FREQ", StringComparison.OrdinalIgnoreCase))
                return kv[1].ToUpperInvariant();
        }
        return "DAILY";
    }
}

/// <summary>
/// Hangfire job target — resolves recipients and DELIVERS the reminder to them (web SSE + their
/// messaging channel) via the notification service. A reminder is outbound: it is pushed to the
/// user, not re-injected into the bus as if it were an inbound message.
/// </summary>
public sealed class ReminderDispatcher(
    AppDbContext db,
    INotificationService notifier,
    ILogger<ReminderDispatcher> log)
{
    public async Task FireAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.SchedulerJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is null || job.Status != "active") return;

        using var doc = JsonDocument.Parse(job.Payload);
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

        var recipients = new List<string>();
        if (job.UserId is { } uid)
        {
            recipients.Add(uid.ToString());
        }
        else if (job.GroupId is { } gid)
        {
            var members = await db.GroupMembers.AsNoTracking()
                .Where(m => m.GroupId == gid)
                .Select(m => m.UserId.ToString())
                .ToListAsync(ct);
            recipients.AddRange(members);
        }

        if (recipients.Count > 0)
            await notifier.NotifyUsersAsync(recipients,
                new LiveEvent("reminder", "⏰ Przypomnienie", text), ct);

        // Reschedule recurring jobs.
        if (!string.IsNullOrEmpty(job.RRule))
        {
            var next = HangfireSchedulerService.NextOccurrence(job.RRule, job.Timezone, DateTimeOffset.UtcNow);
            await db.SchedulerJobs.Where(x => x.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.NextRunAt, next)
                    .SetProperty(x => x.LastRunAt, DateTimeOffset.UtcNow), ct);
        }
        else
        {
            await db.SchedulerJobs.Where(x => x.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "completed")
                    .SetProperty(x => x.LastRunAt, DateTimeOffset.UtcNow), ct);
        }
        log.LogInformation("Reminder job {JobId} fired to {Count} recipients", jobId, recipients.Count);
    }
}
