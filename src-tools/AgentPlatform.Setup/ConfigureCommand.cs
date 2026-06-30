using Spectre.Console;

namespace AgentPlatform.Setup;

/// <summary>
/// <c>agent configure [plugin]</c> — schema-driven wizard. With no argument it lists the plugins it
/// discovered in the scan folder; with a plugin id it walks every field, defaulting to the current
/// value from config.json, masking secrets, running any non-blocking validation hook, then writes the
/// config and prints a "restart required" notice. Pure I/O glue around <see cref="SchemaLoader"/>,
/// <see cref="ConfigureMerge"/> and <see cref="ValidationHooks"/>.
/// </summary>
public static class ConfigureCommand
{
    public static async Task<int> RunAsync(string configPath, string pluginsFolder, string[] args)
    {
        var schemas = SchemaLoader.Scan(pluginsFolder);
        if (schemas.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Brak schematów konfiguracji[/] w {pluginsFolder}.");
            AnsiConsole.MarkupLine("[grey]Wrzuć plik *.config-schema.json do tego folderu, aby plugin się pojawił.[/]");
            return 1;
        }

        // args[0] == "configure"; args[1] (optional) == plugin id.
        var requested = args.Length > 1 ? args[1] : null;

        var schema = requested is null
            ? PickPlugin(schemas)
            : schemas.FirstOrDefault(s => string.Equals(s.PluginId, requested, StringComparison.OrdinalIgnoreCase));

        if (schema is null)
        {
            AnsiConsole.MarkupLine($"[red]Nieznany plugin:[/] {Markup.Escape(requested ?? "")}");
            AnsiConsole.MarkupLine($"[grey]Dostępne:[/] {string.Join(", ", schemas.Select(s => s.PluginId))}");
            return 1;
        }

        AnsiConsole.Write(new Rule($"[green]{Markup.Escape(schema.DisplayName)}[/]").LeftJustified());

        var existing = ConfigureMerge.Read(configPath);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            existing.TryGetValue($"{schema.ConfigSection}:{field.Key}", out var current);
            var value = PromptField(field, current);
            values[field.Key] = value;

            if (!string.IsNullOrEmpty(field.Validate))
            {
                // Validate against everything entered so far (host/port/user/password together).
                var probe = MergeForProbe(schema, existing, values);
                if (!await RunValidationLoopAsync(field, probe, values))
                    return 130; // user aborted
            }
        }

        ConfigureMerge.Apply(configPath, schema.ConfigSection, values);

        AnsiConsole.MarkupLine($"\n[green]✓[/] Zapisano: {Markup.Escape(configPath)}");
        AnsiConsole.MarkupLine("[yellow]Restart wymagany[/] — zrestartuj API, aby zmiany weszły w życie.");
        return 0;
    }

    private static PluginConfigSchemaFile PickPlugin(IReadOnlyList<PluginConfigSchemaFile> schemas)
    {
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Wybierz [green]plugin[/] do skonfigurowania:")
            .AddChoices(schemas.Select(s => $"{s.PluginId}  [grey]({s.DisplayName})[/]")));
        var id = choice.Split("  ", 2)[0];
        return schemas.First(s => s.PluginId == id);
    }

    /// <summary>Prompts a single field; Enter returns the current value (or empty) → preserved by Apply.</summary>
    private static string PromptField(ConfigField field, string? current)
    {
        var prompt = new TextPrompt<string>($"{field.Label}:").AllowEmpty();

        if (field.IsSecret)
            prompt.Secret();

        if (!string.IsNullOrEmpty(current))
            prompt.DefaultValue(current).ShowDefaultValue(!field.IsSecret); // never echo a secret
        else if (!string.IsNullOrWhiteSpace(field.Hint))
            prompt.DefaultValue(field.Hint).ShowDefaultValue();

        var value = AnsiConsole.Prompt(prompt);

        // Required + nothing entered + no existing value → keep asking.
        while (field.Required && string.IsNullOrWhiteSpace(value) && string.IsNullOrEmpty(current))
        {
            AnsiConsole.MarkupLine("[red]To pole jest wymagane.[/]");
            value = AnsiConsole.Prompt(prompt);
        }

        // If the user kept the existing value via the default, don't re-write it (preserve).
        return string.Equals(value, current, StringComparison.Ordinal) ? "" : value;
    }

    /// <summary>Runs the field's validation hook; on failure offers [R]e-enter / [C]ontinue anyway.</summary>
    private static async Task<bool> RunValidationLoopAsync(
        ConfigField field, IReadOnlyDictionary<string, string> probe, Dictionary<string, string> values)
    {
        while (true)
        {
            var (ok, error) = await ValidationHooks.RunAsync(field.Validate, probe);
            if (ok)
                return true;

            AnsiConsole.MarkupLine($"[red]Walidacja nie powiodła się[/] ({field.Validate}): {Markup.Escape(error)}");
            var choice = AnsiConsole.Prompt(new TextPrompt<string>("[[R]]e-enter / [[C]]ontinue anyway?")
                .DefaultValue("R").ShowDefaultValue(false)
                .Validate(c => string.Equals(c, "R", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c, "C", StringComparison.OrdinalIgnoreCase)));

            if (string.Equals(choice, "C", StringComparison.OrdinalIgnoreCase))
                return true; // save as-is

            var current = values.TryGetValue(field.Key, out var v) ? v : null;
            values[field.Key] = PromptField(field, current);
            probe = new Dictionary<string, string>((Dictionary<string, string>)probe) { [field.Key] = values[field.Key] };
        }
    }

    /// <summary>Builds the value set a hook sees: existing config under the section, overlaid with edits.</summary>
    private static Dictionary<string, string> MergeForProbe(
        PluginConfigSchemaFile schema, Dictionary<string, string> existing, Dictionary<string, string> values)
    {
        var probe = new Dictionary<string, string>(StringComparer.Ordinal);
        var prefix = $"{schema.ConfigSection}:";
        foreach (var (k, v) in existing)
            if (k.StartsWith(prefix, StringComparison.Ordinal))
                probe[k[prefix.Length..]] = v;
        foreach (var (k, v) in values)
            if (!string.IsNullOrEmpty(v))
                probe[k] = v;
        return probe;
    }
}
