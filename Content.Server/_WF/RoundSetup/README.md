# RoundSetup

Round Setup lets players organize a Crashlanded or Ship crew start together before the round, or launch a new start mid-round. Hosts control public access, private requests, codes, direct invitations and whether survivors may join after launch. Each member chooses their own character through the existing character selector and selects their own permitted job.

The lobby keeps character preparation in a 280-pixel card on the left, with Crew Setup above a large red/green READY button before the round. Mid-round, equally sized Join Round and Join Crew buttons open the standard job picker and crew browsing. The lobby artwork has no full-screen tint or scanline overlay. Crew discovery appears only when requested. Hosts can give their crew a public name and switch between Ship and Crashlanded before launch without recreating the crew. Changing type clears the vessel choice and readiness while retaining members, roles and admission settings. Editing a character clears both normal and crew readiness. Chat and round briefing/votes share two always-visible halves of the communication dock. Secondary actions sit in a compact bottom bar. Once a player belongs to a crew, its preparation, ship, crew and access sections replace the browsing navigation across the top. The ship page places selection and departure controls beside a flexible preview without a page scrollbar; ship descriptions and preview controls have tooltips. Selectors and buttons wrap long text, pages scroll at smaller sizes, and confirmation and red Leave Crew buttons stay visible. Confirmation closes setup after the server accepts readiness or a mid-round join, including explicit ship purchase consent.

Open Round Setup from the lobby or the in-round game menu. Ready up before round start; a pregame group launches only when its host is ready. Mid-round hosts launch their waiting group explicitly. A disconnected host keeps ownership; a host can transfer ownership to another member or leave to pass it to the next member. Respawning leaves the previous crew and releases its reserved slot; remaining members retain their crew and an existing ship stays in the world. Round restart clears all lobbies and invitation codes.

The server owns membership and validates every request. Scenario job slots remain available when a helm is disabled or unpowered; ordinary shuttle power rules do not override crew admission. Normal joining remains the default ready/join path without creating a scenario crew; only Ship and Crashlanded can create one. The existing join picker retains both regular station and crew jobs. Crashlanded uses a random civilian vessel and planet, a fixed three-minute preparation period with disabled flight controls, and the existing planetary descent and crash systems. Living occupants see a shared on-screen countdown until atmospheric entry, highlighted for the final thirty seconds. Leaving the ship, dying or entering atmosphere clears the warning. The countdown never increases during client clock corrections. Survivors receive an emergency EVA suit, breath mask and a survival kit with species-appropriate gas, medicine, rations and signalling tools; existing clothing is preserved. Later survivors spawn on a surviving floor in that group's wreck.

Crashlanded requires the existing `wf.planet_networks` server setting and a sector containing registered planets. Its card is disabled when planet networks are off. Closing late admission prevents new members; players already accepted retain their place until the host removes them.

Ship crew lets the host select an unrestricted civilian shipyard design and inspect it with the embedded Wolfgate ship previewer, including pan, zoom and fit controls. Selecting a ship is free; readying up inside Round Setup authorizes its listed price from the host's selected character at launch. The server rechecks price, funds, Ironman restrictions and active ship limits. Failed launches refund the same character; accepted crew and later arrivals pay nothing. Hosts choose random clear sector space or a compatible dock at CC; the central station is identified by its existing sector-service host component. Docking uses the existing collision checks and refuses launch without charging if no berth fits. Changing location clears readiness and purchase consent. The ship appears at the selected location and uses normal deeds, repair data, ownership and crew door access. Join Normally never authorizes a ship purchase.

New lobbies contain only the host, with an independent personal job choice and no advertised crew roles. New openings default to one slot. Hosts can name crew roles, choose an eligible underlying job and set 1�16 slots per role (up to 16 roles). Joining or accepting a request reserves a role; disconnected and spawned members retain their slots. Occupied roles cannot change jobs or be removed. Labels appear only in Round Setup: Contractor/Vagrant is called Drifter there, while existing saved jobs, equipment and custom in-world titles remain unchanged.

Pregame lasts six minutes by default, including the Monolith server preset. For local playtests, the default-off `wf.round_setup.demo_enabled` setting seeds a public Crashlanded lobby when the real player named by `wf.round_setup.demo_host` connects. It has a ready host and three explicit Drifter openings. See `Tools/_WF/RoundSetup/README.md` for the headless host launcher. Normal servers never create demo lobbies.

Entry points: `WFRoundSetupSystem`, `GameTicker.RoundSetup`, `WFCrashScenarioSystem`, `WFShipScenarioSystem`, and the client Round Setup window.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/RoundSetup/GameTicker.RoundSetup.cs`](GameTicker.RoundSetup.cs)
- [`Content.Server/_WF/RoundSetup/ShuttleConsoleSystem.RoundSetup.cs`](ShuttleConsoleSystem.RoundSetup.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioComponent.cs`](WFCrashScenarioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioMachinerySystem.cs`](WFCrashScenarioMachinerySystem.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Countdown.cs`](WFCrashScenarioSystem.Countdown.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.cs`](WFCrashScenarioSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Equipment.cs`](WFCrashScenarioSystem.Equipment.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Flight.cs`](WFCrashScenarioSystem.Flight.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.cs`](WFRoundSetupSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Demo.cs`](WFRoundSetupSystem.Demo.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Roles.cs`](WFRoundSetupSystem.Roles.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Spawning.cs`](WFRoundSetupSystem.Spawning.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioComponent.cs`](WFShipScenarioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.cs`](WFShipScenarioSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Docking.cs`](WFShipScenarioSystem.Docking.cs)

