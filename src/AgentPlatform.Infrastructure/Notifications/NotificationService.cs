using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Notifications;

/// <summary>
/// Notifies users live over SSE (web chat) and, best-effort, on their messaging channel
/// (Telegram/Signal) if an external id is known. Scoped. Never throws to the caller.
/// </summary>
public sealed class NotificationService(
    AppDbContext db, ISseHub sse, PluginRegistry registry, ILogger<NotificationService> log)
    : INotificationService
{
    public async Task NotifyUsersAsync(IEnumerable<string> userIds, LiveEvent evt, CancellationToken ct)
    {
        var ids = userIds.Select(u => Guid.TryParse(u, out var g) ? g : (Guid?)null)
                         .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        foreach (var u in users)
        {
            // Live web-chat update (always).
            try { await sse.PublishAsync(u.Id.ToString(), evt, ct); }
            catch (Exception ex) { log.LogWarning(ex, "SSE publish failed for {User}", u.Id); }

            // Best-effort push on a messaging channel.
            try
            {
                if (u.TelegramId is { } tg)
                    await Deliver("telegram", tg.ToString(), evt, ct);
                else if (!string.IsNullOrEmpty(u.SignalNumber))
                    await Deliver("signal", u.SignalNumber!, evt, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Channel push failed for {User}", u.Id); }
        }
    }

    private async Task Deliver(string channelId, string externalUserId, LiveEvent evt, CancellationToken ct)
    {
        IChannelPlugin channel;
        try { channel = registry.GetChannel(channelId); }
        catch { return; } // channel not registered
        await channel.DeliverAsync(new OutputMessage(
            channelId, externalUserId, $"{evt.Title}\n{evt.Body}".Trim(), false, null, null), ct);
    }
}
