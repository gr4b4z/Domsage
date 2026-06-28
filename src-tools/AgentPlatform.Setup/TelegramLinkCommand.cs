using System.Text.Json;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace AgentPlatform.Setup;

/// <summary>
/// `connect-telegram --user &lt;userId&gt; --telegram &lt;chatId&gt;` — binds a Telegram chat to a user
/// straight in the DB (channel_identities), so you don't have to use the web "Połącz Telegram" flow.
/// With no/partial args it lists users so you can copy an id.
/// </summary>
public static class TelegramLinkCommand
{
    public static async Task<int> RunAsync(string configPath, string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ResolveConnectionString(configPath))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(opts);

        var userId = Flag(args, "--user");
        var chat = Flag(args, "--telegram") ?? Flag(args, "--chat");

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(chat))
        {
            AnsiConsole.MarkupLine("[yellow]Użycie:[/] connect-telegram --user <userId> --telegram <chatId>");
            AnsiConsole.MarkupLine("[grey](chatId to numer Twojego czatu z botem — bot pokazuje go na /start)[/]\n");
            try
            {
                var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName)
                    .Select(u => new { u.Id, u.DisplayName }).ToListAsync();
                if (users.Count == 0) { AnsiConsole.MarkupLine("[grey](brak użytkowników w bazie)[/]"); }
                else
                {
                    AnsiConsole.MarkupLine("Dostępni użytkownicy:");
                    foreach (var u in users)
                        AnsiConsole.MarkupLine($"  [green]{u.Id}[/]  {Markup.Escape(u.DisplayName)}");
                }
            }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Nie udało się odczytać użytkowników:[/] {Markup.Escape(ex.Message)}"); }
            return 1;
        }

        var ok = await new UserRepository(db).SetChannelIdentityAsync(userId, "telegram", chat, CancellationToken.None);
        if (ok) AnsiConsole.MarkupLine($"[green]✅ Połączono[/] user [green]{userId}[/] ↔ telegram [green]{chat}[/]");
        else AnsiConsole.MarkupLine("[red]❌ Nie znaleziono użytkownika o tym id.[/]");
        return ok ? 0 : 1;
    }

    private static string? Flag(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string ResolveConnectionString(string configPath)
    {
        var env = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (!string.IsNullOrEmpty(env)) return env;
        if (File.Exists(configPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                    && cs.TryGetProperty("Postgres", out var pg)
                    && pg.GetString() is { Length: > 0 } s)
                    return s;
            }
            catch { /* fall through to default */ }
        }
        return "Host=localhost;Database=agentplatform;Username=app;Password=localdev";
    }
}
