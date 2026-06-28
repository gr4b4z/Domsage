using System.Text.Json;
using Spectre.Console;

namespace AgentPlatform.Setup;

public static class SetupWizard
{
    public static Task RunAsync(string configPath)
    {
        AnsiConsole.Write(new FigletText("Agent Platform").Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]First run setup — Ctrl+C to abort[/]\n");

        var config = new LocalConfig();

        config.LlmProvider = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Wybierz [green]provider LLM[/]:")
            .AddChoices("OpenAI", "Azure OpenAI", "Ollama (lokalny)"));
        config.LlmEndpoint = AnsiConsole.Prompt(new TextPrompt<string>("Endpoint URL:")
            .DefaultValue(DefaultEndpoint(config.LlmProvider)));
        if (!config.LlmProvider.Contains("Ollama"))
            config.LlmApiKey = AnsiConsole.Prompt(new TextPrompt<string>("API Key:").Secret());
        config.LlmSmallModel = AnsiConsole.Prompt(new TextPrompt<string>("Model (Small):").DefaultValue("gpt-4o-mini"));
        config.LlmLargeModel = AnsiConsole.Prompt(new TextPrompt<string>("Model (Large):").DefaultValue("gpt-4o"));

        AnsiConsole.MarkupLine("\n[yellow]Telegram Bot (opcjonalnie)[/]");
        config.TelegramBotToken = AnsiConsole.Prompt(new TextPrompt<string>("Bot Token (Enter aby pominąć):").AllowEmpty().Secret());
        config.TelegramWebhookSecret = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrEmpty(config.TelegramBotToken))
            config.WebhookBaseUrl = AnsiConsole.Prompt(new TextPrompt<string>("Publiczny URL (np. https://xyz.trycloudflare.com):").AllowEmpty());

        AnsiConsole.MarkupLine("\n[yellow]PostgreSQL[/]");
        config.PostgresConnectionString = AnsiConsole.Prompt(new TextPrompt<string>("Connection string:")
            .DefaultValue("Host=localhost;Database=agentplatform;Username=app;Password=localdev"));

        config.BlobStoragePath = AnsiConsole.Prompt(new TextPrompt<string>("Ścieżka dla plików:")
            .DefaultValue(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentplatform", "blobs")));

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(config.BlobStoragePath);
        var json = JsonSerializer.Serialize(config.ToAppsettings(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);

        AnsiConsole.MarkupLine($"\n[green]✓[/] Konfiguracja zapisana: {configPath}");
        AnsiConsole.MarkupLine("[grey]Następny krok: docker compose up[/]");
        AnsiConsole.MarkupLine("[grey]Po starcie wywołaj POST /api/setup/init aby utworzyć konto admina.[/]");
        return Task.CompletedTask;
    }

    private static string DefaultEndpoint(string provider) => provider switch
    {
        "Azure OpenAI" => "https://your-resource.openai.azure.com/",
        "Ollama (lokalny)" => "http://localhost:11434/v1",
        _ => "https://api.openai.com/v1"
    };
}
