using AgentPlatform.Setup;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".agentplatform", "config.json");

await SetupWizard.RunAsync(configPath);

