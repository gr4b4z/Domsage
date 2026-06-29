using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Discord;

/// <summary>Pure decision for an inbound DM — no IO. The gateway executes the side effect.</summary>
public abstract record DiscordDecision;
/// <summary>"/start &lt;code&gt;" with a valid code → link this Discord account to the user.</summary>
public sealed record LinkAccount(string UserId, string AuthorId) : DiscordDecision;
/// <summary>Normal message → hand to the pipeline via the bus.</summary>
public sealed record PublishMessage(string AuthorId, string Text) : DiscordDecision;
/// <summary>Send a DM back (linking prompt / result) without involving the pipeline.</summary>
public sealed record ReplyDm(string Text) : DiscordDecision;
/// <summary>Nothing actionable (e.g. attachment-only / empty) → ignore.</summary>
public sealed record IgnoreDm : DiscordDecision;

/// <summary>Routes an inbound DM to a decision. "/start &lt;code&gt;" links (consuming the code); blank text is ignored.</summary>
public static class DiscordDmRouter
{
    public static DiscordDecision Route(string authorId, string text, DiscordLinkStore links)
    {
        var trimmed = (text ?? "").Trim();

        if (trimmed.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var code = trimmed.Length > 6 ? trimmed[6..].Trim() : "";
            if (code.Length == 0)
                return new ReplyDm("Cześć! Aby połączyć konto, w aplikacji kliknij „Połącz Discord” i wyślij mi tutaj: /start <kod>.");
            var userId = links.Consume(code);
            return userId is null
                ? new ReplyDm("Kod nieprawidłowy lub wygasł — wygeneruj nowy w aplikacji.")
                : new LinkAccount(userId, authorId);
        }

        if (trimmed.Length == 0) return new IgnoreDm();      // attachment-only / empty → text-only MVP ignores
        return new PublishMessage(authorId, trimmed);
    }
}

/// <summary>Parses the gateway's RawEvent body ({"authorId","text"}) into an InputMessage on the "discord" channel.</summary>
public static class DiscordParse
{
    public static InputMessage Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var authorId = root.TryGetProperty("authorId", out var a) ? a.GetString() ?? "" : "";
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return new InputMessage(Guid.NewGuid().ToString(), "discord", authorId, null, text, null, DateTimeOffset.UtcNow);
    }
}