### Shared

- [`Content.Shared/_WF/RoundSetup/WFCrashCountdownClock.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrashCountdownClock.cs)
- [`Content.Shared/_WF/RoundSetup/WFCrashCountdownEvent.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrashCountdownEvent.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupCVars.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupCVars.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupMessages.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupMessages.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupPolicy.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupPolicy.cs)

### Client

- [`Content.Client/_WF/RoundSetup/LobbyCharacterPreviewPanel.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyCharacterPreviewPanel.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/LobbyGui.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyGui.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/LobbyState.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyState.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/PickerWindow.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/PickerWindow.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/WFCrashCountdownHud.cs`](../../../Content.Client/_WF/RoundSetup/WFCrashCountdownHud.cs)
- [`Content.Client/_WF/RoundSetup/WFCrashCountdownHudSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFCrashCountdownHudSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFLobbyLayout.cs`](../../../Content.Client/_WF/RoundSetup/WFLobbyLayout.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupClientSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupClientSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupLobbyPanel.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupLobbyPanel.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupOptionButton.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupOptionButton.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupUIController.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupUIController.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Layout.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Layout.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Preview.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Preview.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Roles.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Roles.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ScenarioColumns.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ScenarioColumns.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Ships.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Ships.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashCountdownTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashCountdownTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEquipmentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEquipmentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupDemoTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupDemoTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.cs)

### Unit tests

- [`Content.Tests/_WF/RoundSetup/WFCrashCountdownClockTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFCrashCountdownClockTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFRoundSetupPolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFRoundSetupPolicyTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFRoundSetupRolePolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFRoundSetupRolePolicyTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/RoundSetup/crash.yml`](../../../Resources/Prototypes/_WF/RoundSetup/crash.yml)
- [`Resources/Prototypes/_WF/RoundSetup/ship.yml`](../../../Resources/Prototypes/_WF/RoundSetup/ship.yml)

### Localization

- [`Resources/Locale/en-US/_WF/RoundSetup/countdown-hud.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/countdown-hud.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/crash.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/crash.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/flow.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/flow.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/home.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/home.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/lobby-panel.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/lobby-panel.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/preview.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/preview.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/round-setup.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/round-setup.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/server.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/server.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/ship.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/ship.ftl)

### Tools

- [`Tools/_WF/RoundSetup/README.md`](../../../Tools/_WF/RoundSetup/README.md)
- [`Tools/_WF/RoundSetup/start-test-host.ps1`](../../../Tools/_WF/RoundSetup/start-test-host.ps1)

## Non-modular edits

- [`Content.Client/Lobby/LobbyState.cs`](../../../Content.Client/Lobby/LobbyState.cs)
  - the normal jobs picker is managed by the Round Setup lobby partial
  - the preparation header supplies its own lobby context
  - keep room for the crew browser at 720p
  - the responsive layout owns the dock size
  - the primary action handles readiness and crew membership together
  - the primary action owns readiness instead of a toggle subscription
  - release live crew updates and the normal job picker
  - the lobby directly handles normal joining or opens the player's crew
  - keep crew management available while normal late join is disabled
  - synchronize direct joining and crew membership with lobby readiness
- [`Content.Client/Lobby/UI/LobbyGui.xaml`](../../../Content.Client/Lobby/UI/LobbyGui.xaml)
  - roundSetup namespace mounts the live player lobby browser
  - a unified preparation screen with character, crew and communication areas
- [`Content.Client/Lobby/UI/LobbyGui.xaml.cs`](../../../Content.Client/Lobby/UI/LobbyGui.xaml.cs)
  - unify character and crew preparation controls
  - expose group setup before the round starts
  - character editing clears crew readiness too
  - Join no longer toggles, but character edits still clear pregame readiness
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): hosts can manage admission and survivors after spawning
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): manage the player lobby during the round
- [`Content.Server/GameTicking/GameTicker.Spawning.cs`](../../GameTicking/GameTicker.Spawning.cs)
  - private scenario stations are never ordinary job destinations
  - explicit Normal choices share the regular job allocation
  - authorize the assigned group member while preserving the saved profile
  - require authenticated scenario membership on every spawn path
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - failed scenario helms reject pilots until the crash is complete.
  - scenario crew admission is independent of helm power
- [`Content.Server/Station/Systems/StationJobsSystem.cs`](../../Station/Systems/StationJobsSystem.cs): scenario admission is controlled by its host
- [`Content.Shared/CCVar/CCVars.Game.cs`](../../../Content.Shared/CCVar/CCVars.Game.cs): give players six minutes to configure and join scenario lobbies.
- [`Resources/ConfigPresets/_Mono/monolithCore.toml`](../../../Resources/ConfigPresets/_Mono/monolithCore.toml): keep the Monolith preset's pregame setup window at six minutes.

<!-- WOLFGATE-GENERATED END -->
