using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AgentPlatform.Plugins.Email;

/// <summary>
/// Short-lived verification codes for self-service email linking. The user requests linking an address;
/// a code is emailed to that address (proving they control it); they enter the code to confirm.
/// Singleton, in-memory; codes are single-use with a 15-minute TTL.
/// </summary>
public sealed class EmailLinkStore
{
    private readonly ConcurrentDictionary<string, (string UserId, string Address, DateTimeOffset Expires)> _codes = new();

    /// <summary>Mints a 6-digit code bound to (user, address). The address is normalised (lowercase).</summary>
    public string Mint(string userId, string address)
    {
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        _codes[code] = (userId, address.Trim().ToLowerInvariant(), DateTimeOffset.UtcNow.AddMinutes(15));
        return code;
    }

    public (string UserId, string Address)? Consume(string code)
    {
        if (_codes.TryRemove(code.Trim(), out var v) && v.Expires > DateTimeOffset.UtcNow)
            return (v.UserId, v.Address);
        return null;
    }
}
