using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Meter;

/// <summary>Records every LLM call and answers spend queries. Scoped.</summary>
public sealed class PostgresUsageMeter(AppDbContext db) : IUsageMeter
{
    public async Task RecordAsync(UsageEvent e, CancellationToken ct)
    {
        db.UsageMeterEvents.Add(new UsageMeterEventEntity
        {
            UserId = Guid.TryParse(e.UserId, out var uid) ? uid : null,
            GroupId = Guid.TryParse(e.GroupId, out var gid) ? gid : null,
            ProviderId = e.ProviderId,
            ModelTier = e.Tier.ToString(),
            PromptVersion = e.PromptVersion,
            InputTokens = e.InputTokens,
            OutputTokens = e.OutputTokens,
            CachedTokens = e.CachedTokens,
            CostUsd = e.CostUsd,
            Intent = e.Intent,
            RequestId = Guid.TryParse(e.RequestId, out var rid) ? rid : Guid.NewGuid(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<SpendSnapshot> GetSpendAsync(BudgetScope scope, Window window, CancellationToken ct)
    {
        var spent = await db.UsageMeterEvents.AsNoTracking()
            .Where(x => x.OccurredAt >= window.From && x.OccurredAt <= window.To)
            .SumAsync(x => (decimal?)x.CostUsd, ct) ?? 0m;
        var state = await db.BudgetStates.AsNoTracking().FirstOrDefaultAsync(x => x.ScopeKey == scope.Key, ct);
        return new SpendSnapshot(scope, spent, state?.Tripped ?? false);
    }
}
