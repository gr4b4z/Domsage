using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Telegram;

/// <summary>
/// telegram.link — conversational account linking. Mints a one-time code and returns the bot
/// deep link, so the user just writes "połącz telegram" in chat (no menu, no settings screen).
/// </summary>
public sealed class TelegramLinkTool(TelegramLinkStore links, IOptions<TelegramOptions> options) : ITool
{
    private readonly TelegramOptions _opts = options.Value;

    public string ToolId => "telegram.link";
    public bool HasSideEffects => false; // mints an ephemeral in-memory code; no persistent change
    public ScopeRequirement RequiredScope => new(ContextScope.User, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();

    public Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken) || !_opts.UsePolling)
            return Task.FromResult(new ToolResult(ToolResultStatus.Success, null, null,
                "ℹ️ Telegram nie jest jeszcze skonfigurowany na serwerze."));

        var code = links.Mint(ctx.UserId);
        var link = string.IsNullOrEmpty(_opts.BotUsername)
            ? "" : $"\nLub otwórz: https://t.me/{_opts.BotUsername}?start={code}";
        var msg = $"📨 Aby połączyć Telegram, wyślij do bota:\n/start {code}{link}\n\nKod wygasa za 15 minut.";

        return Task.FromResult(new ToolResult(ToolResultStatus.Success,
            JsonSerializer.SerializeToElement(new { code }), null, msg));
    }
}

/// <summary>/connect-telegram — deterministic slash command (no LLM) to link this user's Telegram chat.</summary>
public sealed class ConnectTelegramCommand(TelegramLinkStore links, IOptions<TelegramOptions> options) : ISlashCommand
{
    private readonly TelegramOptions _opts = options.Value;
    public string Name => "connect-telegram";
    public string Description => "podłącz swój Telegram do konta";

    public Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken) || !_opts.UsePolling)
            return Task.FromResult("ℹ️ Telegram nie jest skonfigurowany na serwerze.");
        var code = links.Mint(ctx.UserId);
        var link = string.IsNullOrEmpty(_opts.BotUsername)
            ? "" : $"\nLub otwórz: https://t.me/{_opts.BotUsername}?start={code}";
        return Task.FromResult($"📨 Wyślij do bota: /start {code}{link}\n(Kod ważny 15 minut.)");
    }
}

/// <summary>telegram.link — "połącz telegram" / "podłącz mój telegram".</summary>
public sealed class TelegramLinkHandler : IIntentHandler
{
    public string IntentId => "telegram.link";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["telegram.link"];
    public string PromptTemplateId => "link_telegram";
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
}
