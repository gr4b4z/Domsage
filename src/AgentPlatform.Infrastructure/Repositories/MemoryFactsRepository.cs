using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Repositories;

public sealed class MemoryFactsRepository(AppDbContext db) : IMemoryFactsRepository
{
    public async Task<IReadOnlyList<MemoryFact>> GetForUserAsync(string userId, string? groupId, CancellationToken ct)
    {
        Guid.TryParse(userId, out var uid);
        Guid? gid = Guid.TryParse(groupId, out var g) ? g : null;
        var rows = await db.MemoryFacts.AsNoTracking()
            .Where(f => f.UserId == uid || (f.Scope == "group" && f.GroupId == gid))
            .ToListAsync(ct);
        return rows.Select(f => new MemoryFact(f.Key, f.Value, f.Category, f.Scope)).ToList();
    }

    public async Task UpsertAsync(string? userId, string? groupId, string scope, string category,
        string key, string value, string source, CancellationToken ct)
    {
        Guid? uid = Guid.TryParse(userId, out var u) ? u : null;
        Guid? gid = Guid.TryParse(groupId, out var g) ? g : null;

        var existing = await db.MemoryFacts.FirstOrDefaultAsync(
            f => f.UserId == uid && f.GroupId == gid && f.Key == key, ct);
        if (existing is not null)
        {
            existing.Value = value;
            existing.Category = category;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            db.MemoryFacts.Add(new MemoryFactEntity
            {
                UserId = uid, GroupId = gid, Scope = scope, Category = category,
                Key = key, Value = value, Source = source
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
