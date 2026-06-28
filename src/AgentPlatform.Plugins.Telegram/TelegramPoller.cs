using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Telegram;

/// <summary>
/// Short-lived codes that link a Telegram chat to a platform user. The web app mints a code for the
/// signed-in user; the user sends "/start &lt;code&gt;" to the bot; the processor consumes it and stores
/// the chat id on that user. Singleton, in-memory (codes are single-use, 15-min TTL).
/// </summary>
public sealed class TelegramLinkStore
{
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset Expires)> _codes = new();

    public string Mint(string userId)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
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

/// <summary>
/// Processes a single Telegram update — shared by the polling and webhook paths. Handles
/// "/start &lt;code&gt;" account linking inline; everything else is published to the bus for the pipeline.
/// Singleton.
/// </summary>
public sealed class TelegramUpdateProcessor(
    IHttpClientFactory httpFactory,
    IMessageBus bus,
    IServiceScopeFactory scopeFactory,
    TelegramLinkStore links,
    IOptions<TelegramOptions> options,
    ILogger<TelegramUpdateProcessor> log)
{
    private readonly TelegramOptions _opts = options.Value;
    private string BaseUrl => $"https://api.telegram.org/bot{_opts.BotToken}";

    public async Task ProcessAsync(JsonElement update, CancellationToken ct)
    {
        // Intercept "/start <code>" linking on plain messages; pass everything else to the pipeline.
        if (update.TryGetProperty("message", out var message)
            && message.TryGetProperty("text", out var t)
            && (t.GetString() ?? "").TrimStart().StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
            var text = (t.GetString() ?? "").Trim();
            var code = text.Length > 6 ? text[6..].Trim() : "";

            if (code.Length == 0)
            {
                await SendAsync(chatId, "Cześć! Aby połączyć konto, w aplikacji kliknij 'Połącz Telegram' i wyślij mi tutaj: /start <kod>.", ct);
                return;
            }

            var userId = links.Consume(code);
            if (userId is null)
            {
                await SendAsync(chatId, "Kod nieprawidłowy lub wygasł — wygeneruj nowy w aplikacji.", ct);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var ok = await users.SetChannelIdentityAsync(userId, "telegram", chatId.ToString(), ct);
            await SendAsync(chatId,
                ok ? "✅ Połączono! Będę tu wysyłać przypomnienia i odpowiadać na pytania."
                   : "Nie udało się połączyć konta.", ct);
            return;
        }

        // Normal inbound (message or callback_query) — same shape a webhook would deliver.
        await bus.PublishAsync(new RawEvent("telegram", update.GetRawText()), ct);
    }

    private async Task SendAsync(long chatId, string text, CancellationToken ct)
    {
        try { await httpFactory.CreateClient().PostAsJsonAsync($"{BaseUrl}/sendMessage", new { chat_id = chatId, text }, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Telegram sendMessage failed"); }
    }
}

/// <summary>
/// Manages the inbound Telegram connection. Two modes (Telegram pushes vs we pull):
///  • UsePolling=true  → continuously long-polls getUpdates (no public URL needed — great for local dev).
///  • UsePolling=false + WebhookUrl set → registers a webhook once (efficient for production); inbound
///    then arrives via <see cref="TelegramWebhookHandler"/>.
/// </summary>
public sealed class TelegramConnector(
    IHttpClientFactory httpFactory,
    TelegramUpdateProcessor processor,
    IOptions<TelegramOptions> options,
    ILogger<TelegramConnector> log) : BackgroundService
{
    private readonly TelegramOptions _opts = options.Value;
    private long _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_opts.BotToken))
        {
            log.LogInformation("Telegram disabled (no bot token)");
            return;
        }

        var http = httpFactory.CreateClient();
        var baseUrl = $"https://api.telegram.org/bot{_opts.BotToken}";

        if (!_opts.UsePolling)
        {
            // Webhook mode: register the public endpoint with Telegram, then we're done — pushes arrive
            // at TelegramWebhookHandler.
            if (string.IsNullOrEmpty(_opts.WebhookUrl))
            {
                log.LogWarning("Telegram: UsePolling=false but no WebhookUrl set — inbound disabled");
                return;
            }
            var url = $"{baseUrl}/setWebhook?url={Uri.EscapeDataString(_opts.WebhookUrl)}"
                    + (string.IsNullOrEmpty(_opts.WebhookSecret) ? "" : $"&secret_token={Uri.EscapeDataString(_opts.WebhookSecret)}");
            try { await http.GetAsync(url, stoppingToken); log.LogInformation("Telegram webhook registered at {Url}", _opts.WebhookUrl); }
            catch (Exception ex) { log.LogWarning(ex, "Telegram setWebhook failed"); }
            return;
        }

        // Polling mode: a webhook and getUpdates are mutually exclusive — drop any webhook first.
        try { await http.GetAsync($"{baseUrl}/deleteWebhook", stoppingToken); } catch { /* best effort */ }
        log.LogInformation("Telegram long-poll started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnceAsync(http, baseUrl, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Telegram poll cycle failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task PollOnceAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        var url = $"{baseUrl}/getUpdates?timeout=25&offset={_offset}&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";
        using var resp = await http.GetAsync(url, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return;

        foreach (var update in result.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var uid))
                _offset = Math.Max(_offset, uid.GetInt64() + 1); // ack: next poll won't return it again
            await processor.ProcessAsync(update, ct);
        }
    }
}

/// <summary>
/// Public webhook endpoint for Telegram (production mode). Plugin-owned via <see cref="IWebhookHandler"/>;
/// the host maps the route without knowing it's Telegram. Verifies the secret-token header, then hands
/// the update to the shared processor.
/// </summary>
public sealed class TelegramWebhookHandler(
    TelegramUpdateProcessor processor,
    IOptions<TelegramOptions> options,
    ILogger<TelegramWebhookHandler> log) : IWebhookHandler
{
    private readonly TelegramOptions _opts = options.Value;

    public string Route => "/webhook/telegram";

    public async Task<WebhookResponse> HandleAsync(WebhookRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_opts.WebhookSecret))
        {
            request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secret);
            if (secret != _opts.WebhookSecret) return WebhookResponse.Unauthorized;
        }

        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            await processor.ProcessAsync(doc.RootElement, ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "Telegram webhook processing failed"); }

        return WebhookResponse.Ok; // always ack so Telegram doesn't retry-storm
    }
}
