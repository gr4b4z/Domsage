using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Signal;

public sealed class SignalOptions
{
    public string ApiBaseUrl { get; set; } = "http://localhost:8090";
    public string OurNumber { get; set; } = "";
}

public sealed class SignalApiClient(HttpClient http, IOptions<SignalOptions> options)
{
    private readonly SignalOptions _opts = options.Value;

    public async Task SendMessageAsync(string recipient, string message, CancellationToken ct)
    {
        var payload = new { message, number = _opts.OurNumber, recipients = new[] { recipient } };
        var resp = await http.PostAsJsonAsync(_opts.ApiBaseUrl.TrimEnd('/') + "/v2/send", payload, ct);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>Signal channel via bbernhard/signal-cli-rest-api. No inline buttons — text confirmation.</summary>
public sealed class SignalChannelPlugin(SignalApiClient api) : IChannelPlugin
{
    public string ChannelId => "signal";
    public ChannelCapabilities Capabilities => new(false, false, false, true);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(e.Body);
        var root = doc.RootElement;
        // signal-cli-rest-api json-rpc envelope: { envelope: { source, dataMessage: { message } } }
        string source = "", text = "";
        if (root.TryGetProperty("envelope", out var env))
        {
            source = env.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
            if (env.TryGetProperty("dataMessage", out var dm) && dm.TryGetProperty("message", out var m))
                text = m.GetString() ?? "";
        }
        // Text-based confirmation: "TAK <id>" / "NIE <id>".
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("TAK", StringComparison.OrdinalIgnoreCase))
            text = "confirm:" + trimmed[3..].Trim();
        else if (trimmed.StartsWith("NIE", StringComparison.OrdinalIgnoreCase))
            text = "cancel:" + trimmed[3..].Trim();

        return Task.FromResult(new InputMessage(
            Guid.NewGuid().ToString(), "signal", source, null, text, null, DateTimeOffset.UtcNow));
    }

    public async Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        var text = m.ConfirmationRequired && m.ConfirmationId is not null
            ? $"{m.Text}\n\n✅ Odpowiedz: TAK {m.ConfirmationId} aby potwierdzić, NIE {m.ConfirmationId} aby anulować."
            : m.Text;
        await api.SendMessageAsync(m.UserId, text, ct);
    }
}
