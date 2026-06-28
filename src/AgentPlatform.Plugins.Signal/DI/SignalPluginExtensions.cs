using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Signal.DI;

public static class SignalPluginExtensions
{
    public static IServiceCollection AddSignalPlugin(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SignalOptions>(config.GetSection("Signal"));
        services.AddHttpClient<SignalApiClient>();
        services.AddSingleton<SignalChannelPlugin>();
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<SignalChannelPlugin>());
        return services;
    }
}
