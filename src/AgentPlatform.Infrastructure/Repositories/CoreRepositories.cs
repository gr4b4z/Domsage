using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Repositories;

public sealed class AuditLogRepository(AppDbContext db) : IAuditLogRepository
{
    public async Task WriteAsync(AuditEntry e, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntity
        {
            UserId = ParseGuid(e.UserId),
            GroupId = ParseGuid(e.GroupId),
            GroupType = e.GroupType,
            Intent = e.Intent,
            PlannerMode = e.PlannerMode,
            ToolId = e.ToolId,
            TargetId = e.TargetId,
            Result = e.Result,
            ErrorMessage = e.ErrorMessage,
            IdempotencyKey = e.IdempotencyKey,
            PromptVersion = e.PromptVersion,
            ModelTier = e.ModelTier,
            InputTokens = e.InputTokens,
            OutputTokens = e.OutputTokens,
            CostUsd = e.CostUsd,
            DiagnosticSteps = e.DiagnosticSteps,
            ContextFetched = e.ContextFetched,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditActionRecord>> SearchActionsAsync(
        string? groupId, string query, int limit, CancellationToken ct)
    {
        Guid? gid = Guid.TryParse(groupId, out var g) ? g : null;
        var q = db.AuditLog.AsNoTracking().Where(a => a.GroupId == gid && a.Result == "success");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = $"%{query}%";
            q = q.Where(a => EF.Functions.ILike(a.Intent, like)
                || (a.ToolId != null && EF.Functions.ILike(a.ToolId, like))
                || (a.TargetId != null && EF.Functions.ILike(a.TargetId, like)));
        }
        var rows = await q.OrderByDescending(a => a.OccurredAt).Take(limit).ToListAsync(ct);
        return rows.Select(a => new AuditActionRecord(a.Intent, a.ToolId, a.TargetId, a.Result, a.OccurredAt)).ToList();
    }

    private static Guid? ParseGuid(string? s) => Guid.TryParse(s, out var g) ? g : null;
}

public sealed class IdempotencyRepository(AppDbContext db) : IIdempotencyRepository
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<bool> TryAcquireAsync(string key, CancellationToken ct)
    {
        // INSERT ON CONFLICT DO NOTHING — placeholder row claims the key.
        var rows = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO idempotency_keys (key, result, created_at, expires_at) " +
            "VALUES ({0}, '{{}}'::jsonb, NOW(), NOW() + INTERVAL '7 days') ON CONFLICT (key) DO NOTHING",
            key);
        return rows == 1;
    }

    public async Task<ToolResult> GetCachedResultAsync(string key, CancellationToken ct)
    {
        var row = await db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null || row.Result == "{}")
            return new ToolResult(ToolResultStatus.Success, null, null, "✅ Już wykonane.");
        return JsonSerializer.Deserialize<ToolResult>(row.Result, Json)
               ?? new ToolResult(ToolResultStatus.Success, null, null);
    }

    public async Task StoreResultAsync(string key, ToolResult result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, Json);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE idempotency_keys SET result = {0}::jsonb WHERE key = {1}", json, key);
    }

    public async Task ReleaseAsync(string key, CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync("DELETE FROM idempotency_keys WHERE key = {0}", key);
}

public sealed class PendingIntentRepository(AppDbContext db) : IPendingIntentRepository
{
    public async Task<PendingIntent?> GetActiveAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return null;
        var row = await db.PendingIntents.AsNoTracking()
            .Where(x => x.UserId == uid && x.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new PendingIntent(userId, row.GroupId?.ToString(), row.IntentId,
            new Dictionary<string, string>(), row.MissingSlots) { Id = row.Id };
    }

