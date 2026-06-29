using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Notifications;

/// <summary>
/// Notifies users along a delivery chain: live (SSE) for web chat → best-effort push on the user's
/// messaging channel (Telegram/Signal) → email as a last-resort fallback when the web client is
/// offline and no messaging channel is configured. Per-user <c>NotifyMode</c> tunes this:
/// "auto" (default, full chain), "email" (always also email), "silent" (web SSE only).
/// Scoped. Never throws to the caller.
/// </summary>
public sealed class NotificationService(
    AppDbContext db, ISseHub sse, PluginRegistry registry,
    Microsoft.Extensions.Options.IOptions<NotificationOptions> options,
    ILogger<NotificationService> log)
    : INotificationService
{
    private readonly NotificationOptions _options = options.Value;

    public async Task NotifyUsersAsync(IEnumerable<string> userIds, LiveEvent evt, CancellationToken ct)
    {
        var ids = userIds.Select(u => Guid.TryParse(u, out var g) ? g : (Guid?)null)
                         .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        var identities = (await db.ChannelIdentities.AsNoTracking()
                .Where(c => ids.Contains(c.UserId)).ToListAsync(ct))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var u in users)
        {
            // Live web-chat update (always — harmless if no client is connected).
            var live = sse.IsConnected(u.Id.ToString());
            try { await sse.PublishAsync(u.Id.ToString(), evt, ct); }
            catch (Exception ex) { log.LogWarning(ex, "SSE publish failed for {User}", u.Id); }

            if (u.NotifyMode == "silent") continue; // web only

            // Resolve outbound targets from the user's channel identities. A user may have several
            // emails — pick the primary (falling back to any).
            var chans = identities.GetValueOrDefault(u.Id) ?? [];
            // Generic, priority-driven push selection (telegram>signal>… by config; new channels zero-touch).
            var push = ChannelRouting.SelectPushChannel(chans, _options.ChannelPriority);
            var primaryEmail = (chans.FirstOrDefault(c => c.ChannelId == "email" && c.IsPrimary)
                                ?? chans.FirstOrDefault(c => c.ChannelId == "email"))?.ExternalId;
            var hasMessaging = push is not null;

            try
            {
                if (push is { } p) await Deliver(p.ChannelId, p.ExternalId, evt, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Channel push failed for {User}", u.Id); }

            // Email fallback: forced ("email" mode), or last-resort when the user can't be reached
            // live and has no messaging channel — so a reminder is never silently lost.
            var emailFallback = u.NotifyMode == "email" || (!live && !hasMessaging);
            if (emailFallback && primaryEmail is not null)
            {
                log.LogInformation("Email fallback for {User} (mode={Mode}, live={Live}, messaging={Msg})",
                    u.Id, u.NotifyMode, live, hasMessaging);
                try { await Deliver("email", primaryEmail, evt, ct); }
                catch (Exception ex) { log.LogWarning(ex, "Email fallback failed for {User}", u.Id); }
            }
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
