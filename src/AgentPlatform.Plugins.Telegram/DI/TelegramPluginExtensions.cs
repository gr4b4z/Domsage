using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Telegram.DI;

public static class TelegramPluginExtensions
{
    public static IServiceCollection AddTelegramPlugin(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TelegramOptions>(config.GetSection("Telegram"));
        services.AddHttpClient<TelegramChannelPlugin>();
        services.AddHttpClient(); // IHttpClientFactory for the poller
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<TelegramChannelPlugin>());

        // Inbound: shared update processor + connector (polls in dev, registers a webhook in prod) +
        // the plugin-owned public webhook endpoint (mapped generically by the host via IWebhookHandler).
        services.AddSingleton<TelegramLinkStore>();
        services.AddSingleton<TelegramUpdateProcessor>();
        services.AddHostedService<TelegramConnector>();
        services.AddSingleton<IWebhookHandler, TelegramWebhookHandler>();

        // Conversational linking: "połącz telegram" in chat → mints a code + deep link.
        services.AddSingleton<ITool, TelegramLinkTool>();
        services.AddSingleton<IIntentHandler, TelegramLinkHandler>();
        return services;
    }
}
