using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.Infrastructure.Scheduler;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Automation;

/// <summary>
/// Generic "if-this-then-that" engine. Every minute it picks up due rules, runs a read-only tool,
/// evaluates a deterministic condition on the result, and notifies the owner only when it holds.
/// No LLM at run time — the LLM is used once, at rule creation, to author the rule. This keeps the
/// recurring cost at ~zero (the tool's own work + a comparison) and the decision deterministic.
/// A failing tool just skips that tick; the rule is always rescheduled so one bad fetch can't stall it.
/// </summary>
public sealed class AutomationRunner(
    AppDbContext db,
    PluginRegistry registry,
    IServiceProvider sp,
    IExecutionContextAccessor exec,
    INotificationService notifier,
    ILogger<AutomationRunner> log) : IScheduledJob
{
    public string JobId => "core.automation-scan";
    public string Cron => "* * * * *"; // every minute; per-rule timing is honored via NextRunAt

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.AutomationRules.AsNoTracking()
            .Where(r => r.Enabled && r.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var rule in due)
        {
            try { await FireAsync(rule, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Automation rule {Id} check failed", rule.Id); }
            finally { await AdvanceAsync(rule, ct); }
        }
    }

    private async Task FireAsync(AutomationRuleEntity rule, CancellationToken ct)
    {
        // Run the check under the owner's context so RLS-scoped tools see the right group.
        exec.Current = new ExecutionContext(
            Guid.NewGuid().ToString(), rule.UserId.ToString(),
            rule.GroupId?.ToString() ?? Guid.Empty.ToString(), "household",
            MemberRole.Admin, "scheduler", "", false, DateTimeOffset.UtcNow);

        ITool tool;
        try { tool = registry.ResolveTool(rule.ToolId, sp); }
        catch { log.LogWarning("Automation rule {Id}: unknown tool {Tool}", rule.Id, rule.ToolId); return; }

        using var inputDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rule.ToolInput) ? "{}" : rule.ToolInput);
        var result = await tool.ExecuteAsync(new ToolInput(rule.ToolId, inputDoc.RootElement), exec.Current, ct);
        if (result.Status != ToolResultStatus.Success || result.Data is not { } data) return;

        if (!JsonCondition.Evaluate(data, rule.ConditionPath, rule.ConditionOp, rule.ConditionValue)) return;

        var title = "🔔 " + (string.IsNullOrWhiteSpace(rule.Description) ? "Automatyzacja" : rule.Description);
        var body = JsonCondition.Render(rule.MessageText, data, rule.ConditionPath);
        await notifier.NotifyUsersAsync([rule.UserId.ToString()], new LiveEvent("automation", title, body), ct);
        await db.AutomationRules.Where(r => r.Id == rule.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastTriggeredAt, DateTimeOffset.UtcNow), ct);
        log.LogInformation("Automation rule {Id} triggered for user {User}", rule.Id, rule.UserId);
    }

    private async Task AdvanceAsync(AutomationRuleEntity rule, CancellationToken ct)
    {
        var next = HangfireSchedulerService.NextOccurrence(rule.RRule, rule.Timezone, rule.NextRunAt);
        var floor = DateTimeOffset.UtcNow;
        var guard = 0;
        while (next <= floor && guard++ < 1000)
            next = HangfireSchedulerService.NextOccurrence(rule.RRule, rule.Timezone, next);
        // Npgsql requires UTC (offset 0) for 'timestamp with time zone'; NextOccurrence returns a zoned offset.
        var nextUtc = next.ToUniversalTime();
        await db.AutomationRules.Where(r => r.Id == rule.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.NextRunAt, nextUtc)
                .SetProperty(x => x.LastFiredAt, DateTimeOffset.UtcNow), ct);
    }
}
