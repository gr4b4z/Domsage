using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Email;

/// <summary>/connect-email &lt;address&gt; — mails a verification code to the address (proving ownership).</summary>
public sealed class ConnectEmailCommand(EmailLinkStore links, EmailSender sender, IOptions<EmailOptions> options) : ISlashCommand
{
    private readonly EmailOptions _opts = options.Value;
    public string Name => "connect-email";
    public string Description => "podłącz adres email (np. /connect-email jan@x.pl)";

    public async Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        var addr = args.Trim().ToLowerInvariant();
        if (!addr.Contains('@')) return "Podaj adres: /connect-email jan@x.pl";
        if (string.IsNullOrEmpty(_opts.SmtpHost)) return "ℹ️ Email nie jest skonfigurowany na serwerze.";

        var code = links.Mint(ctx.UserId, addr);
        await sender.SendAsync(addr, "[Agent] Kod weryfikacyjny",
            $"Twój kod do połączenia tego adresu: {code}\n(Ważny 15 minut.)", null, ct);
        return $"📧 Wysłałem kod na {addr}. Potwierdź: /confirm-email <kod>";
    }
}

/// <summary>/confirm-email &lt;code&gt; — confirms the code → adds the verified address to the user.</summary>
public sealed class ConfirmEmailCommand(EmailLinkStore links, IUserRepository users) : ISlashCommand
{
    public string Name => "confirm-email";
    public string Description => "potwierdź email kodem (np. /confirm-email 123456)";

    public async Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        var pending = links.Consume(args.Trim());
        if (pending is null || pending.Value.UserId != ctx.UserId)
            return "Kod nieprawidłowy lub wygasł.";
        var ok = await users.AddEmailIdentityAsync(ctx.UserId, pending.Value.Address, makePrimary: false, ct);
        return ok ? $"✅ Połączono email: {pending.Value.Address}" : "Nie udało się połączyć adresu.";
    }
}
