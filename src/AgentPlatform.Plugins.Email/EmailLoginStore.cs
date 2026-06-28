using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AgentPlatform.Plugins.Email;

/// <summary>
/// Passwordless email login: a 6-digit code mailed to a registered address, exchanged for a session
/// token. code → userId, single-use, 10-minute TTL. Singleton, in-memory. Kept separate from
/// <see cref="EmailLinkStore"/> (which links addresses) so the two flows can't be confused.
/// </summary>
public sealed class EmailLoginStore
{
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset Expires)> _codes = new();

    public string Mint(string userId)
    {
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        _codes[code] = (userId, DateTimeOffset.UtcNow.AddMinutes(10));
        return code;
    }

    public string? Consume(string code)
    {
        if (_codes.TryRemove(code.Trim(), out var v) && v.Expires > DateTimeOffset.UtcNow)
            return v.UserId;
        return null;
    }
}
