using System.Security.Cryptography;
using System.Text;
using AgentPlatform.PluginSdk.Contracts;
using Dapper;
using Npgsql;

namespace AgentPlatform.Plugins.Http;

public record AuthResult(string UserId, string Name, string GroupId, string GroupType, string TokenLabel, string Role)
{
    public MemberRole UserRole => Role.ToLowerInvariant() switch
    {
        "admin" or "owner" => MemberRole.Admin,
        "member" or "child" => MemberRole.Member,
        _ => MemberRole.Guest
    };
}

public sealed class UserTokenAuthenticator(NpgsqlDataSource dataSource)
{
    public async Task<AuthResult?> AuthenticateAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var result = await conn.QueryFirstOrDefaultAsync<AuthResult>(
            """
            SELECT u.id::text          AS UserId,
                   u.display_name      AS Name,
                   gm.group_id::text   AS GroupId,
                   g.type              AS GroupType,
                   t.label             AS TokenLabel,
                   gm.role             AS Role
            FROM user_tokens t
            JOIN users u          ON u.id = t.user_id
            JOIN group_members gm ON gm.user_id = u.id
            JOIN groups g         ON g.id = gm.group_id
            WHERE t.token_hash = @hash
              AND (t.expires_at IS NULL OR t.expires_at > NOW())
            ORDER BY g.created_at
            LIMIT 1
            """, new { hash });

        if (result is null) return null;
        await conn.ExecuteAsync("UPDATE user_tokens SET last_used_at = NOW() WHERE token_hash = @hash",
            new { hash });
        return result;
    }
}
