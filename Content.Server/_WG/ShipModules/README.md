# ShipModules

Ship modules are abilities fitted into a ship's three module slots that its pilot fires from the helm. Drydocks
fit them as a paid crane job and empty slots for free (Drydock module, `WGDrydockSystem.Modules.cs`); admins fit
or empty any ship's slots with `shipmodule <grid | here> <slot 1-3> <module | none>`. A module (`wgShipModule`,
`WGShipModulePrototype`) holds charges, refills one at a time while a helm on the ship has power, and can be
locked by the ship's state. The first one is the MJ-3 Sidestep maneuvering jets (`WGShipModuleManeuveringJets`):
each burn kicks the hull about 20 m/s along the direction the pilot is holding, so a ship flying with its shield
generators off can step out of a salvo.

Pilots see a module strip under FUEL and CARGO, on the windowed helm's navigation page and in the cockpit
(`WGShipModuleBank`), with one key per fitted module: its charges and whether it is ready, charging or locked. A module is
fired by pressing its key or its slot's hotkey while piloting (`WGShipModuleKeys`, Z, X and C by default for slots
1 to 3, rebindable under Shuttle in the controls). The strip is hidden on a ship with no modules.

The fitted modules live on the grid in `WGShipModuleBayComponent`, saved with the ship; charges and timers aren't
saved, so a loaded ship starts full. Without a powered helm (`ShuttleConsole`) aboard, recharging pauses. Every 0.25 s
`WGShipModuleSystem` writes the strip to the networked `WGShipModulesComponent` on the grid, dirtied only when
something shown changes. A use is refused, with a popup at the pilot's cursor, while the pilot's helm has no power,
the ship is in a jump, or (for a module with `blockedByShields`) a shield generator aboard is on or was switched off
less than `shieldLockout` ago. A generator counts as on when it is anchored and switched on, at the machine and at
the ship's field switch, whether or not it has power or a field up, so a field down to recharge or after an overload
still locks the module. Any field on the hull from another source counts too. A deflector (an emitter with
`WGShipShieldDeflector`, such as the MD-10 Parasol) doesn't. Such a module is also refused while the ship vents
(`WGShipVentingComponent`) or its generator is overloaded (`WGShipOverloadedComponent`), so lowering the field by hand
is no way out of either.

Each use plays `activateSound` to everyone aboard and to the pilot, and sends them `WGShipModuleFiredEvent`. On their
clients `WGShipModuleHelmSystem` lights the module's key red for a moment on every strip showing the ship, shakes the
helm window or cockpit holding that strip by a few pixels, and kicks the view of crew aboard. `jolt` scales both shakes
(0 for none), and the player's screen shake setting scales them again.

## Modules

