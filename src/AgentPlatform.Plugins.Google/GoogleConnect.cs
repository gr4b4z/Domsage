using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Google;

/// <summary>Short-lived OAuth state → userId map (survives the redirect; same process).</summary>
public sealed class GoogleLinkStore
{
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset At)> _m = new();

    public string Mint(string userId)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        _m[state] = (userId, DateTimeOffset.UtcNow);
        return state;
    }

    public string? Consume(string state) =>
        _m.TryRemove(state, out var v) && DateTimeOffset.UtcNow - v.At < TimeSpan.FromMinutes(15) ? v.UserId : null;

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Handles GET /oauth/google/callback — exchanges the code and stores the user's tokens.</summary>
public sealed class GoogleCallbackHandler(
    GoogleLinkStore links, GoogleOAuth oauth, ConnectedAccountStore store, IOptions<GoogleOptions> options) : IOAuthCallbackHandler
{
    public string Provider => "google";

    public async Task<OAuthCallbackResult> HandleAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (query.TryGetValue("error", out var err))
            return Page($"Odmowa dostępu ({err}). Możesz zamknąć tę kartę.");
        if (!query.TryGetValue("code", out var code) || !query.TryGetValue("state", out var state))
            return Page("Brak parametrów code/state.", 400);

        var userId = links.Consume(state);
        if (userId is null)
            return Page("Link wygasł lub jest nieprawidłowy. Spróbuj ponownie: /connect-google", 400);

        try
        {
            var tokens = await oauth.ExchangeCodeAsync(code, ct);
            if (tokens.RefreshToken is null)
                return Page("Google nie zwróciło refresh-tokena. Odłącz aplikację w ustawieniach konta Google i spróbuj ponownie.", 400);

            var email = await oauth.GetEmailAsync(tokens.AccessToken, ct);
            await store.SaveAsync(userId, "google", tokens.RefreshToken, tokens.AccessToken, tokens.ExpiresAt, options.Value.Scopes, email, ct);
            return Page($"✅ Połączono Google{(email is null ? "" : $" ({email})")}. Wróć do czatu — mogę już dodawać wpisy do kalendarza.");
        }
        catch (Exception ex)
        {
            return Page($"Nie udało się połączyć: {ex.Message}", 500);
        }
    }

    private static OAuthCallbackResult Page(string msg, int status = 200) => new(
        "<!doctype html><meta charset=utf-8><body style='font-family:system-ui,sans-serif;max-width:32rem;" +
        "margin:4rem auto;text-align:center;color:#222'><p style='font-size:1.15rem'>" +
        WebUtility.HtmlEncode(msg) + "</p></body>", status);
}

/// <summary>/connect-google — mints a consent link the user opens to authorize calendar access.</summary>
public sealed class ConnectGoogleCommand(GoogleLinkStore links, GoogleOAuth oauth, IOptions<GoogleOptions> options) : ISlashCommand
{
    public string Name => "connect-google";
    public string Description => "połącz swój kalendarz Google (logowanie przez Google)";

    public Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        if (!options.Value.Configured)
            return Task.FromResult("ℹ️ Google nie jest skonfigurowany na serwerze.");
        var url = oauth.BuildConsentUrl(links.Mint(ctx.UserId));
        return Task.FromResult($"🔗 Połącz Google Calendar — otwórz link i zaloguj się:\n{url}\n(Ważny 15 minut.)");
    }
}

/// <summary>Shared Google account/OAuth plugin — reused by any Google capability (Calendar now, Gmail/Drive later).</summary>
public sealed class GooglePluginRegistration : IPluginRegistration
{
    public string Namespace => "google";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoogleOptions>(config);
        services.AddHttpClient();
        services.AddSingleton<TokenCipher>();
        services.AddSingleton<GoogleOAuth>();
        services.AddSingleton<GoogleLinkStore>();
        services.AddScoped<ConnectedAccountStore>();
        services.AddScoped<IOAuthTokenProvider, GoogleTokenProvider>();
        services.AddScoped<IOAuthCallbackHandler, GoogleCallbackHandler>();
        services.AddScoped<ISlashCommand, ConnectGoogleCommand>();
    }
}
