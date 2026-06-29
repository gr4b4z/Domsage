# SPEC: Generyczny routing pushu w NotificationService
Status: Draft
Tryb: Core Change (shared infra — zmienia routing dla telegram/signal/email)
Data: 2026-06-29

## Co i dlaczego
Wybór kanału do wypychania powiadomień jest zaszyty (`if telegram else if signal`) — każdy nowy kanał
(Discord, …) wymaga edycji tego pliku. Zamieniamy go na **generyczną pętlę po priorytecie**, by kolejne
kanały były zero-touch. Prereq dla slice'a Discord ([2026-06-29-discord-channel.md](2026-06-29-discord-channel.md)).

## Przykład użycia
Niewidoczne dla użytkownika — to refaktor behawioralnie tożsamy. Po nim: user z połączonym Discordem
dostaje przypomnienia na Discord bez żadnej zmiany w `NotificationService`.

## Edge Cases
- User ma telegram I signal → nadal **telegram** (priorytet zachowany).
- User ma tylko email → email-fallback bez zmian (mode=="email" lub `!live && brak messaging`).
- Kanał z tożsamości nie jest zarejestrowany (`registry.GetChannel` rzuca) → pomiń, próbuj następny (dziś: `catch { return; }` w `Deliver`, :74-76 — zachować ten sam brak-crasha).

## Scope
IN:  ekstrakcja selekcji kanału do czystej funkcji + iteracja po liście priorytetu; Discord wpada „za darmo".
OUT: zmiana semantyki „dostarcz do JEDNEGO kanału" (nadal pierwszy dostępny, nie broadcast); zmiana email-fallbacku; UI/`NotifyMode`.

## Impact na Core
Problem: `NotificationService.PushAsync` (`src/AgentPlatform.Infrastructure/Notifications/NotificationService.cs:46-56`)
hardkoduje `tg`/`sig` i łańcuch `if/else`. To shared infra — używane przez reminders, automations i wszystkie powiadomienia.

## ADR
Opcja A: lista priorytetu w kodzie `["telegram","signal","discord"]`, iteruj, dostarcz do pierwszego obecnego w `chans`.
Opcja B: priorytet z configu `Notifications:ChannelPriority` (default jw.), reszta messaging-kanałów po nim.
Decyzja: **B** — config z sensownym defaultem; nowy kanał działa bez edycji kodu, a kolejność da się dostroić bez deployu. `email`/`http` wykluczone z messaging (email ma własny fallback).
Konsekwencje dla istniejących pluginów: telegram/signal — kolejność musi zostać telegram>signal (default listy); email — fallback nietknięty. Żaden plugin nie zmienia kontraktu.

## Migracja istniejących pluginów
Brak zmian w pluginach (kontrakty bez zmian). Tylko `NotificationService` + nowa opcja configu z defaultem `["telegram","signal","discord"]`.

## Pliki do UTWORZENIA
- src/AgentPlatform.Infrastructure/Notifications/ChannelRouting.cs  # czysta funkcja: SelectPushChannel(chans, priority) -> (channelId, externalId)? ; + NotificationOptions { string[] ChannelPriority }

## Pliki do MODYFIKACJI
- src/AgentPlatform.Infrastructure/Notifications/NotificationService.cs:46-56  # użyj SelectPushChannel zamiast tg/sig + if/else; hasMessaging = wynik != null
- src/AgentPlatform.Infrastructure/DI/InfrastructureServiceExtensions.cs        # services.Configure<NotificationOptions>(config.GetSection("Notifications"))

## Pliki KTÓRYCH NIE DOTYKAMY
- src/AgentPlatform.Core/Pipeline/*, sdk/AgentPlatform.PluginSdk/Contracts/*    # to czysto infra
- src/AgentPlatform.Infrastructure/Notifications/NotificationService.cs:58-78    # email-fallback + Deliver() bez zmian

## Tests First
Plik: tests/AgentPlatform.Core.Tests/NotificationRoutingTests.cs
Klasa: NotificationRoutingTests   (xUnit, [Theory] — czysta funkcja, bez DB)
Przypadki:
- Telegram_i_Signal_wybiera_Telegram  (priorytet zachowany)
- Tylko_Signal_wybiera_Signal
- Brak_messaging_zwraca_null  (→ ścieżka email-fallback)
- Nowy_kanal_w_priority_jest_wybierany  (np. discord, gdy brak telegram/signal)

## Acceptance Criteria
- [ ] `SelectPushChannel` jest czysta (bez DB/IO) i pokryta testami z [Theory].
- [ ] Zachowane: telegram>signal, email-fallback identyczny jak przed refaktorem.
- [ ] Dodanie nowego channelId do `Notifications:ChannelPriority` kieruje push tam — bez edycji `NotificationService`.
- [ ] `dotnet test` zielony (istniejące testy powiadomień nie regresują).

## Open Questions
Brak.

## Kolejność implementacji
1. Napisz NotificationRoutingTests (na razie czerwone) → commit checkpoint.
2. Utwórz ChannelRouting.cs (SelectPushChannel + NotificationOptions).
3. Podłącz w NotificationService + zarejestruj NotificationOptions.
4. Wszystkie testy zielone; sprawdź AC po kolei.