| Module | Mode | Unlock | What it does |
|---|---|---|---|
| MJ-3 Sidestep jets | fired, 3 charges | free | Kick along the held direction; shields off only. |
| PS-2 Wolf phase skimmer | fired, 3 charges | unlisted | Drops the hull up to 160 m along its course, short of other grids; breaks missile locks. |
| GX-4 gun gyros | passive | unlisted | Rotating guns turn 1.5 times as fast; wide swings whir and ring. |
| FS-9 Bastille fortress shield | toggle | unlisted | Absorbed hits heat a quarter as much; guns silent; hard heat every second; field yellow in the world and on radar. |
| AF-2 ammo feeder | fired, 6 s, 1 charge | unlisted | Guns Mono types `Ballistic` (or, untyped, fed rounds that aren't missiles) fire twice as fast, bursts and the pause after them included; tinted yellow. |

Mappers fit modules into a ship template with preset markers (`presets.yml`, `WGShipModulePreset<Module>`): placed
anywhere on the grid, each fits its module for free when the ship spawns (`WGShipModulePresetSystem`, on map init) and
deletes itself, into hotkey `slot` 1-3 if set or the first free slot. A preset for a module the ship already has, or
for a filled slot, is skipped with a warning.

Ships flown by NPCs use their modules (`WGShipModuleNpcSystem`): a ship with modules fitted and an NPC at its helm
or guns (Mono ship AI or an NPC crew) and no player piloting it is checked every half second. The jets dodge a round on
course to hit within 1.2 s, sideways out of its path. The skimmer fires sideways when a missile is locked on within
250 m, breaking the lock, or when a big round (1000 damage or a blast of 300 intensity) will hit within 1.5 s. The
fortress goes up while the field took a hit in the last 4 s and the hull is at half heat, and comes down at 85% or
when the hits stop; the feeder fires while the ship's guns are engaged and the fortress isn't silencing them. Gyros
are passive. Only the search for incoming rounds costs anything, and it runs only for a ship whose jets or skimmer
could fire then (charged, off cooldown and unlocked, so not jets behind a raised shield).

Every module states its unlock: `free: true`, a `technology` whose node unlocks its `kit` at the circuit imprinter
(`recipes.yml`, in a pack on the imprinter's dynamic packs), or `unlisted: true`, which keeps it off drydock lists so
only presets and the `shipmodule` command fit it. The skimmer, gyros, fortress and feeder are unlisted for now; they
keep their kits and recipes, so making one researchable again takes a technology node, its `technology` line, and a
recipe pack on the circuit imprinter (a marked upstream edit to `lathe.yml`). A drydock fits a researched module only
with a kit (`WGShipModuleKitComponent`) carried anywhere aboard the ship, checked when the job is sold and again when it
finishes, and uses the kit up. `WGShipModuleAbilitiesTest` fails a module that states none or more than one.

A module's `mode` is `Fired` (each press spends a charge; a `duration` keeps it running), `Toggle` (each press switches
it, no charges) or `Passive` (on while fitted, started on the next tick after a fit or a load). A running or switched-on
module is switched off by any lock but a dead helm. The strip lights a running key, counts a timed use down, shows
ON/OFF for a switch and ONLINE, or a reading from `WGShipModuleGaugeEvent`, for a passive.

Sounds mostly reuse files already in the repo: the skimmer uses the void blink, Starlight's null phase and `phasein`,
the gyros use the mecha hydraulic, and the feeder uses the LMG cock and kinetic reload. The fortress cuts the ship shield
start-up short into a hum that loops while it is on (`fortress_on.ogg`, `fortress_hum.ogg`, built by
`Tools/_WG/ShipModules/synthesize_fortress.py`) and stops with the shield-off sound. Visuals are drawn by
`WGShipModuleFxOverlay` (skimmer ghosts, gyro rings), the jets' plume and smoke-ring event, the yellow cast
`WGShipModuleFxSystem` puts on boosted guns, and the shield overlay and radar, which draw a hardened field yellow
(`WGShipShieldHardenedComponent.FieldColor`).

The fortress puts `WGShipShieldHardenedComponent` (ShipShields) on the grid, which the shield heat
code reads for absorbed hits and the gun lock. The gyros multiply the turn step in `WGTurretTraverseSystem`. The feeder
divides each boosted gun's time between shots (`QueryFireRateMultiplierEvent`) and takes the same share off the pause
after a burst. Effect state on grids and guns is unsaved, so a ship saved mid-effect loads with nothing running.


## Adding a module

Add a `wgShipModule` prototype (name, description, strip label, drydock icon, price and materials, `free` or
`technology` and `kit`, mode, charges, recharge, cooldown, duration, shield rule, sounds, jolt) whose `effect` is a new
`WGShipModuleEffect` subclass with its settings. A server system handles the broadcast events, acting only when
`Module.Effect` is its kind: `WGShipModuleLockEvent` to lock it for reasons of its own (add a value to
`WGShipModuleLock` with `wg-ship-module-status-*` and `wg-ship-module-refused-*` strings),
`WGShipModuleActivateAttemptEvent` to refuse a use before the charge is spent, `WGShipModuleActivatedEvent` to do the
effect, `WGShipModuleDeactivatedEvent` to undo a running one and `WGShipModuleGaugeEvent` to show a reading on its key.
`WGShipModuleSystem.TryActivate` is the one entry point; the network request only adds the check that the sender is
piloting that ship.

## Maneuvering jets

`WGManeuveringJetsSystem`. The burn direction is the pilot's held strafe keys, relative to the hull, fixed in world
space when the burn starts; with nothing held the use is refused and no charge is spent. The kick is `deltaV` for a
hull up to `referenceMass` (60, a 120-tile fighter). Above that every hull gets the same push, so the kick falls in
proportion to mass, down to `minScale`; small ships dodge and big ones barely shift (about 16 m/s for a QJ-271 Heavy
Fighter, 5 m/s for a Neptune). It is then multiplied by CargoLoad's space thrust factor, so a laden hauler gets less. It is applied as
impulse over `burnTime` (`WGManeuveringJetsBurningComponent` on the grid), independent of the ship's thrusters. The jets lock while the ship is docked to anything or
its body is static, and each burn adds `heat` to the grid's thermal signature. Three charges, eight seconds each.

Each burn also sends `WGManeuveringJetsPuffEvent` to the map, naming the ship's powered thrusters that push along it
(up to 48), each with its size (the square root of its thrust against a standard 150 thruster, so a capital thruster
is twice as big) and how squarely it pushes. On clients `WGManeuveringJetsVisualsSystem` shows those thrusters' plumes
for the burn and puffs three smoke rings from each nozzle, out along its exhaust; `WGSmokeRingOverlay` draws them
turned across the exhaust, growing and fading over 1.4 s. The ring sprite is generated by
`Tools/_WG/ShipModules/gen_smoke_ring.py`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ShipModules/WGAmmoFeederSystem.cs`](WGAmmoFeederSystem.cs)
- [`Content.Server/_WG/ShipModules/WGFortressShieldSystem.cs`](WGFortressShieldSystem.cs)
- [`Content.Server/_WG/ShipModules/WGGunGyrosSystem.cs`](WGGunGyrosSystem.cs)
- [`Content.Server/_WG/ShipModules/WGManeuveringJetsSystem.cs`](WGManeuveringJetsSystem.cs)
- [`Content.Server/_WG/ShipModules/WGPhaseSkimmerSystem.cs`](WGPhaseSkimmerSystem.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleBayComponent.cs`](WGShipModuleBayComponent.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleCommand.cs`](WGShipModuleCommand.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleEvents.cs`](WGShipModuleEvents.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleKitComponent.cs`](WGShipModuleKitComponent.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleNpcSystem.cs`](WGShipModuleNpcSystem.cs)
- [`Content.Server/_WG/ShipModules/WGShipModulePresetSystem.cs`](WGShipModulePresetSystem.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleSystem.cs`](WGShipModuleSystem.cs)
- [`Content.Server/_WG/ShipModules/WGShipModuleSystem.Locks.cs`](WGShipModuleSystem.Locks.cs)

### Shared

- [`Content.Shared/_WG/ShipModules/WGAmmoFeeder.cs`](../../../Content.Shared/_WG/ShipModules/WGAmmoFeeder.cs)
- [`Content.Shared/_WG/ShipModules/WGFortressShield.cs`](../../../Content.Shared/_WG/ShipModules/WGFortressShield.cs)
- [`Content.Shared/_WG/ShipModules/WGGunGyros.cs`](../../../Content.Shared/_WG/ShipModules/WGGunGyros.cs)
- [`Content.Shared/_WG/ShipModules/WGManeuveringJets.cs`](../../../Content.Shared/_WG/ShipModules/WGManeuveringJets.cs)
- [`Content.Shared/_WG/ShipModules/WGPhaseSkimmer.cs`](../../../Content.Shared/_WG/ShipModules/WGPhaseSkimmer.cs)
- [`Content.Shared/_WG/ShipModules/WGShipModuleFitting.cs`](../../../Content.Shared/_WG/ShipModules/WGShipModuleFitting.cs)
- [`Content.Shared/_WG/ShipModules/WGShipModuleKeys.cs`](../../../Content.Shared/_WG/ShipModules/WGShipModuleKeys.cs)
- [`Content.Shared/_WG/ShipModules/WGShipModulePresetComponent.cs`](../../../Content.Shared/_WG/ShipModules/WGShipModulePresetComponent.cs)
- [`Content.Shared/_WG/ShipModules/WGShipModulePrototype.cs`](../../../Content.Shared/_WG/ShipModules/WGShipModulePrototype.cs)
- [`Content.Shared/_WG/ShipModules/WGShipModulesComponent.cs`](../../../Content.Shared/_WG/ShipModules/WGShipModulesComponent.cs)

### Client

- [`Content.Client/_WG/ShipModules/NavScreen.ShipModules.cs`](../../../Content.Client/_WG/ShipModules/NavScreen.ShipModules.cs)
- [`Content.Client/_WG/ShipModules/WGManeuveringJetsVisualsSystem.cs`](../../../Content.Client/_WG/ShipModules/WGManeuveringJetsVisualsSystem.cs)
- [`Content.Client/_WG/ShipModules/WGShipModuleBank.cs`](../../../Content.Client/_WG/ShipModules/WGShipModuleBank.cs)
- [`Content.Client/_WG/ShipModules/WGShipModuleFxOverlay.cs`](../../../Content.Client/_WG/ShipModules/WGShipModuleFxOverlay.cs)
- [`Content.Client/_WG/ShipModules/WGShipModuleFxSystem.cs`](../../../Content.Client/_WG/ShipModules/WGShipModuleFxSystem.cs)
- [`Content.Client/_WG/ShipModules/WGShipModuleHelmSystem.cs`](../../../Content.Client/_WG/ShipModules/WGShipModuleHelmSystem.cs)
- [`Content.Client/_WG/ShipModules/WGShipModuleHelmSystem.Jolt.cs`](../../../Content.Client/_WG/ShipModules/WGShipModuleHelmSystem.Jolt.cs)
- [`Content.Client/_WG/ShipModules/WGSmokeRingOverlay.cs`](../../../Content.Client/_WG/ShipModules/WGSmokeRingOverlay.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ShipModules/WGShipModuleAbilitiesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipModules/WGShipModuleAbilitiesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipModules/WGShipModuleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipModules/WGShipModuleTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/ShipModules/kits.yml`](../../../Resources/Prototypes/_WG/ShipModules/kits.yml)
- [`Resources/Prototypes/_WG/ShipModules/modules.yml`](../../../Resources/Prototypes/_WG/ShipModules/modules.yml)
- [`Resources/Prototypes/_WG/ShipModules/presets.yml`](../../../Resources/Prototypes/_WG/ShipModules/presets.yml)
- [`Resources/Prototypes/_WG/ShipModules/recipes.yml`](../../../Resources/Prototypes/_WG/ShipModules/recipes.yml)

