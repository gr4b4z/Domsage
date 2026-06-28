using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.Plugins.Family.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Plugins.Family.Reminders;

public sealed class ReminderOptions
{
    /// <summary>If still unpaid this long after the first reminder, escalate (re-broadcast to all).</summary>
    public TimeSpan PaymentEscalateAfter { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Recurring scan: lead-time reminders for due payments/renewals (broadcast to the whole group),
/// then escalation — if still unpaid after the window, re-broadcast to everyone. Per-group RLS
/// context is set so it works under a non-superuser DB role too. Audited; anti-spam via
/// reminded_at / escalated_at flags.
/// </summary>
public sealed class ReminderScanner(
    AppDbContext core,
    IPaymentsRepository payments,
    IRenewalsRepository renewals,
    IExecutionContextAccessor exec,
    IGroupDirectory dir,
    INotificationService notifier,
    IAuditLogRepository audit,
    Microsoft.Extensions.Options.IOptions<ReminderOptions> options,
    ILogger<ReminderScanner> log) : IScheduledJob
{
    private readonly ReminderOptions _opts = options.Value;

    public string JobId => "family.reminder-scan";
    public string Cron => "0 * * * *"; // hourly
    public Task RunAsync(CancellationToken ct) => ScanAsync(ct);

    public async Task ScanAsync(CancellationToken ct)
    {
        var groups = await core.Groups.AsNoTracking()
            .Where(g => g.Type == "household")
            .Select(g => new { g.Id, g.Type }).ToListAsync(ct);

        foreach (var g in groups)
        {
            // Set RLS context for this group's family.* queries.
            exec.Current = new ExecutionContext(
                Guid.NewGuid().ToString(), Guid.Empty.ToString(), g.Id.ToString(), g.Type,
                MemberRole.Admin, "scheduler", "", false, DateTimeOffset.UtcNow);

            var members = (await dir.GetMembersAsync(g.Id.ToString(), ct)).Select(m => m.UserId).ToList();
            if (members.Count == 0) continue;

            await ScanPaymentsAsync(g.Id, g.Type, members, ct);
            await ScanRenewalsAsync(g.Id, g.Type, members, ct);
        }
    }

    private async Task ScanPaymentsAsync(Guid groupId, string groupType, List<string> members, CancellationToken ct)
    {
        foreach (var p in await payments.DueForReminderAsync(groupId, ct))
        {
            await notifier.NotifyUsersAsync(members, new LiveEvent("payment.reminder",
                $"💸 Przypomnienie: {p.Creditor} {p.Amount} {p.Currency}",
                $"Termin {p.DueDate:yyyy-MM-dd}. Oznacz jako zapłacone gdy zapłacisz.",
                ActionToolId: "family.payments.mark_paid", ActionInput: PaymentAck(p.Id),
                ActionLabel: "✅ Zapłacone"), ct);
            await payments.MarkRemindedAsync(p.Id, ct);
            await Audit(groupId, groupType, "family.payment_reminder", p.Id.ToString(), ct);
        }

        foreach (var p in await payments.DueForEscalationAsync(groupId, _opts.PaymentEscalateAfter, ct))
        {
            await notifier.NotifyUsersAsync(members, new LiveEvent("payment.escalation",
                $"⏰ WCIĄŻ NIEZAPŁACONE: {p.Creditor} {p.Amount} {p.Currency}",
                $"Termin {p.DueDate:yyyy-MM-dd} — nikt jeszcze nie oznaczył jako zapłacone. Kto to ogarnie?",
                ActionToolId: "family.payments.mark_paid", ActionInput: PaymentAck(p.Id),
                ActionLabel: "✅ Zapłacone"), ct);
            await payments.MarkEscalatedAsync(p.Id, ct);
            await Audit(groupId, groupType, "family.payment_escalation", p.Id.ToString(), ct);
        }
    }

    private async Task ScanRenewalsAsync(Guid groupId, string groupType, List<string> members, CancellationToken ct)
    {
        foreach (var r in await renewals.DueForReminderAsync(groupId, ct))
        {
            await notifier.NotifyUsersAsync(members, new LiveEvent("renewal.reminder",
                $"🔔 {r.Label} ({r.Category}) wkrótce wygasa",
                $"Wygasa {r.ExpiresOn:yyyy-MM-dd} — zaplanuj odnowienie.",
                ActionToolId: "family.renewals.mark_renewed", ActionInput: RenewalAck(r.Id),
                ActionLabel: "✅ Odnowione"), ct);
            await renewals.MarkRemindedAsync(r.Id, ct);
            await Audit(groupId, groupType, "family.renewal_reminder", r.Id.ToString(), ct);
        }

        foreach (var r in await renewals.DueForEscalationAsync(groupId, ct))
        {
            await notifier.NotifyUsersAsync(members, new LiveEvent("renewal.escalation",
                $"⏰ {r.Label} ({r.Category}) — wygasa {r.ExpiresOn:yyyy-MM-dd}",
                "Wciąż nieodnowione. Przypomnienie dla wszystkich.",
                ActionToolId: "family.renewals.mark_renewed", ActionInput: RenewalAck(r.Id),
                ActionLabel: "✅ Odnowione"), ct);
            await renewals.MarkEscalatedAsync(r.Id, ct);
            await Audit(groupId, groupType, "family.renewal_escalation", r.Id.ToString(), ct);
        }
        log.LogInformation("Reminder scan done for group {Group}", groupId);
    }

    private static string PaymentAck(Guid id) => $"{{\"paymentId\":\"{id}\"}}";
    private static string RenewalAck(Guid id) => $"{{\"renewalId\":\"{id}\"}}";

    private Task Audit(Guid groupId, string groupType, string intent, string targetId, CancellationToken ct) =>
        audit.WriteAsync(new AuditEntry(
            Guid.Empty.ToString(), groupId.ToString(), groupType, intent, "scheduler",
            null, targetId, "success", null, null, "scheduler", "None", 0, 0, 0m, 0, null), ct);
}
