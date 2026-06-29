using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AgentPlatform.Plugins.Discord;

/// <summary>
/// Short-lived codes linking a Discord account to a platform user. The web app mints a code for the
/// signed-in user; the user sends "/start &lt;code&gt;" to the bot in DM; the router consumes it. In-memory,
/// single-use, 15-min TTL (mirrors the Telegram link store).
/// </summary>
public sealed class DiscordLinkStore
{
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset Expires)> _codes = new();

    public string Mint(string userId)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(6);
        var code = string.Concat(bytes.Select(b => alphabet[b % alphabet.Length]));
        _codes[code] = (userId, DateTimeOffset.UtcNow.AddMinutes(15));
        return code;
    }

    public string? Consume(string code)
    {
        if (_codes.TryRemove(code.Trim().ToUpperInvariant(), out var v) && v.Expires > DateTimeOffset.UtcNow)
            return v.UserId;
        return null;
    }
}
