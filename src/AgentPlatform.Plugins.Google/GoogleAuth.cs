using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Google;

public sealed class GoogleOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string RedirectUri { get; set; } = "http://localhost:8080/oauth/google/callback";
    /// <summary>Base64 of 16/24/32 random bytes — encrypts tokens at rest.</summary>
    public string? EncryptionKey { get; set; }
    /// <summary>read+write events, plus identity to label the connection.</summary>
    public string Scopes { get; set; } = "openid email https://www.googleapis.com/auth/calendar.events";

    public bool Configured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(EncryptionKey);
}

/// <summary>AES-GCM encryption for tokens at rest. Format: base64(nonce[12] | tag[16] | ciphertext).</summary>
public sealed class TokenCipher(IOptions<GoogleOptions> options)
{
    private readonly byte[]? _key = Decode(options.Value.EncryptionKey);
    public bool Available => _key is not null;

    private static byte[]? Decode(string? b64)
    {
        try { return string.IsNullOrWhiteSpace(b64) ? null : Convert.FromBase64String(b64); }
        catch { return null; }
    }

    public string Encrypt(string plain)
    {
        if (_key is null) throw new InvalidOperationException("Plugins:Google:EncryptionKey not configured");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(_key, 16);
        gcm.Encrypt(nonce, pt, ct, tag);
        var buf = new byte[12 + 16 + ct.Length];
        Buffer.BlockCopy(nonce, 0, buf, 0, 12);
        Buffer.BlockCopy(tag, 0, buf, 12, 16);
        Buffer.BlockCopy(ct, 0, buf, 28, ct.Length);
        return Convert.ToBase64String(buf);
    }

    public string Decrypt(string enc)
    {
        if (_key is null) throw new InvalidOperationException("Plugins:Google:EncryptionKey not configured");
        var buf = Convert.FromBase64String(enc);
        var nonce = buf[..12];
        var tag = buf[12..28];
        var ct = buf[28..];
        var pt = new byte[ct.Length];
        using var gcm = new AesGcm(_key, 16);
        gcm.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}

public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>Raw Google OAuth2 web-app flow (consent URL, code exchange, refresh, userinfo). No SDK dependency.</summary>
public sealed class GoogleOAuth(IHttpClientFactory http, IOptions<GoogleOptions> options)
{
    private readonly GoogleOptions _o = options.Value;

    public string BuildConsentUrl(string state)
    {
        var q = new Dictionary<string, string?>
        {
            ["client_id"] = _o.ClientId,
            ["redirect_uri"] = _o.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = _o.Scopes,
            ["access_type"] = "offline",      // ask for a refresh token
            ["prompt"] = "consent",           // ensure a refresh token even on re-consent
            ["include_granted_scopes"] = "true",
            ["state"] = state
        };
        var qs = string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        return "https://accounts.google.com/o/oauth2/v2/auth?" + qs;
    }

    public Task<OAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => PostTokenAsync(new()
    {
        ["code"] = code,
        ["client_id"] = _o.ClientId!,
        ["client_secret"] = _o.ClientSecret!,
        ["redirect_uri"] = _o.RedirectUri,
        ["grant_type"] = "authorization_code"
    }, ct);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) => PostTokenAsync(new()
    {
        ["refresh_token"] = refreshToken,
        ["client_id"] = _o.ClientId!,
        ["client_secret"] = _o.ClientSecret!,
        ["grant_type"] = "refresh_token"
    }, ct);

    private async Task<OAuthTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = http.CreateClient();
        using var resp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expires = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        return new OAuthTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expires));
    }

    public async Task<string?> GetEmailAsync(string accessToken, CancellationToken ct)
    {
        var client = http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        req.Headers.Authorization = new("Bearer", accessToken);
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("email", out var em) ? em.GetString() : null;
    }
}

public sealed record StoredAccount(string RefreshToken, string? AccessToken, DateTimeOffset? AccessExpiresAt, string Scopes, string? Email);

/// <summary>Persists encrypted OAuth tokens in the core connected_accounts table (one row per user+provider).</summary>
public sealed class ConnectedAccountStore(AppDbContext db, TokenCipher cipher)
{
    public async Task SaveAsync(string userId, string provider, string refresh, string? access,
        DateTimeOffset? expires, string scopes, string? email, CancellationToken ct)
    {
        var uid = Guid.Parse(userId);
        var existing = await db.ConnectedAccounts.FirstOrDefaultAsync(a => a.UserId == uid && a.Provider == provider, ct);
        if (existing is null)
        {
            db.ConnectedAccounts.Add(new ConnectedAccountEntity
            {
                UserId = uid, Provider = provider,
                RefreshTokenEnc = cipher.Encrypt(refresh),
                AccessTokenEnc = access is null ? null : cipher.Encrypt(access),
                AccessExpiresAt = expires, Scopes = scopes, AccountEmail = email
            });
        }
        else
        {
            existing.RefreshTokenEnc = cipher.Encrypt(refresh);
            existing.AccessTokenEnc = access is null ? null : cipher.Encrypt(access);
            existing.AccessExpiresAt = expires;
            existing.Scopes = scopes;
            existing.AccountEmail = email;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoredAccount?> GetAsync(string userId, string provider, CancellationToken ct)
    {
        var uid = Guid.Parse(userId);
        var a = await db.ConnectedAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid && x.Provider == provider, ct);
        if (a is null) return null;
        return new StoredAccount(
            cipher.Decrypt(a.RefreshTokenEnc),
            a.AccessTokenEnc is null ? null : cipher.Decrypt(a.AccessTokenEnc),
            a.AccessExpiresAt, a.Scopes, a.AccountEmail);
    }

    public async Task UpdateAccessAsync(string userId, string provider, string access, DateTimeOffset expires, CancellationToken ct)
    {
        var uid = Guid.Parse(userId);
        var enc = cipher.Encrypt(access);
        await db.ConnectedAccounts.Where(x => x.UserId == uid && x.Provider == provider)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AccessTokenEnc, enc)
                .SetProperty(x => x.AccessExpiresAt, expires)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    public Task<bool> ExistsAsync(string userId, string provider, CancellationToken ct)
    {
        var uid = Guid.Parse(userId);
        return db.ConnectedAccounts.AnyAsync(x => x.UserId == uid && x.Provider == provider, ct);
    }
}

/// <summary>IOAuthTokenProvider for Google — returns a valid access token, refreshing transparently.</summary>
public sealed class GoogleTokenProvider(ConnectedAccountStore store, GoogleOAuth oauth, IOptions<GoogleOptions> options) : IOAuthTokenProvider
{
    public string Provider => "google";

    public Task<bool> IsConnectedAsync(string userId, CancellationToken ct) => store.ExistsAsync(userId, "google", ct);

    public async Task<string?> GetAccessTokenAsync(string userId, CancellationToken ct)
    {
        if (!options.Value.Configured) return null;
        var acc = await store.GetAsync(userId, "google", ct);
        if (acc is null) return null;
        if (acc.AccessToken is not null && acc.AccessExpiresAt is { } exp && exp > DateTimeOffset.UtcNow.AddSeconds(60))
            return acc.AccessToken;

        var refreshed = await oauth.RefreshAsync(acc.RefreshToken, ct);
        await store.UpdateAccessAsync(userId, "google", refreshed.AccessToken, refreshed.ExpiresAt, ct);
        return refreshed.AccessToken;
    }
}
