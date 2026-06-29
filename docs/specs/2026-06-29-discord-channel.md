# SPEC: Kanał Discord (DM 1:1)
Status: Draft
Tryb: Plugin Only
Data: 2026-06-29
Zależność: wymaga [2026-06-29-notify-channel-routing-generic.md](2026-06-29-notify-channel-routing-generic.md) (Slice 1) — bez niego push na Discord nie zadziała.

## Co i dlaczego
Użytkownik może rozmawiać z domowym asystentem przez Discord (DM 1:1, jak Telegram) i dostawać tam powiadomienia.

## Przykład użycia
Użytkownik (DM do bota): "jakie mam dziś zadania?"
System (DM): "Masz 2: odebrać paczkę, zadzwonić do hydraulika."

## Edge Cases
- Nieznany użytkownik DM-uje bota (Discord-konto niepowiązane z domownikiem) → flow linkowania jak Telegram: bot prosi o `/connect-discord` w webczacie i wpisanie `/start <kod>` w DM (kod z `DiscordLinkStore`, in-memory). Wzór: TelegramPoller.cs:74-89.
- Załącznik/obraz w DM → MVP text-only: obsłuż sam tekst, media zignoruj (bez błędu).
- (z Check D) push: kanał wybierany przez generyczny routing ze Slice 1 — Discord musi być w `Notifications:ChannelPriority`.

## Scope
IN:  inbound DM 1:1 (Gateway/WebSocket przez Discord.Net) → publish RawEvent("discord", …) na bus; outbound DeliverAsync (odpowiedzi + powiadomienia); linkowanie tożsamości; `/connect-discord`.
OUT: kanały serwera (guild), wzmianki @bot, wątki, reakcje; natywne slash-Interactions Discorda; media/embeds/głos; ekspozycja `discordLinkable` w /api/me (opcjonalnie później).

## Pliki do UTWORZENIA
- src/AgentPlatform.Plugins.Discord/AgentPlatform.Plugins.Discord.csproj   # ref: SDK + Infrastructure (IUserRepository, IMessageBus); PackageReference Discord.Net
- src/AgentPlatform.Plugins.Discord/GlobalUsings.cs                        # alias ExecutionContext
- src/AgentPlatform.Plugins.Discord/DiscordOptions.cs                      # BotToken (gating jak Telegram: brak tokenu → kanał nieaktywny)
- src/AgentPlatform.Plugins.Discord/DiscordChannelPlugin.cs               # IChannelPlugin: ChannelId "discord"; ParseAsync(RawEvent)->InputMessage; DeliverAsync (user.CreateDMChannelAsync().SendMessageAsync; >2000 zn. tnij); Capabilities. Wzór: TelegramChannelPlugin.cs
- src/AgentPlatform.Plugins.Discord/DiscordGateway.cs                     # BackgroundService: DiscordSocketClient (GatewayIntents.DirectMessages | MessageContent | Guilds); on MessageReceived w DM: jeśli "/start <kod>" -> SetChannelIdentityAsync(userId,"discord", authorId); inaczej bus.PublishAsync(RawEvent("discord", payload)). Wzór: TelegramPoller.cs (TelegramConnector + TelegramUpdateProcessor)
- src/AgentPlatform.Plugins.Discord/DiscordLink.cs                        # DiscordLinkStore (kody, in-memory, jak TelegramLinkStore) + ConnectDiscordCommand : ISlashCommand "connect-discord"
- src/AgentPlatform.Plugins.Discord/DI/DiscordPluginExtensions.cs         # AddDiscordPlugin: Configure<DiscordOptions>; AddSingleton<IChannelPlugin,DiscordChannelPlugin>; AddSingleton<DiscordLinkStore>; AddHostedService<DiscordGateway>; AddScoped<ISlashCommand,ConnectDiscordCommand>

## Pliki do MODYFIKACJI
- src/AgentPlatform.Api/Program.cs                          # builder.Services.AddDiscordPlugin(builder.Configuration); (obok AddTelegramPlugin, ~:47)
- src/AgentPlatform.Api/AgentPlatform.Api.csproj + AgentPlatform.slnx   # ProjectReference + wpis projektu
- ~/.agentplatform/config.json (POZA repo)                 # Plugins:Discord:BotToken; dopisać "discord" do Notifications:ChannelPriority

## Pliki KTÓRYCH NIE DOTYKAMY
- src/AgentPlatform.Core/Pipeline/*           # kanał nie dotyka plannera; inbound: RawEvent->bus->MessageBusConsumer; outbound: OutputRouter.cs:11 registry.GetChannel(message.ChannelId) — generyczne
- sdk/AgentPlatform.PluginSdk/Contracts/*     # IChannelPlugin (Interfaces.cs:7-13) wystarcza
- src/AgentPlatform.Infrastructure/Notifications/NotificationService.cs   # już generyczny po Slice 1
- PluginNamespaces (Program.cs:65)            # MVP nie dodaje żadnego tool/intent z prefiksem "discord." → brak wpisu

## Tests First
Plik: tests/AgentPlatform.Core.Tests/DiscordChannelTests.cs
Klasa: DiscordChannelTests   (xUnit, [Fact] — czysta logika, bez sieci; ew. fake DiscordSocketClient/wydzielony parser)
Przypadki:
- ParseAsync_DM_mapuje_na_InputMessage_z_ChannelId_discord   (happy path)
- StartCommand_z_kodem_linkuje_tozsamosc                      (nieznany user — wywołuje SetChannelIdentityAsync)
- Zalacznik_bez_tekstu_jest_ignorowany                        (text-only)
(Żywy Gateway wymaga BotTokenu → ścieżka integracyjna/manualna, nie unit.)

## Acceptance Criteria
- [ ] DM do bota od powiązanego użytkownika → odpowiedź asystenta wraca w DM (pełny obieg przez pipeline).
- [ ] Nieznany użytkownik prowadzi flow `/connect-discord` + `/start <kod>` → `channel_identities` ma wpis ("discord", authorId).
- [ ] Powiadomienie (np. reminder) trafia na Discord, gdy user ma połączony Discord i jest on w `Notifications:ChannelPriority`.
- [ ] Brak `Plugins:Discord:BotToken` → kanał nieaktywny, reszta systemu działa (gating jak Telegram).

## Open Questions
Brak (biblioteka: Discord.Net; refaktor routingu: Slice 1).

## Kolejność implementacji
1. Napisz DiscordChannelTests (czerwone) → commit checkpoint.
2. DiscordOptions + DiscordChannelPlugin (ParseAsync/DeliverAsync) → testy parsowania zielone.
3. DiscordLink (store + /connect-discord) + DiscordGateway (DM → bus / linkowanie).
4. AddDiscordPlugin + rejestracja w Program.cs + ProjectReference/slnx.
5. `dotnet build` + `dotnet test` zielone; weryfikacja żywa z BotTokenem (DM tam i z powrotem, push).
6. Sprawdź AC po kolei.
