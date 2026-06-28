using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Email.DI;

public static class EmailPluginExtensions
{
    public static IServiceCollection AddEmailPlugin(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddScoped<EmailParser>();
        services.AddSingleton<EmailSender>();
        services.AddSingleton<EmailLinkStore>();  // self-service email verification codes
        services.AddSingleton<EmailLoginStore>(); // passwordless email login codes
        services.AddScoped<ISlashCommand, ConnectEmailCommand>();
        services.AddScoped<ISlashCommand, ConfirmEmailCommand>();
        services.AddScoped<ImapPoller>();
        services.AddScoped<IScheduledJob, ImapPoller>();
        services.AddSingleton<EmailChannelPlugin>();
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<EmailChannelPlugin>());
        return services;
    }
}