    public async Task SaveAsync(PendingIntent intent, CancellationToken ct)
    {
        db.PendingIntents.Add(new PendingIntentEntity
        {
            Id = intent.Id,
            UserId = Guid.Parse(intent.UserId),
            GroupId = Guid.TryParse(intent.GroupId, out var g) ? g : null,
            IntentId = intent.IntentId,
            MissingSlots = intent.MissingSlots.ToArray(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(Guid id, CancellationToken ct) =>
        await db.PendingIntents.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
}

public sealed class PendingConfirmationRepository(AppDbContext db) : IPendingConfirmationRepository
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<string> SaveAsync(ActionPlan plan, ExecutionContext ctx, CancellationToken ct)
    {
        var entity = new PendingConfirmationEntity
        {
            UserId = Guid.Parse(ctx.UserId),
            GroupId = Guid.TryParse(ctx.GroupId, out var g) ? g : null,
            ChannelId = ctx.ChannelId,
            ActionPlan = JsonSerializer.Serialize(plan, Json),
        };
        db.PendingConfirmations.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id.ToString();
    }

    public async Task<PendingConfirmation?> GetAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var row = await db.PendingConfirmations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gid && x.ExpiresAt > DateTimeOffset.UtcNow, ct);
        if (row is null) return null;
        var plan = JsonSerializer.Deserialize<ActionPlan>(row.ActionPlan, Json);
        return plan is null ? null : new PendingConfirmation(id, plan);
    }

    public async Task RecordSignalAsync(string id, string signal, string? correction, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        await db.PendingConfirmations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EvalSignal, signal)
                .SetProperty(x => x.EvalCorrection, correction)
                .SetProperty(x => x.EvalAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task ExpireAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        await db.PendingConfirmations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), ct);
    }
}

public sealed class BudgetRepository(AppDbContext db) : IBudgetRepository
{
    public async Task<BudgetState?> GetAsync(string scopeKey, CancellationToken ct)
    {
        var row = await db.BudgetStates.AsNoTracking().FirstOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);
        return row is null ? null : new BudgetState(row.ScopeKey, row.SpentUsd, row.Tripped);
    }

    public async Task RecordSpendAsync(string scopeKey, decimal cost, decimal cap, CancellationToken ct)
    {
        // Upsert + trip when over cap, in one statement.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO budget_states (scope_key, spent_usd, tripped, window_start, updated_at)
            VALUES ({0}, {1}, ({1} >= {2}), NOW(), NOW())
            ON CONFLICT (scope_key) DO UPDATE SET
                spent_usd = budget_states.spent_usd + {1},
                tripped = (budget_states.spent_usd + {1}) >= {2},
                tripped_at = CASE WHEN (budget_states.spent_usd + {1}) >= {2} AND NOT budget_states.tripped
                                  THEN NOW() ELSE budget_states.tripped_at END,
                updated_at = NOW()
            """, scopeKey, cost, cap);
    }

    public async Task ResetAsync(string scopeKey, CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE budget_states SET tripped = FALSE, spent_usd = 0, reset_at = NOW() WHERE scope_key = {0}",
            scopeKey);
}

public sealed class DeadLetterRepository(AppDbContext db) : IDeadLetterRepository
{
    public async Task WriteAsync(string? groupId, string toolId, string inputJson,
        string errorMessage, string errorType, CancellationToken ct)
    {
        db.DeadLetterQueue.Add(new DeadLetterEntity
        {
            GroupId = Guid.TryParse(groupId, out var g) ? g : null,
            ToolId = toolId,
            Input = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class PromptVersionRepository(AppDbContext db) : IPromptVersionRepository
{
    public async Task<PromptVersionRecord?> GetActiveAsync(string templateId, CancellationToken ct)
    {
        var row = await db.PromptVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TemplateId == templateId && x.IsActive, ct);
        return row is null ? null
            : new PromptVersionRecord(row.Id, row.TemplateId, row.Content, row.ModelId,
                row.ModelTier, row.Temperature, row.TopP, row.MaxTokens, row.ReasoningLevel, row.ProviderId);
    }
}
