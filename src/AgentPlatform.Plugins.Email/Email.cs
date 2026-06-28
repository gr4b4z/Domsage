using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AgentPlatform.Plugins.Email;

public sealed class EmailOptions
{
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string ImapUser { get; set; } = "";
    public string ImapPassword { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string FromAddress { get; set; } = "agent@example.com";
    public string FromName { get; set; } = "Mój Agent";
    public bool SmtpUseSsl { get; set; } = true;
}

public record ParsedEmail(string MessageId, string FromEmail, string? GroupId,
    string BodyText, DateTimeOffset Date);

public sealed partial class EmailParser(IUserRepository users, ILogger<EmailParser> log)
{
    public async Task<ParsedEmail?> ParseAsync(MimeMessage msg, CancellationToken ct)
    {
        var fromAddr = msg.From.OfType<MailboxAddress>().FirstOrDefault()?.Address?.Trim().ToLowerInvariant();
        if (fromAddr is null) return null;
        // Email is a generic channel identity now — any of the user's linked addresses resolves to them.
        var user = await users.GetByChannelIdentityAsync("email", fromAddr, ct);
        if (user is null) { log.LogInformation("Email from unknown sender {Addr} — ignored", fromAddr); return null; }

        var isReply = msg.Subject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true;
        var bodyText = ExtractText(msg.HtmlBody ?? msg.TextBody ?? "");

        if (isReply)
        {
            var head = bodyText.TrimStart();
            var refMatch = RefRegex().Match(msg.TextBody ?? bodyText);
            var confId = refMatch.Success ? refMatch.Groups[1].Value : "";
            if (head.StartsWith("TAK", StringComparison.OrdinalIgnoreCase)) bodyText = $"confirm:{confId}";
            else if (head.StartsWith("NIE", StringComparison.OrdinalIgnoreCase)) bodyText = $"cancel:{confId}";
        }

        return new ParsedEmail(msg.MessageId ?? Guid.NewGuid().ToString(),
            fromAddr, user.GroupId, bodyText, msg.Date);
    }

    private static string ExtractText(string body) => TagRegex().Replace(body, " ").Trim();

    [GeneratedRegex("<[^>]+>")] private static partial Regex TagRegex();
    [GeneratedRegex(@"Ref:\s*([0-9a-fA-F\-]+)")] private static partial Regex RefRegex();
}

public sealed class EmailSender(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _o = options.Value;
    public async Task SendAsync(string to, string subject, string body, string? inReplyTo, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_o.SmtpHost)) return;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };
        if (inReplyTo is not null) msg.InReplyTo = inReplyTo;

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.SmtpHost, _o.SmtpPort,
            _o.SmtpUseSsl ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(_o.SmtpUser))
            await client.AuthenticateAsync(_o.SmtpUser, _o.SmtpPassword, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Hangfire recurring job — polls IMAP for unseen messages and publishes to the bus.</summary>
public sealed class ImapPoller(EmailParser parser, IMessageBus bus, IOptions<EmailOptions> options,
    ILogger<ImapPoller> log) : IScheduledJob
{
    private readonly EmailOptions _o = options.Value;

    public string JobId => "email.imap-poll";
    public string Cron => "*/2 * * * *";
    public Task RunAsync(CancellationToken ct) => PollAsync(ct);

    public async Task PollAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_o.ImapHost)) return;
        using var client = new ImapClient();
        await client.ConnectAsync(_o.ImapHost, _o.ImapPort, true, ct);
        await client.AuthenticateAsync(_o.ImapUser, _o.ImapPassword, ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        foreach (var uid in await inbox.SearchAsync(SearchQuery.NotSeen, ct))
        {
            var message = await inbox.GetMessageAsync(uid, ct);
            var parsed = await parser.ParseAsync(message, ct);
            if (parsed is not null)
                await bus.PublishAsync(new RawEvent("email", JsonSerializer.Serialize(parsed)), ct);
            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        }
        await client.DisconnectAsync(true, ct);
        log.LogInformation("IMAP poll complete");
    }
}

public sealed class EmailChannelPlugin(EmailSender sender) : IChannelPlugin
{
    public string ChannelId => "email";
    public ChannelCapabilities Capabilities => new(false, false, false, true);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<ParsedEmail>(e.Body)!;
        // UserId carries the sender email so the pipeline resolves it via GetByEmailAsync.
        return Task.FromResult(new InputMessage(p.MessageId, "email", p.FromEmail, p.GroupId,
            p.BodyText, null, p.Date));
    }

    public async Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        if (!m.UserId.Contains('@')) return; // can only reply to an email address
        var subject = m.ConfirmationRequired ? "[Agent] Potwierdzenie wymagane" : "[Agent] Odpowiedź";
        var body = m.ConfirmationRequired
            ? $"{m.Text}\n\nOdpisz TAK aby potwierdzić lub NIE aby anulować.\n(Ref: {m.ConfirmationId})"
            : m.Text;
        await sender.SendAsync(m.UserId, subject, body, m.ConfirmationId, ct);
    }
}
