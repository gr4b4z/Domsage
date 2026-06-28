using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Plugins.Family.Data;

public interface IShoppingRepository
{
    Task<Guid> AddItemAsync(ShoppingItem item, CancellationToken ct);
    /// <summary>First-wins mark by fuzzy name (chat path).</summary>
    Task<ShoppingItem?> MarkBoughtAsync(Guid groupId, string name, Guid userId, CancellationToken ct);
    /// <summary>First-wins mark by exact id (tap path).</summary>
    Task<ShoppingItem?> MarkBoughtByIdAsync(Guid groupId, Guid itemId, Guid userId, CancellationToken ct);
    /// <summary>Undo a check-off (tap mistake).</summary>
    Task<ShoppingItem?> UncheckAsync(Guid groupId, Guid itemId, CancellationToken ct);
    Task<IReadOnlyList<ShoppingItem>> ListNeededAsync(Guid groupId, CancellationToken ct);
    /// <summary>Checklist board: needed items + items bought within the last 12h.</summary>
    Task<IReadOnlyList<ShoppingItem>> BoardAsync(Guid groupId, CancellationToken ct);

    // Notification watchers (opt-in). Default: none → silent.
    Task AddWatcherAsync(Guid groupId, Guid userId, DateTimeOffset? until, CancellationToken ct);
    Task RemoveWatcherAsync(Guid groupId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ActiveWatchersAsync(Guid groupId, CancellationToken ct);
}

public sealed class ShoppingRepository(FamilyDbContext db) : IShoppingRepository
{
    public async Task<Guid> AddItemAsync(ShoppingItem item, CancellationToken ct)
    {
        db.ShoppingItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task<ShoppingItem?> MarkBoughtAsync(Guid groupId, string name, Guid userId, CancellationToken ct)
    {
        var item = await db.ShoppingItems
            .Where(s => s.GroupId == groupId && s.Status == "needed" && EF.Functions.ILike(s.Name, $"%{name}%"))
            .OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (item is null) return null;
        var affected = await db.ShoppingItems
            .Where(s => s.Id == item.Id && s.Status == "needed")
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, "bought")
                .SetProperty(s => s.BoughtBy, userId)
                .SetProperty(s => s.BoughtAt, DateTimeOffset.UtcNow), ct);
        if (affected == 0) return null; // someone else won the race
        item.Status = "bought"; item.BoughtBy = userId;
        return item;
    }

    public async Task<ShoppingItem?> MarkBoughtByIdAsync(Guid groupId, Guid itemId, Guid userId, CancellationToken ct)
    {
        var affected = await db.ShoppingItems
            .Where(s => s.Id == itemId && s.GroupId == groupId && s.Status == "needed")
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, "bought")
                .SetProperty(s => s.BoughtBy, userId)
                .SetProperty(s => s.BoughtAt, DateTimeOffset.UtcNow), ct);
        if (affected == 0) return null;
        return await db.ShoppingItems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == itemId, ct);
    }

    public async Task<ShoppingItem?> UncheckAsync(Guid groupId, Guid itemId, CancellationToken ct)
    {
        var affected = await db.ShoppingItems
            .Where(s => s.Id == itemId && s.GroupId == groupId && s.Status == "bought")
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, "needed")
                .SetProperty(s => s.BoughtBy, (Guid?)null)
                .SetProperty(s => s.BoughtAt, (DateTimeOffset?)null), ct);
        if (affected == 0) return null;
        return await db.ShoppingItems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == itemId, ct);
    }

    public async Task<IReadOnlyList<ShoppingItem>> ListNeededAsync(Guid groupId, CancellationToken ct) =>
        await db.ShoppingItems.AsNoTracking()
            .Where(s => s.GroupId == groupId && s.Status == "needed").OrderBy(s => s.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ShoppingItem>> BoardAsync(Guid groupId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-12);
        return await db.ShoppingItems.AsNoTracking()
            .Where(s => s.GroupId == groupId && (s.Status == "needed" || (s.Status == "bought" && s.BoughtAt > since)))
            .OrderBy(s => s.Status == "needed" ? 0 : 1).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddWatcherAsync(Guid groupId, Guid userId, DateTimeOffset? until, CancellationToken ct)
    {
        var existing = await db.ShoppingWatchers.FirstOrDefaultAsync(w => w.GroupId == groupId && w.UserId == userId, ct);
        if (existing is null)
            db.ShoppingWatchers.Add(new ShoppingWatcher { GroupId = groupId, UserId = userId, Until = until });
        else
            existing.Until = until; // standing (null) overrides / refresh TTL
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveWatcherAsync(Guid groupId, Guid userId, CancellationToken ct) =>
        await db.ShoppingWatchers.Where(w => w.GroupId == groupId && w.UserId == userId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<Guid>> ActiveWatchersAsync(Guid groupId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.ShoppingWatchers.AsNoTracking()
            .Where(w => w.GroupId == groupId && (w.Until == null || w.Until > now))
            .Select(w => w.UserId).ToListAsync(ct);
    }
}

public interface IRenewalsRepository
{
    Task<Guid> AddAsync(Renewal renewal, CancellationToken ct);
    Task<IReadOnlyList<Renewal>> ListUpcomingAsync(Guid groupId, CancellationToken ct);
}

public sealed class RenewalsRepository(FamilyDbContext db) : IRenewalsRepository
{
    public async Task<Guid> AddAsync(Renewal renewal, CancellationToken ct)
    {
        db.Renewals.Add(renewal);
        await db.SaveChangesAsync(ct);
        return renewal.Id;
    }
    public async Task<IReadOnlyList<Renewal>> ListUpcomingAsync(Guid groupId, CancellationToken ct) =>
        await db.Renewals.AsNoTracking()
            .Where(r => r.GroupId == groupId && r.Status == "active")
            .OrderBy(r => r.ExpiresOn).ToListAsync(ct);
}

public interface IChoresRepository
{
    Task<Guid> AddAsync(Chore chore, CancellationToken ct);
}

public sealed class ChoresRepository(FamilyDbContext db) : IChoresRepository
{
    public async Task<Guid> AddAsync(Chore chore, CancellationToken ct)
    {
        db.Chores.Add(chore);
        await db.SaveChangesAsync(ct);
        return chore.Id;
    }
}
