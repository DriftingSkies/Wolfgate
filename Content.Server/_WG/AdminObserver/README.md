# AdminObserver

The admin observer (`aghost`) as a working tool: a key to enter and leave it, a saved kit, a visible "manifested"
form that is always marked as staff, and one panel for all of it. The module is built in phases; this page describes
what has landed. Everything here is gated on the `Admin` flag, the same as `aghost`; the `EVENT` flag alone gets
nothing.

The admin ghost key (Shift+F5 by default, rebindable under Options > Controls) runs `aghost`: from a body it visits a
new observer, and from that observer it goes back to the same body. An observer with no body to go back to gets a
popup instead, since `aghost` would only replace it with a new observer. The manifest and observer panel keys are
listed beside it and ship unbound.

Each admin has one saved document (`WGAdminObserverData`: avatar presets, staff marker choices, kit layout), a JSON
row in `wolfgate_admin_observer`. `WGAdminObserverStore` loads it when the admin joins or gains the flag, sends it to
their client, and writes it two seconds after the last change and when they leave or lose the flag. One admin's
writes run in order, a failed write is kept and tried again, and a failed read leaves the document unloaded rather
than starting a new one over it. A document is capped at 64 KB, and
anything in it that names a prototype which no longer exists is dropped on load.

## Kit

The observer no longer takes `NFMobAghostGear` from a `Loadout`. When an admin enters a new observer,
`WGAdminObserverKitSystem` dresses it from their saved layout, or from `WGAdminObserverGear` (the old 14 items plus
the admin tools that were missing) when they have none. The layout is what sits in each slot and hand, and where
each item sits in a bag's grid, three levels deep. It is saved when the admin leaves the observer, on every Give and
Put away, and on Save layout. An item that finds no room on restore is listed in the Kit window, not dropped.

Items are saved by prototype and position only: a renamed or refilled item comes back fresh, and a container comes
back with its own starting contents plus what was saved in it. Only kit catalogue items are saved, so something
picked up in a round is not carried into the next; an admin who also holds the `Spawn` flag has everything saved.
A player put in an observer without the `Admin` flag (`aghost <player>`) gets the default kit and saves nothing.

The catalogue is the `wgAdminKitCategory` prototypes in `Resources/Prototypes/_WG/AdminObserver/kit.yml`. `adminkit`
opens the Kit window (catalogue by group, search, Give, Put away, Save layout); `adminkit give <item>` hands over a
catalogue item, to the active hand, else a pocket, else the bag; `adminkit putaway` deletes the held kit item. Give
takes catalogue IDs only, so the `Admin` flag does not become a general spawn right.

## Manifesting

