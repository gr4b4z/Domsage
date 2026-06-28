using AgentPlatform.Plugins.Calendar;
using AgentPlatform.Plugins.Google;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Core.Tests;

/// <summary>Deterministic pieces of the calendar/OAuth stack — the parts that must be exact without Google.</summary>
public sealed class CalendarOAuthTests
{
    private static TokenCipher Cipher()
    {
        var key = Convert.ToBase64String(new byte[32]); // 32 zero bytes — fine for a round-trip test
        return new TokenCipher(Options.Create(new GoogleOptions { EncryptionKey = key }));
    }

    [Fact]
    public void Token_cipher_round_trips()
    {
        var c = Cipher();
        const string secret = "1//refresh-token-żółć-✓";
        Assert.Equal(secret, c.Decrypt(c.Encrypt(secret)));
    }

    [Fact]
    public void Token_cipher_is_nondeterministic_but_decryptable()
    {
        var c = Cipher();
        var a = c.Encrypt("same");
        var b = c.Encrypt("same");
        Assert.NotEqual(a, b);              // random nonce per encryption
        Assert.Equal("same", c.Decrypt(a));
        Assert.Equal("same", c.Decrypt(b));
    }

    [Fact]
    public void Cipher_unavailable_without_key()
    {
        var c = new TokenCipher(Options.Create(new GoogleOptions()));
        Assert.False(c.Available);
    }

    [Fact]
    public void Consent_url_has_required_params()
    {
        var oauth = new GoogleOAuth(new StubHttpFactory(), Options.Create(new GoogleOptions
        {
            ClientId = "cid.apps.googleusercontent.com",
            ClientSecret = "secret",
            RedirectUri = "http://localhost:8080/oauth/google/callback"
        }));
        var url = oauth.BuildConsentUrl("state123");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=cid.apps.googleusercontent.com", url);
        Assert.Contains("access_type=offline", url);   // ensures a refresh token
        Assert.Contains("prompt=consent", url);
        Assert.Contains("state=state123", url);
        Assert.Contains("calendar.events", url);        // scope present (url-encoded)
    }

    [Fact]
    public void ParseLocal_treats_bare_time_as_Warsaw_local()
    {
        // 2026-07-01 is summer → Europe/Warsaw = UTC+2, so 09:00 local == 07:00 UTC.
        var utc = CalTime.ParseLocal("2026-07-01T09:00").UtcDateTime;
        Assert.Equal(7, utc.Hour);
        Assert.Equal(new DateTime(2026, 7, 1), utc.Date);
    }

    [Fact]
    public void ParseLocal_respects_explicit_offset()
    {
        var utc = CalTime.ParseLocal("2026-07-01T09:00:00+02:00").UtcDateTime;
        Assert.Equal(7, utc.Hour);
    }

    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
