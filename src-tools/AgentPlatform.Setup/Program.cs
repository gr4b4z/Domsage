using AgentPlatform.Setup;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".agentplatform", "config.json");

// Subcommand: link a Telegram chat to a user from the CLI (alternative to the web "Połącz Telegram").
if (args.Length > 0 && args[0] is "link-telegram" or "link")
    return await TelegramLinkCommand.RunAsync(configPath, args);

await SetupWizard.RunAsync(configPath);
return 0;