`manifest [preset]`, the Manifest action on the observer and the manifest key show the observer to living players;
`unmanifest` or the same action or key hides it. `WGManifestSystem` moves it from the ghost visibility layer to the
normal one, gives it an in-character name and description (the preset's, else the avatar prototype's, else the
admin's character, else "admin observer"; never the account name) and puts the old ones back on hiding. It stays a
ghost: nothing can hit, pull, cuff or strip it, and it keeps every observer power. Each change is admin-logged. A
new observer always starts hidden. A client that has ghosts switched off, as one does after leaving a ghost for a
body, still draws a manifested observer.

While manifested, Say, Whisper, Emote, Subtle and radio are in character: `ChatSystem` no longer forwards its
speech to dead chat, it gets an intrinsic transmitter on every radio channel that needs no telecom server, and it
counts as a subtle listener. Dead chat still works when that channel is picked (the client sends it by `dsay`).
Hidden, everything is as before.

A manifested observer always carries `WGStaffMarkerComponent`, and nothing can remove the marker's fixed part
(`WGStaffMarkerSystem`): a line on examine and a tag in front of its name in chat and on the radio. The wording is
the admin's choice (rank by default, "Staff", account name, or rank and name). At least one visual is always on.
The marker's colour is fixed for staff and the admin's own only for a holder of the `Host` flag; the server decides
both from the admin's flags.

The visuals, any mix of them, drawn on every viewer's client from the networked marker:

- Nameplate: the wording over the avatar (`WGStaffNameplateOverlay`).
- Sigil: a status icon beside the head, by rank and never chosen: the `Admin` job icon, or the `MindShield` frame
  for a holder of the `Host` flag.
- Ground ring, shield and glyph: drawn by `WGStaffMarkerOverlay` under and over the avatar, unlit, upright and in
  the marker's colour. The glyph circles the avatar and passes behind it.
- Hologram and aura outline: passes over the avatar's sprite (`SpriteSystem.SetPostShader`), put back each frame
  if something clears them. The aura can instead be a light, an entity each client makes for itself.

Each look of the ring, glyph, shield and aura is a `wgStaffMarkerVariant` prototype in
`Resources/Prototypes/_WG/AdminObserver/marker.yml`. A look points at a sprite where it already is; nothing is
copied. `staff_tint.swsl` draws a sprite as its brightness alone so the marker's colour becomes its colour,
whatever hue the art has. A look that no longer exists falls back to its visual's default.

## Avatars

A preset has a kind. Ghost is the observer's own sprite.

Object and Mob show an entity prototype: the server takes any that exists and can be spawned, the client refuses
one with no sprite (`WGAvatarVisualsSystem.CanShow`; the server reads no sprites). The client copies the
prototype's default sprite onto the observer and puts the observer's own back when it hides. It is the look as
written in the prototype and does not animate with state. An object, mob or character with only a plate or sigil
beside it could pass for the real thing, so one whose marker has nothing drawn on the avatar itself also gets the
hologram and the ring. `WGAvatarPickerWindow` picks the prototype: the spawn menu, with picking in place of placing.

Character shows one of the admin's saved characters. `WGAvatarBodySystem` builds a real humanoid from the profile,
so markings, colours, species and clothing are drawn by the code that draws anyone's, and each client copies its
sprite onto the observer twice a second. It wears the preset's saved layout, else its starting gear, else the
loadout of the job the character most wants. The observer takes the character's name and speech sounds. The body
has no mind, rides in a container on the observer, is deleted with it and is kept after hiding, so it can be
dressed while hidden: `OpenDresser` opens its strip window and `SaveOutfit` writes what it wears into the preset,
under the kit's save rules. The outfit menu's `Pick` mode picks a starting gear. The body is rebuilt when its
preset or character changes, and an observer shown as the old body hides first.

The body must never count as a person:

- It is kept paused, the engine's flag for what is not simulated, and paused again after a map change wakes it.
  Godmode is on as well but is not enough here: Wolfmed puts a body in vacuum under without dealing damage.
- It is built from the profile without Monolith's persistent components and items, which belong to the character
  played for real.
- Its suit sensors are switched off, when it is built and whenever it is dressed.
- Every body is deleted when the round ends, before `PersistAtRoundEndSystem` saves the persistent items it finds
  under each player's entity.

A new admin's document starts with two presets: Ghost, and My character (their selected character).

## Observer panel

`observerpanel`, the Observer panel action on the observer, the panel key (unbound by default) and a button on
the Wolfgate admin tab open one window with four tabs:

- Presence: the manifest toggle; the avatar presets, with New, Edit, Make default and Delete; the editor of the
  selected preset (kind, the name and description players see, the prototype, character or outfit, Dress avatar
  and Save outfit, how it arrives); and the staff marker (wording, each visual and its look, colour).
- Kit: the Kit window's contents.
- Tools: the mass scanner, the orbit menu and the observer's other built-in consoles.
- People: Spawn as Outfit at the admin's position, and the ERT builder.

Every choice is sent as it is made and `WGObserverPanelSystem` writes it to the saved document at once; there is
no Save button. A marker change shows at once on an admin who is marked. A preset is known by the name players
will see where it has one and that name is free; a new one is numbered until then. Ring and glyph looks are
picked by their pictures, shield and aura looks by name. Only a holder of the `Host` flag gets the colour field.

Not in this module: Offer to Ghosts and Second Chance stay right-click verbs, since each acts on a body; what the
observer holds is not drawn on an avatar; MODsuits on avatars.

Entry points: `WGAdminObserverKeys` (the key functions), `WGAdminObserverHotkeySystem` (client),
`WGManifestSystem` (`TryManifest`, `TryUnmanifest`), `WGStaffMarkerSystem` (`Mark`), `ManifestCommand`,
`WGStaffMarkerVisualsSystem` and `WGAvatarVisualsSystem` (client), `WGAvatarBodySystem` (`Ensure`, `OpenDresser`,
`SaveOutfit`), `WGObserverPanelSystem` (`SetMarker`, `SavePreset`, `DeletePreset`, `SetDefault`, `OpenTool`),
`WGObserverPanelWindow` and `WGObserverPresenceTab` (client),
`WGAdminObserverStore` (`Get`, `Change`), `WGAdminObserverSerializer` (storage format and clean-up),
`WGAdminObserverKitSystem` (`Snapshot`, `Restore`, `Give`, `PutAway`), `WGAdminKitCatalogueSystem`, `AdminKitCommand`
and `WGAdminKitControl` (client).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/AdminObserver/Commands/AdminKitCommand.cs`](Commands/AdminKitCommand.cs)
- [`Content.Server/_WG/AdminObserver/Commands/ManifestCommands.cs`](Commands/ManifestCommands.cs)
- [`Content.Server/_WG/AdminObserver/Commands/ObserverPanelCommand.cs`](Commands/ObserverPanelCommand.cs)
- [`Content.Server/_WG/AdminObserver/ServerDbManager.AdminObserver.cs`](ServerDbManager.AdminObserver.cs)
- [`Content.Server/_WG/AdminObserver/WGAdminObserverKitSystem.cs`](WGAdminObserverKitSystem.cs)
- [`Content.Server/_WG/AdminObserver/WGAdminObserverSerializer.cs`](WGAdminObserverSerializer.cs)
- [`Content.Server/_WG/AdminObserver/WGAdminObserverStore.cs`](WGAdminObserverStore.cs)
- [`Content.Server/_WG/AdminObserver/WGAvatarBodySystem.cs`](WGAvatarBodySystem.cs)
- [`Content.Server/_WG/AdminObserver/WGManifestSystem.cs`](WGManifestSystem.cs)
- [`Content.Server/_WG/AdminObserver/WGObserverPanelSystem.cs`](WGObserverPanelSystem.cs)
- [`Content.Server/_WG/AdminObserver/WGStaffMarkerSystem.cs`](WGStaffMarkerSystem.cs)

### Shared

- [`Content.Shared/_WG/AdminObserver/WGAdminKitCatalogueSystem.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminKitCatalogueSystem.cs)
- [`Content.Shared/_WG/AdminObserver/WGAdminKitCategoryPrototype.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminKitCategoryPrototype.cs)
- [`Content.Shared/_WG/AdminObserver/WGAdminObserverComponent.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminObserverComponent.cs)
- [`Content.Shared/_WG/AdminObserver/WGAdminObserverData.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminObserverData.cs)
- [`Content.Shared/_WG/AdminObserver/WGAdminObserverEvents.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminObserverEvents.cs)
- [`Content.Shared/_WG/AdminObserver/WGAdminObserverKeys.cs`](../../../Content.Shared/_WG/AdminObserver/WGAdminObserverKeys.cs)
- [`Content.Shared/_WG/AdminObserver/WGAvatarBodyComponent.cs`](../../../Content.Shared/_WG/AdminObserver/WGAvatarBodyComponent.cs)
- [`Content.Shared/_WG/AdminObserver/WGManifestedComponent.cs`](../../../Content.Shared/_WG/AdminObserver/WGManifestedComponent.cs)
- [`Content.Shared/_WG/AdminObserver/WGObserverPanelEvents.cs`](../../../Content.Shared/_WG/AdminObserver/WGObserverPanelEvents.cs)
- [`Content.Shared/_WG/AdminObserver/WGStaffMarkerComponent.cs`](../../../Content.Shared/_WG/AdminObserver/WGStaffMarkerComponent.cs)
- [`Content.Shared/_WG/AdminObserver/WGStaffMarkerVariantPrototype.cs`](../../../Content.Shared/_WG/AdminObserver/WGStaffMarkerVariantPrototype.cs)

