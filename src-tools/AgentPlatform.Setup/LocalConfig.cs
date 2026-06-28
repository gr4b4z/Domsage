namespace AgentPlatform.Setup;

public sealed class LocalConfig
{
    public string LlmProvider { get; set; } = "OpenAI";
    public string LlmEndpoint { get; set; } = "https://api.openai.com/v1";
    public string? LlmApiKey { get; set; }
    public string LlmSmallModel { get; set; } = "gpt-4o-mini";
    public string LlmLargeModel { get; set; } = "gpt-4o";
    public string TelegramBotToken { get; set; } = "";
    public string TelegramWebhookSecret { get; set; } = "";
    public string WebhookBaseUrl { get; set; } = "";
    public string PostgresConnectionString { get; set; } = "";
    public string BlobStoragePath { get; set; } = "";
    public Dictionary<string, string> PluginConfig { get; set; } = new();

    public object ToAppsettings()
    {
        var plugins = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (key, value) in PluginConfig)
        {
            var parts = key.Split(':', 2);
            if (parts.Length != 2) continue;
            if (!plugins.TryGetValue(parts[0], out var inner)) plugins[parts[0]] = inner = new();
            inner[parts[1]] = value;
        }

        return new
        {
            ConnectionStrings = new { Postgres = PostgresConnectionString },
            Telegram = new
            {
                BotToken = TelegramBotToken,
                WebhookSecret = TelegramWebhookSecret,
                WebhookUrl = string.IsNullOrEmpty(WebhookBaseUrl) ? "" : $"{WebhookBaseUrl.TrimEnd('/')}/webhook/telegram"
            },
            Llm = new
            {
                ProviderId = LlmProvider.ToLowerInvariant().Contains("azure") ? "azure-openai" : "openai",
                Endpoint = LlmEndpoint,
                ApiKey = LlmApiKey,
                Models = new { Small = LlmSmallModel, Medium = LlmSmallModel, Large = LlmLargeModel }
            },
            BlobStorage = new { Provider = "local", LocalPath = BlobStoragePath },
            Plugins = plugins
        };
    }
}
