using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Signal;

/// <summary>
/// Public webhook for Signal (e.g. a signal-cli receive hook). Plugin-owned via IWebhookHandler —
/// the host maps the route without any Signal-specific code. Publishes the raw payload to the bus.
/// </summary>
public sealed class SignalWebhookHandler(IMessageBus bus) : IWebhookHandler
{
    public string Route => "/webhook/signal";

    public async Task<WebhookResponse> HandleAsync(WebhookRequest request, CancellationToken ct)
    {
        await bus.PublishAsync(new RawEvent("signal", request.Body), ct);
        return WebhookResponse.Ok;
    }
}