### Client

- [`Content.Client/_WG/AdminObserver/ChatUIController.AdminObserver.cs`](../../../Content.Client/_WG/AdminObserver/ChatUIController.AdminObserver.cs)
- [`Content.Client/_WG/AdminObserver/GhostSystem.AdminObserver.cs`](../../../Content.Client/_WG/AdminObserver/GhostSystem.AdminObserver.cs)
- [`Content.Client/_WG/AdminObserver/UI/WGAdminKitControl.xaml`](../../../Content.Client/_WG/AdminObserver/UI/WGAdminKitControl.xaml)
- [`Content.Client/_WG/AdminObserver/UI/WGAdminKitControl.xaml.cs`](../../../Content.Client/_WG/AdminObserver/UI/WGAdminKitControl.xaml.cs)
- [`Content.Client/_WG/AdminObserver/UI/WGAdminKitWindow.xaml`](../../../Content.Client/_WG/AdminObserver/UI/WGAdminKitWindow.xaml)
- [`Content.Client/_WG/AdminObserver/UI/WGAdminKitWindow.xaml.cs`](../../../Content.Client/_WG/AdminObserver/UI/WGAdminKitWindow.xaml.cs)
- [`Content.Client/_WG/AdminObserver/UI/WGAvatarPickerWindow.xaml`](../../../Content.Client/_WG/AdminObserver/UI/WGAvatarPickerWindow.xaml)
- [`Content.Client/_WG/AdminObserver/UI/WGAvatarPickerWindow.xaml.cs`](../../../Content.Client/_WG/AdminObserver/UI/WGAvatarPickerWindow.xaml.cs)
- [`Content.Client/_WG/AdminObserver/UI/WGObserverPanelWindow.cs`](../../../Content.Client/_WG/AdminObserver/UI/WGObserverPanelWindow.cs)
- [`Content.Client/_WG/AdminObserver/UI/WGObserverPresenceTab.cs`](../../../Content.Client/_WG/AdminObserver/UI/WGObserverPresenceTab.cs)
- [`Content.Client/_WG/AdminObserver/WGAdminKitClientSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGAdminKitClientSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGAdminObserverClientSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGAdminObserverClientSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGAdminObserverHotkeySystem.cs`](../../../Content.Client/_WG/AdminObserver/WGAdminObserverHotkeySystem.cs)
- [`Content.Client/_WG/AdminObserver/WGAvatarVisualsComponent.cs`](../../../Content.Client/_WG/AdminObserver/WGAvatarVisualsComponent.cs)
- [`Content.Client/_WG/AdminObserver/WGAvatarVisualsSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGAvatarVisualsSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGManifestClientSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGManifestClientSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGObserverPanelClientSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGObserverPanelClientSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGStaffMarkerOverlay.cs`](../../../Content.Client/_WG/AdminObserver/WGStaffMarkerOverlay.cs)
- [`Content.Client/_WG/AdminObserver/WGStaffMarkerVisualsComponent.cs`](../../../Content.Client/_WG/AdminObserver/WGStaffMarkerVisualsComponent.cs)
- [`Content.Client/_WG/AdminObserver/WGStaffMarkerVisualsSystem.cs`](../../../Content.Client/_WG/AdminObserver/WGStaffMarkerVisualsSystem.cs)
- [`Content.Client/_WG/AdminObserver/WGStaffNameplateOverlay.cs`](../../../Content.Client/_WG/AdminObserver/WGStaffNameplateOverlay.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitCatalogueTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitCatalogueTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitFixture.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitFixture.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitRoundTripTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitRoundTripTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitRulesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AdminKitRulesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AdminObserverStoreTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AdminObserverStoreTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarBodyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarBodyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarOutfitTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarOutfitTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarPrototypeRulesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/AvatarPrototypeRulesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestPermissionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestPermissionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestSpeechTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestSpeechTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestVisibilityTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/ManifestVisibilityTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/ObserverPanelRequestsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/ObserverPanelRequestsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/StaffMarkerFloorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/StaffMarkerFloorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdminObserver/StaffMarkerVisualsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdminObserver/StaffMarkerVisualsTest.cs)

