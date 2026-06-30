# SPEC: Guided CLI config wizard
Status: Draft
Tryb: Plugin Only (src-tools, nie nowy plugin)
Data: 2026-06-30
Źródło: BMAD SPEC.md (_bmad-output/specs/spec-config-cli-admin-panel/SPEC.md)

## Co i dlaczego
Domowi użytkownicy nie mogą skonfigurować pluginu po dniu 0 bez ręcznego otwierania
`~/.agentplatform/config.json` — `agent configure email` prowadzi za rączkę przez
każde pole, czyta istniejące wartości jako domyślne i zapisuje zaktualizowany config.

## Przykład użycia
Użytkownik: `agent configure email`
System: zadaje po kolei pytania (serwer IMAP, port, login, hasło itd.), każde z domyślną
z obecnego config.json, maskuje sekrety, po haśle IMAP testuje połączenie → sukces lub
`[R]e-enter / [C]ontinue anyway`, zapisuje config.json, drukuje "Restart wymagany".

## Edge Cases
- IMAP ping fail → `[R]e-enter / [C]ontinue` — użytkownik może zapisać mimo błędu
- Enter na wszystkich polach → stare wartości zachowane bez zmiany
- Plugin bez `config-schema.json` w folderze → nie pojawia się w menu (nie blokuje innych)
- Zewnętrzny plugin wrzucony z `config-schema.json` → pojawia się bez rekompilacji CLI
- `agent configure` bez argumentu → wyświetla dostępne pluginy z foldera skanowania

## Korekta schema-format.md
`field.key` = nazwa property C# (`ImapHost`, nie `Imap.Host`).
`configSection` = sama nazwa pluginu (`"Email"`, nie `"Plugins:Email"`).
CLI buduje klucz `PluginConfig`: `$"{configSection}:{field.Key}"` → np. `"Email:ImapHost"`.
`LocalConfig.ToAppsettings()` (LocalConfig.cs:17-22) mapuje to na `Plugins.Email.ImapHost`. ✓

## Scope
IN:  subkomenda `configure`, SchemaLoader (skan folderu *.config-schema.json), ConfigureMerge
     (read-overwrite config.json), validate hooks (imap-ping, smtp-ping — nieblokujące),
     email.config-schema.json jako pierwszy przykład, standalone binary publish,
     refaktoryzacja SetupWizard na wspólny loader
OUT: OAuth flows przez CLI, admin panel jako edytor config, runtime changes bez restartu,
     status dashboard, ciche ignorowanie błędów walidacji, schematy dla pozostałych pluginów
     (Telegram/Discord/Signal/Google/WebSearch — additive, nie blocking)

## Pliki do UTWORZENIA
- `src-tools/AgentPlatform.Setup/PluginConfigSchema.cs`
  `record PluginConfigSchemaFile(string PluginId, string DisplayName, string ConfigSection, ConfigField[] Fields)`
  `record ConfigField(string Key, string Label, string? Hint, bool IsSecret, bool Required, string? Validate)`
- `src-tools/AgentPlatform.Setup/SchemaLoader.cs`
  `SchemaLoader.Scan(pluginsFolder)` → `IReadOnlyList<PluginConfigSchemaFile>` (glob *.config-schema.json)
  `SchemaLoader.TryLoad(path)` → `PluginConfigSchemaFile?` (skip on malformed)
- `src-tools/AgentPlatform.Setup/ConfigureMerge.cs`   ← PURE LOGIC (testowalny)
  `ConfigureMerge.Read(configPath)` → `Dictionary<string,string>` (istniejące wartości sekcji Plugins)
  `ConfigureMerge.Apply(configPath, pluginId, fieldValues)` → nadpisuje config.json
- `src-tools/AgentPlatform.Setup/ValidationHooks.cs`
  `ValidationHooks.RunAsync(hookName, fields)` → `(bool ok, string error)`
  Hooki: `"imap-ping"` (TCP + IMAP LOGIN), `"smtp-ping"` (TCP banner check)
- `src-tools/AgentPlatform.Setup/ConfigureCommand.cs`
  Spectre.Console wizard loop; po validate fail: prompt `[R]e-enter / [C]ontinue`; kończy `ConfigureMerge.Apply` + komunikat restart
- `src/AgentPlatform.Plugins.Email/email.config-schema.json`
  Fields: ImapHost(hint:imap.gmail.com), ImapPort(hint:993), ImapUser, ImapPassword(secret,validate:imap-ping),
          SmtpHost(hint:smtp.gmail.com), SmtpPort(hint:587), SmtpUser, SmtpPassword(secret,validate:smtp-ping),
          FromAddress(hint:agent@example.com), FromName(hint:Mój Agent)

