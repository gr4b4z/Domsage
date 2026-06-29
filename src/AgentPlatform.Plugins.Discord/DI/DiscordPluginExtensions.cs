using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Discord.DI;

public static class DiscordPluginExtensions
{
    public static IServiceCollection AddDiscordPlugin(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DiscordOptions>(config.GetSection("Plugins:Discord"));
        services.AddHttpClient<DiscordChannelPlugin>();
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<DiscordChannelPlugin>());
        services.AddSingleton<DiscordLinkStore>();
        services.AddScoped<ISlashCommand, ConnectDiscordCommand>();
        services.AddHostedService<DiscordGateway>();
        return services;
    }
}
