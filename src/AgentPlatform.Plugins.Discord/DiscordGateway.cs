using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Discord;

/// <summary>
/// Inbound Discord connection: a persistent Gateway (WebSocket) via Discord.Net. Handles DM 1:1 only.
/// "/start &lt;code&gt;" links the account inline; everything else is published to the bus for the pipeline.
/// No bot token → disabled. Guild messages/bots are ignored (OUT of scope).
/// </summary>
public sealed class DiscordGateway(
    DiscordChannelPlugin channel,
    IMessageBus bus,
    IServiceScopeFactory scopeFactory,
    DiscordLinkStore links,
    IOptions<DiscordOptions> options,
    ILogger<DiscordGateway> log) : BackgroundService
{
    private readonly DiscordOptions _opts = options.Value;
    private DiscordSocketClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_opts.BotToken))
        {
            log.LogInformation("Discord disabled (no bot token)");
            return;
        }

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.MessageContent | GatewayIntents.Guilds,
            LogLevel = LogSeverity.Warning
        });
        _client.MessageReceived += OnMessageReceived;
        _client.Log += m => { log.LogDebug("Discord: {Message}", m.Message); return Task.CompletedTask; };

        await _client.LoginAsync(TokenType.Bot, _opts.BotToken);
        await _client.StartAsync();
        log.LogInformation("Discord gateway started");

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }
        await _client.StopAsync();
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        try
        {
            if (msg.Author.IsBot) return;                  // ignore self / other bots
            if (msg.Channel is not IDMChannel) return;     // DM 1:1 only

            var authorId = msg.Author.Id.ToString();
            switch (DiscordDmRouter.Route(authorId, msg.Content ?? "", links))
            {
                case PublishMessage p:
                    await bus.PublishAsync(new RawEvent("discord",
                        JsonSerializer.Serialize(new { authorId = p.AuthorId, text = p.Text })), CancellationToken.None);
                    break;

                case LinkAccount l:
                    await using (var scope = scopeFactory.CreateAsyncScope())
                    {
                        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                        var ok = await users.SetChannelIdentityAsync(l.UserId, "discord", l.AuthorId, CancellationToken.None);
                        await Reply(authorId, ok
                            ? "✅ Połączono! Będę tu odpowiadać i wysyłać przypomnienia."
                            : "Nie udało się połączyć konta.");
                    }
                    break;

                case ReplyDm r:
                    await Reply(authorId, r.Text);
                    break;

                case IgnoreDm:
                    break;
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Discord message handling failed"); }
    }

    private Task Reply(string authorId, string text) =>
        channel.DeliverAsync(new OutputMessage("discord", authorId, text, false, null, null), CancellationToken.None);
}
