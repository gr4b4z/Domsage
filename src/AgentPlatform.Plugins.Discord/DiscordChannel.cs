using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Discord;

public sealed class DiscordOptions
{
    /// <summary>Bot token from the Discord Developer Portal. Empty → channel inactive (gating, like Telegram).</summary>
    public string BotToken { get; set; } = "";
}

/// <summary>
/// Discord channel (DM 1:1). Inbound parsing reuses <see cref="DiscordParse"/>; outbound opens a DM channel
/// via the REST API and posts the message. Singleton (typed HttpClient). Text-only — no inline buttons/media.
/// </summary>
public sealed class DiscordChannelPlugin(HttpClient http, IOptions<DiscordOptions> options) : IChannelPlugin
{
    private readonly DiscordOptions _opts = options.Value;

    public string ChannelId => "discord";

    public ChannelCapabilities Capabilities => new(
        SupportsInlineButtons: false, SupportsRichCards: false,
        SupportsVoice: false, SupportsAttachments: false);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct) =>
        Task.FromResult(DiscordParse.Parse(e.Body));

    public async Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken)) return;
        try
        {
            // 1) open (or fetch) the recipient's DM channel
            using var openReq = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/users/@me/channels")
            { Content = JsonContent.Create(new { recipient_id = m.UserId }) };
            openReq.Headers.Authorization = new("Bot", _opts.BotToken);
            using var openResp = await http.SendAsync(openReq, ct);
            if (!openResp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await openResp.Content.ReadAsStringAsync(ct));
            var dmId = doc.RootElement.GetProperty("id").GetString();

            // 2) post the message (Discord hard limit: 2000 chars)
            var content = m.Text.Length > 2000 ? m.Text[..2000] : m.Text;
            using var sendReq = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{dmId}/messages")
            { Content = JsonContent.Create(new { content }) };
            sendReq.Headers.Authorization = new("Bot", _opts.BotToken);
            await http.SendAsync(sendReq, ct);
        }
        catch { /* outbound best-effort — transient failures logged upstream */ }
    }
}

/// <summary>/connect-discord — mints a code; the user sends "/start &lt;code&gt;" to the bot in DM to link.</summary>
public sealed class ConnectDiscordCommand(DiscordLinkStore links, IOptions<DiscordOptions> options) : ISlashCommand
{
    public string Name => "connect-discord";
    public string Description => "połącz swój Discord do konta (potem wyślij /start <kod> do bota w DM)";

    public Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.Value.BotToken))
            return Task.FromResult("ℹ️ Discord nie jest skonfigurowany na serwerze.");
        var code = links.Mint(ctx.UserId);
        return Task.FromResult($"🔗 Napisz do bota na Discordzie (DM): /start {code}\n(Kod ważny 15 minut.)");
    }
}
