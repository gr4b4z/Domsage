using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Telegram;

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    /// <summary>Bot @username (without @) — used to build the t.me deep link for account linking.</summary>
    public string BotUsername { get; set; } = "";
    /// <summary>When true, a recurring job long-polls getUpdates (no public webhook needed — ideal for local testing).</summary>
    public bool UsePolling { get; set; } = false;
}

/// <summary>
/// Telegram channel. Parses webhook Update JSON and delivers via the Bot API sendMessage.
/// Singleton — stateless. Inline buttons for confirmation.
/// </summary>
public sealed class TelegramChannelPlugin(HttpClient http, IOptions<TelegramOptions> options) : IChannelPlugin
{
    private readonly TelegramOptions _opts = options.Value;

    public string ChannelId => "telegram";

    public ChannelCapabilities Capabilities => new(
        SupportsInlineButtons: true, SupportsRichCards: false,
        SupportsVoice: false, SupportsAttachments: true);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(e.Body);
        var root = doc.RootElement;

        // Callback query (confirmation reply)?
        if (root.TryGetProperty("callback_query", out var cb))
        {
            var data = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
            var chatId = cb.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64();
            return Task.FromResult(Make(chatId.ToString(), data));
        }

        var message = root.TryGetProperty("message", out var m) ? m
            : root.TryGetProperty("edited_message", out var em) ? em : default;
        if (message.ValueKind == JsonValueKind.Undefined)
            return Task.FromResult(Make("0", ""));

        var fromId = message.GetProperty("chat").GetProperty("id").GetInt64();
        var text = message.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return Task.FromResult(Make(fromId.ToString(), text));
    }

    public async Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken)) return;
        var url = $"https://api.telegram.org/bot{_opts.BotToken}/sendMessage";

        object payload = m.ConfirmationRequired && m.ConfirmationId is not null
            ? new
            {
                chat_id = m.UserId,
                text = m.Text,
                reply_markup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "✅ Tak", callback_data = $"confirm:{m.ConfirmationId}" },
                            new { text = "❌ Anuluj", callback_data = $"cancel:{m.ConfirmationId}" }
                        }
                    }
                }
            }
            : new { chat_id = m.UserId, text = m.Text };

        try { await http.PostAsJsonAsync(url, payload, ct); }
        catch { /* transient — Telegram resends inbound; outbound failures logged upstream */ }
    }

    private static InputMessage Make(string chatId, string text) =>
        new(Guid.NewGuid().ToString(), "telegram", chatId, null, text, null, DateTimeOffset.UtcNow);
}
