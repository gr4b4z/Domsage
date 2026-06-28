using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Repositories;

public sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public async Task<ConversationRecord?> GetActiveAsync(string userId, string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return null;
        var row = await db.Conversations.AsNoTracking()
            .Where(x => x.UserId == uid && x.ChannelId == channelId && x.Status == "active")
            .OrderByDescending(x => x.LastActiveAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row);
    }

    public async Task<ConversationRecord> GetAsync(string conversationId, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        var row = await db.Conversations.AsNoTracking().FirstAsync(x => x.Id == gid, ct);
        return Map(row);
    }

    public async Task<ConversationRecord> CreateAsync(string userId, string? groupId, string channelId,
        bool isIncognito, CancellationToken ct)
    {
        var entity = new ConversationEntity
        {
            UserId = Guid.Parse(userId),
            GroupId = Guid.TryParse(groupId, out var g) ? g : null,
            ChannelId = channelId,
            Incognito = isIncognito,
        };
        db.Conversations.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task CloseAsync(string conversationId, string reason, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        await db.Conversations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "closed")
                .SetProperty(x => x.CloseReason, reason)
                .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task AppendAsync(string conversationId, ConversationMessageRecord m, CancellationToken ct)
    {
        db.ConversationMessages.Add(new ConversationMessageEntity
        {
            ConversationId = Guid.Parse(conversationId),
            Role = m.Role,
            Content = m.Content,
            Intent = m.Intent,
            ActionSummary = m.ActionSummary,
            Tokens = m.Tokens,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task AppendMetadataOnlyAsync(string conversationId, int tokens, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        await db.Conversations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActiveAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task TouchAsync(string conversationId, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        await db.Conversations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActiveAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task SaveSummaryAsync(string conversationId, string summary, Guid coversUpTo, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        await db.Conversations.Where(x => x.Id == gid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Summary, summary)
                .SetProperty(x => x.SummaryCoversUpTo, coversUpTo), ct);
    }

    public async Task<IReadOnlyList<ConversationMessageRecord>> FetchWithinBudgetAsync(
        string conversationId, Guid? afterMessageId, int tokenBudget, CancellationToken ct)
    {
        var gid = Guid.Parse(conversationId);
        DateTimeOffset after = DateTimeOffset.MinValue;
        if (afterMessageId is { } amid)
        {
            var cursor = await db.ConversationMessages.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == amid, ct);
            if (cursor is not null) after = cursor.CreatedAt;
        }

        var rows = await db.ConversationMessages.AsNoTracking()
            .Where(x => x.ConversationId == gid && x.CreatedAt > after)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var result = new List<ConversationMessageRecord>();
        int used = 0;
        foreach (var r in rows)
        {
            if (used + r.Tokens > tokenBudget && result.Count > 0) break;
            used += r.Tokens;
            result.Add(new ConversationMessageRecord(r.Role, r.Content, r.Intent, r.ActionSummary, r.Tokens));
        }
        return result;
    }

    public async Task<IReadOnlyList<ConversationMessageRecord>> SearchMessagesAsync(
        string userId, string query, int limit, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid) || string.IsNullOrWhiteSpace(query)) return [];
        // websearch_to_tsquery handles free-text; FTS index idx_conv_messages_fts backs this.
        var rows = await db.ConversationMessages.AsNoTracking()
            .Where(m => db.Conversations.Any(c => c.Id == m.ConversationId && c.UserId == uid))
            .Where(m => EF.Functions.ToTsVector("simple", m.Content)
                .Matches(EF.Functions.WebSearchToTsQuery("simple", query)))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(r => new ConversationMessageRecord(r.Role, r.Content, r.Intent, r.ActionSummary, r.Tokens)).ToList();
    }

    private static ConversationRecord Map(ConversationEntity e) =>
        new(e.Id, e.UserId.ToString(), e.GroupId?.ToString(), e.ChannelId, e.Incognito,
            e.Status, e.Summary, e.SummaryCoversUpTo, e.LastActiveAt.UtcDateTime);
}
