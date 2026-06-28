using AgentPlatform.Setup;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".agentplatform", "config.json");

// Subcommands: connect a messaging account to a user from the CLI (link-* kept as aliases).
if (args.Length > 0 && args[0] is "connect-telegram" or "link-telegram" or "link")
    return await TelegramLinkCommand.RunAsync(configPath, args);
if (args.Length > 0 && args[0] is "connect-email" or "link-email")
    return await EmailLinkCommand.RunAsync(configPath, args);

await SetupWizard.RunAsync(configPath);
return 0;