### Unit tests

- [`Content.Tests/_WG/AdminObserver/WGAdminObserverDataTest.cs`](../../../Content.Tests/_WG/AdminObserver/WGAdminObserverDataTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/AdminObserver/actions.yml`](../../../Resources/Prototypes/_WG/AdminObserver/actions.yml)
- [`Resources/Prototypes/_WG/AdminObserver/kit.yml`](../../../Resources/Prototypes/_WG/AdminObserver/kit.yml)
- [`Resources/Prototypes/_WG/AdminObserver/marker.yml`](../../../Resources/Prototypes/_WG/AdminObserver/marker.yml)

### Localization

- [`Resources/Locale/en-US/_WG/AdminObserver/avatars.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/avatars.ftl)
- [`Resources/Locale/en-US/_WG/AdminObserver/hotkeys.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/hotkeys.ftl)
- [`Resources/Locale/en-US/_WG/AdminObserver/kit.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/kit.ftl)
- [`Resources/Locale/en-US/_WG/AdminObserver/manifest.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/manifest.ftl)
- [`Resources/Locale/en-US/_WG/AdminObserver/marker.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/marker.ftl)
- [`Resources/Locale/en-US/_WG/AdminObserver/panel.ftl`](../../../Resources/Locale/en-US/_WG/AdminObserver/panel.ftl)

