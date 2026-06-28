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
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<TelegramChannelPlugin>());
        return services;
    }
}