### Localization

- [`Resources/Locale/en-US/_WG/ShipModules/ship-modules.ftl`](../../../Resources/Locale/en-US/_WG/ShipModules/ship-modules.ftl)

### Textures

- [`Resources/Textures/_WG/ShipModules/smoke_ring.rsi/`](../../../Resources/Textures/_WG/ShipModules/smoke_ring.rsi/)

### Audio

- [`Resources/Audio/_WG/ShipModules/attributions.yml`](../../../Resources/Audio/_WG/ShipModules/attributions.yml)
- [`Resources/Audio/_WG/ShipModules/fortress_hum.ogg`](../../../Resources/Audio/_WG/ShipModules/fortress_hum.ogg)
- [`Resources/Audio/_WG/ShipModules/fortress_on.ogg`](../../../Resources/Audio/_WG/ShipModules/fortress_on.ogg)
- [`Resources/Audio/_WG/ShipModules/jets_burn.ogg`](../../../Resources/Audio/_WG/ShipModules/jets_burn.ogg)

### Tools

- [`Tools/_WG/ShipModules/gen_smoke_ring.py`](../../../Tools/_WG/ShipModules/gen_smoke_ring.py)
- [`Tools/_WG/ShipModules/synthesize_fortress.py`](../../../Tools/_WG/ShipModules/synthesize_fortress.py)

## Non-modular edits

- [`Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs`](../../../Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs): ship module hotkeys
- [`Content.Client/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../../Content.Client/Shuttles/Systems/ShuttleConsoleSystem.cs): the module hotkeys work while piloting
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml): ship module hotkeys while piloting

<!-- WOLFGATE-GENERATED END -->
