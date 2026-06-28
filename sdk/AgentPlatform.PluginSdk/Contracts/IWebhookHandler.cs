namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// Lets a plugin expose its own public HTTP endpoint (e.g. a Telegram/Signal/Stripe webhook) without
/// the host knowing anything about it. The host discovers every <see cref="IWebhookHandler"/> and maps
/// its <see cref="Route"/> as a POST endpoint — generic, no plugin-specific code in the host.
/// The handler is responsible for its own authentication (e.g. verifying a webhook secret header).
/// </summary>
public interface IWebhookHandler
{
    /// <summary>Absolute path to map, e.g. "/webhook/telegram". Must be unique across plugins.</summary>
    string Route { get; }

    Task<WebhookResponse> HandleAsync(WebhookRequest request, CancellationToken ct);
}

/// <summary>The raw inbound request: case-insensitive headers + the body as a string.</summary>
public sealed record WebhookRequest(IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>What to return to the caller. Keep bodies tiny — webhooks only need an ack.</summary>
public sealed record WebhookResponse(int StatusCode, string? Body = null)
{
    public static WebhookResponse Ok { get; } = new(200);
    public static WebhookResponse Unauthorized { get; } = new(401);
}