## Pliki do MODYFIKACJI
- `src-tools/AgentPlatform.Setup/Program.cs`
  Dodaj routing: `args[0] is "configure"` → `ConfigureCommand.RunAsync(configPath, pluginsFolder, args)`
  `pluginsFolder` = `~/.agentplatform/plugins` (lub override z env)
- `src-tools/AgentPlatform.Setup/SetupWizard.cs`
  Refaktoryzacja: LLM fields → `llm.config-schema.json` (lub inline schema w kodzie)
  używa `ConfigureMerge.Apply` zamiast ręcznego JSON serialize
- `src-tools/AgentPlatform.Setup/AgentPlatform.Setup.csproj`
  Dodaj: `<PublishSingleFile>true</PublishSingleFile>`, `<SelfContained>true</SelfContained>`,
  `<RuntimeIdentifiers>osx-arm64;osx-x64;linux-x64;win-x64</RuntimeIdentifiers>`
- `README.md` — sekcja CLI: `agent configure <plugin>`, dostępne pluginy, przykład email
- `docs/ARCHITECTURE.md` — wzmianka o schema-driven CLI config w sekcji Setup CLI

## Pliki KTÓRYCH NIE DOTYKAMY
- `src/AgentPlatform.Core/Pipeline/*`
- `sdk/AgentPlatform.PluginSdk/Contracts/*`
- `src/AgentPlatform.Infrastructure/Postgres/AppDbContext.cs`
- `src/AgentPlatform.Api/Program.cs` (brak rejestracji — to CLI tool, nie plugin)

## Tests First
Plik: `tests/AgentPlatform.Core.Tests/CliConfigureTests.cs`
Wymaga: `<ProjectReference Include="../../../src-tools/AgentPlatform.Setup/AgentPlatform.Setup.csproj" />`
w `tests/AgentPlatform.Core.Tests/AgentPlatform.Core.Tests.csproj`
Klasa: `CliConfigureTests` (xUnit, [Fact]/[Theory])
Przypadki:
- `ConfigureMerge_Apply_WritesFieldsToPluginsSection` — happy path, weryfikuje JSON po apply
- `ConfigureMerge_Apply_PreservesExistingFieldsOnEmptyInput` — Enter = stara wartość zachowana
- `SchemaLoader_Scan_SkipsMalformedFile` — malformed JSON → pominięty, reszta załadowana
- `SchemaLoader_Scan_FindsExternalPlugin` — plik schema w folderze → zwrócony w liście
- `ValidationHooks_InapplicableHook_ReturnsOk` — nieznany hook → ok:true (nie blokuje)

## Acceptance Criteria
- [ ] `agent configure email` prowadzi przez 10 pól email bez otwierania config.json
- [ ] Istniejące wartości w config.json pojawiają się jako domyślne w promptach; Enter zachowuje je
- [ ] Błąd imap-ping nie blokuje zapisu — user dostaje `[R]/[C]`, wybór C = config zapisany
- [ ] `*.config-schema.json` wrzucony do folderu pluginów pojawia się w `agent configure` bez rekompilacji
- [ ] `SetupWizard` i `ConfigureCommand` używają wspólnego `ConfigureMerge.Apply`
- [ ] `dotnet publish --self-contained -r osx-arm64` produkuje standalone binary bez .NET SDK
- [ ] `dotnet test tests/AgentPlatform.Core.Tests` — 5 nowych testów zielone

## Open Questions
Brak — wszystkie pytania rozwiązane w sesji forge + spec.

## Kolejność implementacji
1. Napisz `CliConfigureTests.cs` → commit jako TDD checkpoint
2. `PluginConfigSchema.cs` + `SchemaLoader.cs` → testy SchemaLoader zielone
3. `ConfigureMerge.cs` → testy ConfigureMerge zielone
4. `ValidationHooks.cs` (imap-ping + smtp-ping + nieznany hook = ok)
5. `ConfigureCommand.cs` (Spectre.Console wizard, używa powyższych)
6. `Program.cs` — routing `configure` subkomendy
7. `SetupWizard.cs` — refaktoryzacja na `ConfigureMerge.Apply`
8. `email.config-schema.json` w Plugins.Email
9. `.csproj` — standalone publish config
10. `README.md` + `docs/ARCHITECTURE.md`
11. `dotnet test` — wszystkie zielone
12. AC jeden po drugim
