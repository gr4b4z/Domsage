using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace AgentPlatform.Setup;

/// <summary>
/// Named, non-blocking validation checks the CLI runs after a field is entered. Plugins ship only the
/// hook <em>name</em>; the logic lives here. An unknown / inapplicable hook is a no-op that returns ok,
/// so it never blocks a save. A real failure returns <c>(false, reason)</c> and the CLI offers
/// <c>[R]e-enter / [C]ontinue</c> — the failure is surfaced, never silently swallowed.
/// </summary>
public static class ValidationHooks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>Runs the named hook against the values entered so far.</summary>
    public static async Task<(bool ok, string error)> RunAsync(string? hookName, IReadOnlyDictionary<string, string> fields)
    {
        try
        {
            return hookName switch
            {
                "imap-ping" => await ImapPingAsync(fields),
                "smtp-ping" => await SmtpPingAsync(fields),
                _ => (true, ""), // unknown / inapplicable → never blocks
            };
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(bool ok, string error)> ImapPingAsync(IReadOnlyDictionary<string, string> fields)
    {
        var host = Get(fields, "ImapHost");
        var port = GetPort(fields, "ImapPort", 993);
        if (string.IsNullOrWhiteSpace(host))
            return (true, ""); // nothing to check yet

        var user = Get(fields, "ImapUser");
        var pass = Get(fields, "ImapPassword");

        using var cts = new CancellationTokenSource(Timeout);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cts.Token);

        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(host);
        using var reader = new StreamReader(ssl, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(ssl, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        var greeting = await reader.ReadLineAsync(cts.Token);
        if (greeting is null || !greeting.Contains("OK", StringComparison.OrdinalIgnoreCase))
            return (false, $"Brak poprawnego powitania IMAP: {greeting}");

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            return (true, ""); // reachable; no credentials to test LOGIN with

        await writer.WriteLineAsync($"a1 LOGIN {Quote(user)} {Quote(pass)}");
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (line.StartsWith("a1 OK", StringComparison.OrdinalIgnoreCase))
                return (true, "");
            if (line.StartsWith("a1 NO", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("a1 BAD", StringComparison.OrdinalIgnoreCase))
                return (false, $"IMAP LOGIN odrzucony: {line}");
        }
        return (false, "IMAP LOGIN — brak odpowiedzi serwera.");
    }

    private static async Task<(bool ok, string error)> SmtpPingAsync(IReadOnlyDictionary<string, string> fields)
    {
        var host = Get(fields, "SmtpHost");
        var port = GetPort(fields, "SmtpPort", 587);
        if (string.IsNullOrWhiteSpace(host))
            return (true, "");

        using var cts = new CancellationTokenSource(Timeout);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cts.Token);

        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
        var banner = await reader.ReadLineAsync(cts.Token);
        if (banner is not null && banner.StartsWith("220"))
            return (true, "");
        return (false, $"Nieoczekiwany banner SMTP: {banner}");
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var v) ? v : "";

    private static int GetPort(IReadOnlyDictionary<string, string> fields, string key, int fallback)
        => int.TryParse(Get(fields, key), out var p) && p > 0 ? p : fallback;

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
