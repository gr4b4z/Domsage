using System.Text.Json;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace AgentPlatform.Setup;

/// <summary>
/// `link-email --user &lt;userId&gt; --email &lt;address&gt; [--primary]` — adds an email address to a user
/// (a user may have several). The first becomes primary; pass --primary to promote a later one.
/// Admin-side linking (trusted) — no verification needed, unlike the self-service web flow.
/// With no/partial args it lists users.
/// </summary>
public static class EmailLinkCommand
{
    public static async Task<int> RunAsync(string configPath, string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ResolveConnectionString(configPath))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(opts);

        var userId = Flag(args, "--user");
        var addr = Flag(args, "--email");
        var primary = Array.IndexOf(args, "--primary") >= 0;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(addr))
        {
            AnsiConsole.MarkupLine("[yellow]Użycie:[/] link-email --user <userId> --email <adres> [--primary]\n");
            try
            {
                var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName)
                    .Select(u => new { u.Id, u.DisplayName }).ToListAsync();
                AnsiConsole.MarkupLine(users.Count == 0 ? "[grey](brak użytkowników)[/]" : "Dostępni użytkownicy:");
                foreach (var u in users)
                    AnsiConsole.MarkupLine($"  [green]{u.Id}[/]  {Markup.Escape(u.DisplayName)}");
            }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Błąd odczytu użytkowników:[/] {Markup.Escape(ex.Message)}"); }
            return 1;
        }

        var ok = await new UserRepository(db).AddEmailIdentityAsync(userId, addr, primary, CancellationToken.None);
        if (ok) AnsiConsole.MarkupLine($"[green]✅ Podpięto[/] email [green]{Markup.Escape(addr)}[/] do usera [green]{userId}[/]{(primary ? " (primary)" : "")}");
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
            catch { }
        }
        return "Host=localhost;Database=agentplatform;Username=app;Password=localdev";
    }
}
