using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Http.DI;

public static class HttpPluginExtensions
{
    public static IServiceCollection AddHttpChannelPlugin(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<HttpChannelPlugin>();
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<HttpChannelPlugin>());
        services.AddScoped<UserTokenAuthenticator>();
        return services;
    }
}
