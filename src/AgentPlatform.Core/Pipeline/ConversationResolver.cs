using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Resolves or creates the active conversation. Handles reset and incognito triggers. Scoped.</summary>
public sealed class ConversationResolver(IConversationRepository repo)
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

    /// <param name="userId">Resolved platform user id (GUID) — NOT the channel's external id, which
    /// for messaging channels (telegram/signal) is a chat id, not a GUID.</param>
    public async Task<ConversationContext> ResolveAsync(
        InputMessage msg, string userId, string? groupId, CancellationToken ct)
    {
        var active = await repo.GetActiveAsync(userId, msg.ChannelId, ct);

        if (IsIncognitoCommand(msg.Text))
        {
            if (active is not null && !active.Incognito)
                await repo.CloseAsync(active.Id.ToString(), "user_start_incognito", ct);
            var inc = await repo.CreateAsync(userId, groupId, msg.ChannelId, isIncognito: true, ct);
            return new ConversationContext(inc.Id.ToString(), true, inc.Summary);
        }

        if (IsIncognitoOffCommand(msg.Text) && active is { Incognito: true })
        {
            await repo.CloseAsync(active.Id.ToString(), "user_exit_incognito", ct);
            active = null;
        }

        bool isReset = IsResetCommand(msg.Text);
        if (isReset && active is not null)
        {
            await repo.CloseAsync(active.Id.ToString(), "user_reset", ct);
        }

        bool shouldStartNew = active is null
            || isReset
            || (DateTime.UtcNow - active.LastActiveAt) > SessionTimeout;

        if (shouldStartNew)
            active = await repo.CreateAsync(userId, groupId, msg.ChannelId, isIncognito: false, ct);

        return new ConversationContext(active!.Id.ToString(), active.Incognito, active.Summary);
    }

    private static bool IsResetCommand(string text) =>
        text.Trim().ToLowerInvariant() is "/start" or "reset" or "zacznijmy od nowa"
            or "nowa rozmowa" or "zacznij od nowa";

    private static bool IsIncognitoCommand(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t is "/incognito" or "tryb incognito" or "nie zapamiętuj";
    }

    private static bool IsIncognitoOffCommand(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t is "/incognito off" or "wyjdź z incognito" or "wyjdz z incognito";
    }
}
