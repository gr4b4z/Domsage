using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Plugins.Automation;

/// <summary>/automations — lists the user's automation rules (deterministic, no LLM).</summary>
public sealed class ListAutomationsCommand(AppDbContext db) : ISlashCommand
{
    public string Name => "automations";
    public string Description => "pokaż swoje automatyzacje (reguły „jeśli… to powiadom”)";

    public async Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        var uid = Guid.Parse(ctx.UserId);
        var rules = await db.AutomationRules.AsNoTracking()
            .Where(r => r.UserId == uid)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        if (rules.Count == 0)
            return "Nie masz jeszcze żadnych automatyzacji. Napisz np. „sprawdzaj codziennie rano czy ma padać i daj znać”.";

        var lines = rules.Select(r =>
        {
            var localNext = r.NextRunAt.ToOffset(TimeSpan.FromHours(2));
            var status = r.Enabled ? "" : " (wyłączona)";
            return $"• [{r.Id.ToString()[..8]}] {Label(r.Description)}{status}\n" +
                   $"    warunek: {r.ConditionPath} {r.ConditionOp} {r.ConditionValue} · następne sprawdzenie: {localNext:dd.MM HH:mm}";
        });
        return "🔔 Twoje automatyzacje:\n" + string.Join("\n", lines) +
               "\n\nUsuń przez: /automation-delete <id>";
    }

    private static string Label(string d) => string.IsNullOrWhiteSpace(d) ? "Automatyzacja" : d;
}

/// <summary>/automation-delete &lt;id-prefix&gt; — removes one of the user's rules.</summary>
public sealed class DeleteAutomationCommand(AppDbContext db) : ISlashCommand
{
    public string Name => "automation-delete";
    public string Description => "usuń automatyzację (np. /automation-delete 0993d7d5)";

    public async Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct)
    {
        var prefix = args.Trim();
        if (prefix.Length < 4) return "Podaj co najmniej 4 znaki id: /automation-delete <id>";

        var uid = Guid.Parse(ctx.UserId);
        var matches = await db.AutomationRules.AsNoTracking()
            .Where(r => r.UserId == uid)
            .ToListAsync(ct);
        var hit = matches.Where(r => r.Id.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        if (hit.Count == 0) return $"Nie znalazłem automatyzacji o id zaczynającym się od „{prefix}”.";
        if (hit.Count > 1) return "Pasuje więcej niż jedna — podaj dłuższy fragment id.";

        await db.AutomationRules.Where(r => r.Id == hit[0].Id).ExecuteDeleteAsync(ct);
        return $"🗑️ Usunąłem automatyzację „{Label(hit[0].Description)}”.";
    }

    private static string Label(string d) => string.IsNullOrWhiteSpace(d) ? "Automatyzacja" : d;
}