### Textures

- [`Resources/Textures/_WG/AdminObserver/Shaders/staff_tint.swsl`](../../../Resources/Textures/_WG/AdminObserver/Shaders/staff_tint.swsl)

## Non-modular edits

- [`Content.Client/Chat/Managers/ChatManager.cs`](../../../Content.Client/Chat/Managers/ChatManager.cs): a manifested admin observer's say is in character, so its dead chat goes by dsay
- [`Content.Client/Input/ContentContexts.cs`](../../../Content.Client/Input/ContentContexts.cs)
  - the manifest key works in the admin observer
  - the observer panel key works in the admin observer
  - the admin ghost key works from any body
- [`Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs`](../../../Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs): admin ghost, manifest and observer panel keys
- [`Content.Client/UserInterface/Systems/Chat/ChatUIController.cs`](../../../Content.Client/UserInterface/Systems/Chat/ChatUIController.cs)
  - a manifested admin observer speaks in character
  - Local stays Local for a manifested admin observer
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - admins' observer settings
  - one observer document per admin
  - an admin's saved observer presets, staff marker and kit
- [`Content.Server/Chat/Systems/ChatSystem.cs`](../../Chat/Systems/ChatSystem.cs): a manifested admin observer speaks in character
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml): admin ghost and back to body, SS13's aghost key
- [`Resources/Prototypes/Entities/Mobs/Player/admin_ghost.yml`](../../../Resources/Prototypes/Entities/Mobs/Player/admin_ghost.yml)
  - the kit comes from the admin's saved layout, or WGAdminObserverGear
  - a manifested observer says, whispers and emotes as the living do
  - the staff marker's sigil is drawn as a status icon

<!-- WOLFGATE-GENERATED END -->
