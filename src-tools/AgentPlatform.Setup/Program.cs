using AgentPlatform.Setup;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".agentplatform", "config.json");

// Subcommands: link a messaging account to a user from the CLI.
if (args.Length > 0 && args[0] is "link-telegram" or "link")
    return await TelegramLinkCommand.RunAsync(configPath, args);
if (args.Length > 0 && args[0] == "link-email")
    return await EmailLinkCommand.RunAsync(configPath, args);

await SetupWizard.RunAsync(configPath);
return 0;

