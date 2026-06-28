using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<UserGroupInfo?> GetByTelegramIdAsync(long telegramId, CancellationToken ct) =>
        ResolveAsync(u => u.TelegramId == telegramId, ct);

    public Task<UserGroupInfo?> GetBySignalNumberAsync(string phoneNumber, CancellationToken ct) =>
        ResolveAsync(u => u.SignalNumber == phoneNumber, ct);

    public Task<UserGroupInfo?> GetByEmailAsync(string email, CancellationToken ct) =>
        ResolveAsync(u => u.Email == email, ct);

    public async Task<UserGroupInfo?> GetPrimaryGroupAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return null;
        return await ResolveAsync(u => u.Id == uid, ct);
    }

    private async Task<UserGroupInfo?> ResolveAsync(
        System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(predicate).FirstOrDefaultAsync(ct);
        if (user is null) return null;

        // Primary group = oldest joined.
        var membership = await (
            from gm in db.GroupMembers.AsNoTracking()
            join g in db.Groups.AsNoTracking() on gm.GroupId equals g.Id
            where gm.UserId == user.Id
            orderby g.CreatedAt
            select new { gm.Role, g.Id, g.Type }).FirstOrDefaultAsync(ct);

        if (membership is null) return null;

        return new UserGroupInfo(user.Id.ToString(), membership.Id.ToString(),
            membership.Type, MapRole(membership.Role), user.DisplayName);
    }

    public static MemberRole MapRole(string role) => role.ToLowerInvariant() switch
    {
        "admin" or "owner" => MemberRole.Admin,
        "member" or "child" => MemberRole.Member,
        _ => MemberRole.Guest
    };
}
