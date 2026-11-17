# Random Encounters System (RES)

Module name `Encounters`. Folders: `Content.{Shared,Server,Client}/_WG/Encounters/`,
`Content.IntegrationTests/Tests/_WG/Encounters/`, `Resources/Prototypes/_WG/Encounters/`,
`Resources/Locale/en-US/_WG/Encounters/`, `Resources/Maps/_WG/Encounters/` (encounter grids; the client never loads
them, so they don't go in `SharedMaps`). Namespaces `Content.*._WG.Encounters`, prototype IDs `WGEncounter*`, cvars
`wg.encounters.*`, Fluent IDs `wg-encounter-*`. The roadmap already lists the feature as five Planned items
(`wg-roadmap-item-encounters-*` in `Resources/Prototypes/_WG/Roadmap/roadmap.yml`); flip them as milestones land.

## Why

Random events today are a weighted table of things that happen *to* a ship: vent critters, ion storms, random
sentience, solar flares, a bluespace crate dropping onto the deck, a smuggling fax. The only events that put something
new in space are the bluespace grid events (caches, vaults, FTL interceptions, salvage shuttles, dungeons), and they
appear at a random distance from the outpost with no crew, no behaviour and no stake for anyone who is not already
looking for salvage. Loot at best, an inconvenience at worst.

RES replaces that with encounters: things in the sector that move, talk, defend themselves and pay out, with a side for
pirates and a side for the faction that owns the cargo. It is also the groundwork for story-style content: NPC crews,
NPC-flown ships, trigger zones and payouts are the building blocks every later encounter reuses.

## What exists already

Checked before designing anything new. "Gap" is what the module has to add.

| Need | Exists | Gap |
|---|---|---|
| Event pacing | `BasicStationEventSchedulerSystem` picks weighted `StationEvent` rules from a table, one instance per prototype, gated by player count and cooldowns. No director of any kind. | A scheduler that runs several encounters at once, weighs faction presence and keeps a live-encounter cap. |
| NPC ship flying | Mono `ShipSteeringSystem` / `ShipSteererComponent` (`Content.Server/_Mono/NPC/HTN/`): put the component on any entity aboard, call `Steer(ent, coords)` and it registers as a pilot with `MoverController.AddPilot` and drives thrusters through `GetShuttleInputsEvent`, with collision and projectile avoidance. The HTN operator `ShipMoveToOperator` and the console `AutopilotShuttleCompound` are wrappers around it. | Waypoint routes, stop-on-engage, arrival handling. Driving the steerer directly avoids the HTN sleep problem (HTN pilots sleep with no player within range and the thrusters cut). |
| NPC ship guns | `ShipTargetingSystem` + `ShipFireGunsOperator` fire every `FireControllable` gun on the grid at the current target with `noServer: true`. | `NearbyNpcTargetsQuery` ignores factions (marked TODO) and shoots any powered console within 4000 m. Encounter ships need a target source fed by the zone system. |
| Crew AI | Frontier hostile humanoid bases (`MobSyndicateNavalBase`, `MobMercenaryBase`, `MobHumanoidHostileAISimpleRanged` and friends in `_NF/Entities/Mobs/NPCs/mob_hostile_*.yml`), `SimpleHumanoidHostileCompound`, `NPCRetaliation`, `MobPrying`. Ranged mobs carry the gun as an infinite-ammo component, so loadouts are looks and loot. | Vision is 10 tiles (`VisionRadius`/`AggroVisionRadius`), nothing lets one NPC alert another, every door with an access reader is pried rather than opened (pathfinding never checks the NPC's access), no radio operator. |
| Factions | Monolith companies (`CompanyPrototype`, `CompanyComponent.CompanyName` on mobs and on purchased grids): TSF/TSFCivilian/TSFHighComm, PDV (Phaethon Dynasty, the pirate side), USSP, MMC, MD and the corporations. NPC factions are separate (`TSFMC`, `USSP`, `VG`, `MMC`, `PirateNF`, `SyndicateNF` in `ai_factions.yml`). | A per-encounter mapping "owner faction = these company IDs + this NPC faction". No "Rogue" exists in either system. |
| Mass scanner | `RadarConsoleComponent.MaxRange` is 3072 everywhere; IFF labels cut off at 3000. Mono `RadarBlipComponent` (server only) draws shaped blips for any entity with a `PhysicsComponent`, grid or not, within `MaxDistance` (2048) of a radar. Mono detection shows undetected grids as a plain blip, detected ones as an outline. | Blips have no label; `Ring`/`GridAlignedBox` shapes exist in the enum but draw nothing. No zone/circle primitive. |
| Radio | `Traffic` is "Shortband" (1500 m). Faction channels: `Nfsd` ("TSF Comms"), `Ussp`; PDV has none. `RadioSystem.SendRadioMessage(source, text, channel, radioSource)` works from any entity; `ChatSystem.DispatchGlobalAnnouncement` for sector-wide. | Nothing. |
| Payout | `BankSystem.TryBankDeposit(mob, amount)`; sector accounts (`Nfsd` stands for TSF, `BlackMarket` for PDV); `CurrencyInjectionSystem.InjectCurrency(company, ...)` funds a faction uplink and announces it on its channel. Bounties mint cash from nothing. | Who qualifies and how much. |
| Grid lifecycle | `BluespaceErrorRule` loads with `MapLoaderSystem.TryLoadGrid` onto a temp map then `ShuttleSystem.TryFTLProximity` to a clear spot; `LinkedLifecycleGridSystem.UnparentPlayersFromGrid` deletes a grid without deleting players; Mono `AutoExtendRuleSystem` extends an event while players are near; Mono `GridCleanupSystem` deletes unpowered, label-hidden, cheap grids nobody is near. | Lazy load/unload. |
| Freight | `TradeCrateComponent` (value at destination vs elsewhere), `PricingSystem.AppraiseGrid`, `SharedContrabandTurnInSystem.HandleContrabandValueByCompany` (an item is worth nothing to its own faction). | Freight marker, destructibility, delivered-fraction accounting. |

Two engine-side facts shape the whole design: the sector is one unbounded map with stations 5 to 27 km out, and a
mass scanner sees 3 km. A freighter crossing the sector is therefore invisible to almost everyone almost all the time,
which is why it starts as a cheap contact and only becomes a grid when someone closes in.

## Lifecycle

Every encounter is one entity with `WGEncounterComponent` (the "encounter entity"), spawned by the scheduler and
parented to the map. Its states:

```
Scheduled -> Contact -> Live -> Engaged -> Resolved -> Cleanup
                 ^        |
                 +--------+   (unload while untouched)
```

- **Scheduled**: picked by the storyteller, origin and destination chosen, route planned, announcement sent. Lasts
  `announceLead` (default 60 s) so players can react to the announcement before the contact moves.
- **Contact**: the encounter entity *is* the blip. It moves along the route at `cruiseSpeed`. No grid, no NPCs.
- **Live**: a player grid came within `loadRange` (2048 m, the blip range, so the blip never visibly turns into a
  ship: Mono detection already shows a distant grid as a blip). The grid is loaded at the contact's position and
  velocity, the crew spawns, the autopilot takes over the route. The encounter entity stays as the owner of state.
- **Engaged**: a non-member crossed the fire zone, or boarded, or damaged the hull, or freight left the grid. The
  encounter can no longer unload; it ends only by resolution.
- **Resolved**: one of `Delivered` (reached destination with freight), `Destroyed` (grid gone or no freight left),
  `Derelict` (crew dead or no pilot and stopped, players gone for `derelictTimeout`), `Expired` (timed encounters) or
  `Abandoned` (admin or round end).
- **Cleanup**: payouts, follow-up encounters queued, grid deleted with `UnparentPlayersFromGrid` (never with players
  still aboard counted as cargo), encounter entity deleted. Every transition raises `WGEncounterStateChangedEvent` on
  the encounter entity so encounter-specific systems hook it instead of the core polling.

Unload: a Live encounter that is still untouched (never Engaged, hull undamaged, freight intact, nobody aboard who is
not crew) and has had no player grid within `unloadRange` (4000 m) for `unloadDelay` (90 s) goes back to Contact at
the grid's current position and velocity. Anything touched stays live to the end.

Ranges, defaults, cvars:

| Name | Default | Why |
|---|---|---|
| blip range (`RadarBlip.MaxDistance`) | 3072 | Match the scanner range so the contact shows wherever a grid would. |
| `loadRange` | 2048 | Beyond the visual detection radius of a freighter-sized grid (diagonal × 16), so the switch is invisible. |
| `unloadRange` / `unloadDelay` | 4000 / 90 s | Hysteresis so a ship hovering at the edge doesn't flap. |
| `wg.encounters.max_live` | 2 | A live freighter costs about as much as a player ship (atmos, power, 5–10 HTN NPCs). |
| `wg.encounters.max_contacts` | 4 | Contacts are nearly free; the cap is about announcement spam. |

## Encounters

### Freighter

"A high-value TSF freighter, designation XX-121, is scheduled for transit from Caelestinus Central to Helios
Fortress" on the sector announcement channel, then a contact named `Freighter XX-121` moving from A to B. Once a
player comes within `loadRange` the real freighter loads: a large hauler full of freight, crewed and flying.

The freighter has two circles on the mass scanner:

- **Warning zone**. A grid or player not in the owner faction entering it makes the captain broadcast on Shortband,
  naming the intruder's vessel, to leave immediately. Leaving the zone ends it; re-entering repeats it with a cooldown.
- **Fire zone**. Crossing it declares the intruder hostile: the captain broadcasts to the faction channel with the
  freighter's coordinates, the freighter's guns engage the intruding grid, and the crew turns hostile to everyone not
  in the owner faction. The freighter heaves to (autopilot stops) unless its bracket says otherwise.

The crew is 5 to 10 NPCs. Everyone fights when the fire zone is crossed. Two are special: the **pilot** owns the
autopilot and leaves the helm to fight once engaged, and the **captain** is the radio voice; killing the captain
silences the faction updates (and the follow-up call for help), which is a reason to board rather than bombard.

Two ways to take it:

- Disable the guns (and thrusters if the bracket keeps it moving), dock and board. Best loot: freight is intact and
  crates can be hauled over. Crew are alerted to boarders anywhere on the grid and converge through the ship.
- Destroy it from outside. Less loot: freight containers are destructible and lose most of their value when breached,
  and whatever survives is scattered in the wreck. No crew to fight through.

Faction players (actual players whose character is in the owner faction) hear the fire-zone broadcast with
coordinates. If the freighter reaches its destination, every qualifying escort gets paid from the delivered value.
The faction has a monetary reason to show up.

Freighters come in value brackets (below) that set cargo value, crew, guns and whether escorts fly with it.

### Freighter under attack

A stationary freighter is under attack from pirates. Render aid, or fight everyone and pilfer the wreckage.

Loads live immediately at a random point on a plausible route (no contact phase; the point of this one is the call for
help), with 1 to 3 NPC pirate ships flown by Mono's ship AI (`Attacker`/`Broadside` compounds) and a timeout instead of
a destination. The pirates attack any grid in range that isn't the freighter. Each pirate ship has a **pirate captain**
NPC aboard; a pirate ship is "dead" when its captain is dead (its steering and guns stop). When every pirate captain is
dead the freighter becomes a standard freighter of its bracket and sets off for a destination; escorts are paid on
arrival. If the timeout runs out with pirates alive, the freighter is destroyed (breached freight, crew dead) and the
encounter ends as `Derelict`.

The freighter's own crew is passive here until the fire zone is crossed by a non-member, which keeps "shoot the
pirates" and "shoot everyone" both legal.

### Engine fire

A distress call on Shortband and the faction channel, a `DISTRESS` contact on the mass scanner. Loads live when
approached: a small ship with a burning engine room (a fire spawned on map init), a passive crew sheltering forward,
and a thank-you payout to whoever is aboard when the fire is out and the crew is alive. The `trap` variant has a hidden
hostile crew that turns on boarders once they are inside, and no payout. Which variant is rolled is not visible from
outside; the crew's chatter is the only tell.

### Later

Convoys (two or three freighters sharing one escort), customs inspection (an armed faction cutter hails a player and
demands to board; contraband aboard makes it hostile), prisoner transport (free the prisoner, or deliver it),
derelict with survivors, a bounty target that runs. All are the same lifecycle with a different grid, crew roster and
resolution rule; none needs new systems.

## Freighter value brackets

Lowest to highest. Numbers are starting points for tuning, in spesos; cargo values are chosen against Frontier trade
crates (4250 at destination, 7500 for high-value) so a Low freighter is worth roughly six crates.

| Bracket | Cargo | Crew | Guns | Escort | Zones (warn / fire) | On fire zone | Faction alert |
|---|---|---|---|---|---|---|---|
| Low value | 25k | 5, sidearms | none | none | 350 / 200 m | heave to | Shortband only |
| Medium value | 50k | 6, one rifle | 1 light turret | none | 400 / 250 m | heave to | faction channel |
| High value | 100k | 8, rifles | 2 turrets | none | 500 / 300 m | heave to | faction channel |
| Military transport | 150k + weapons crates | 10, armoured | 2 heavy turrets | 1 fighter | 600 / 400 m | keeps moving | faction channel + announcement |
| Nuclear transport | 250k + nuclear material | 10, armoured | 3 heavy turrets | 2 fighters | 800 / 500 m | keeps moving | faction channel + announcement |

Escort fighters are NPC-flown grids (Mono ship AI) that spawn with the freighter, orbit it in `Orbit` mode and attack
whatever the freighter declares hostile. A bracket that keeps moving is boardable only by matching velocity or by
killing its thrusters; that is the point of the bracket.

# Technical sheet

### Module layout

Server (`Content.Server/_WG/Encounters/`):

- `EncounterSchedulerSystem`: the storyteller. A `GameRuleSystem<WGEncounterSchedulerRuleComponent>` run from the
  preset's `rules:` list, not a `StationEvent` in the basic table (that table allows one instance per prototype and
  resets its timer whether or not anything ran).
- `EncounterSystem`: lifecycle, state machine, load/unload, resolution, cleanup. Owns `WGEncounterComponent`.
- `EncounterContactSystem`: moves Contact-state encounter entities along their route.
- `EncounterRouteSystem`: picks origin/destination, plans waypoints around known static grids.
- `EncounterZoneSystem`: warning/fire/escort circles, membership tests, intruder tracking, hostile list.
- `EncounterCrewSystem`: spawns the roster, alerts crew, tracks the captain/pilot/leader roles and deaths.
- `EncounterAutopilotSystem`: waypoint following on top of `ShipSteeringSystem`.
- `EncounterGunnerySystem`: feeds the hostile list to the ship's guns.
- `EncounterCommsSystem`: announcements and radio lines from Fluent.
- `EncounterPayoutSystem`: escort credit and payment.
- `EncounterCommand`: admin command `wg_encounter`.

Shared: `WGEncounterPrototype`, `WGEncounterBracketPrototype`, `WGEncounterZonesComponent` (networked, for the radar
rings), enums and events. Client: `ShuttleNavControl.Encounters.cs` partial that draws the rings, following the
`_WG/ShipShields` partial. Prototypes: encounters, brackets, crew mobs, freight containers, escort vessels. Grids:
`Resources/Maps/_WG/Encounters/*.yml`.

### Storyteller

v1 is a pacer, not a narrator. `EncounterSchedulerSystem.ActiveTick` every 30 s:

1. Count Contacts and Live encounters; stop if either cap is hit.
2. Build the candidate list from `WGEncounterPrototype`s: `minPlayers`/`maxPlayers`, `cooldown` since the same
   prototype last ended, `earliestStart` into the round, `requiresOnline` (an owner-faction member must be online, so
   a TSF freighter only flies when a TSF player can answer; off by default for Low).
3. Weighted pick, weight × `factionPresenceWeight` (more members of the owner faction online, more of its traffic).
4. Spawn the encounter entity in `Scheduled`.

Pacing knobs: `wg.encounters.interval_min/max` (default 900–1800 s between spawns), the two caps, and a per-faction
budget so one faction doesn't get every spawn. Everything the scheduler decided is logged at Info with the encounter
ID, so an admin can see why something did or didn't fire.

What makes it a storyteller later: `WGEncounterPrototype.followUps` (resolution → list of weighted prototypes with a
delay), so a destroyed TSF freighter can queue a TSF patrol along the same route, or a pirate victory can queue
"freighter under attack" with the same pirates. The scheduler treats a follow-up as a normal candidate with its
origin/destination pinned. No scripting language, no quest state; chains of prototypes are enough for a long time.

The `WGEncounterPrototype`:

```yaml
- type: wgEncounter
  id: WGEncounterFreighterTsfMedium
  kind: Freighter                       # Freighter, FreighterUnderAttack, Distress
  bracket: WGBracketMedium
  owner:                                # the faction
    companies: [TSF, TSFCivilian, TSFHighComm]
    npcFaction: TSFMC
    radioChannel: Nfsd
    announceSender: wg-encounter-sender-tsf-traffic-control
  grids:
    - path: /Maps/_WG/Encounters/freighter_medium_tsf.yml
      role: Primary
  nameTemplate: wg-encounter-name-freighter     # "Freighter {$designation}"
  designationGroup: WGFreighterDesignation      # "XX-121" style
  route:
    originStations: [Caelestinus, HeliosFortress, ...]   # by station prototype or tag; empty = any station
    minDistance: 4000
    cruiseSpeed: 12
  weight: 10
  minPlayers: 8
  cooldown: 1800
  requiresOnline: true
  followUps:
    Destroyed: [{ id: WGEncounterPatrolTsf, weight: 1, delay: 600 }]
```

Brackets (`wgEncounterBracket`) hold the numbers in the table above plus the roster and gun loadout, so a new freighter
grid is a map file and a prototype.

### Contacts (simulated blips)

The encounter entity in `Contact` state carries `RadarBlipComponent` (config: a small orange circle, the same look as
an undetected grid; `VisibleFromOtherGrids`, `RequireNoGrid` false, `MaxDistance` 3072) and a kinematic
`PhysicsComponent` with `LinearVelocity` set to the route velocity. The blip system reports velocity, so the client
extrapolates between its 2 Hz updates and the dot moves smoothly instead of stepping; moving it by teleport would step.
`EncounterContactSystem` only has to turn the corner at each waypoint and detect load conditions.

Blips have no text. The name reaches players three ways: the announcement, the IFF label of the live grid once within
3 km, and the captain's radio lines. Deliberately not used: an `IFFComponent` beacon entity with a global PVS override
(what the star-system map does), because that labels the contact with name, distance and coordinates from anywhere in
the sector and turns the hunt into a waypoint. Interception is meant to be "we know it goes from A to B, go sit on
the line". If a bracket wants to be found, `broadcastPosition` puts coordinates in its announcements.

Load check, every second per contact: any grid with a player aboard (`ActorComponent` with `GridUid` set) within
`loadRange` on the same map. Ignore ghosts. The grid is loaded on a temporary map with `TryLoadGrid`, moved with
`TryFTLProximity` to the contact's position (clears overlaps the way bluespace events do), given the contact's
velocity, named, and given `CompanyComponent` = the owner's primary company (so the IFF label shows the faction colour
and `WGShipAccessSystem.IsFactionGrid` works for member access). It is not a shipyard ship, so it never gets
`IFFFlags.IsPlayerShuttle`. `GridCleanupSystem` must not touch it: keep the IFF label shown (that system only deletes
label-hidden grids) and add `WGEncounterGridComponent` as an explicit opt-out.

Unload reverses it: record position and velocity, `UnparentPlayersFromGrid` (nobody should be aboard by definition,
but a stowaway is moved to space rather than deleted), delete, resume as Contact.

### Trigger zones

`WGEncounterZonesComponent` on the encounter entity and mirrored (networked) on the live grid: `warningRadius`,
`fireRadius`, `escortRadius` (default 1500 m, used for payout credit), plus the current `State` per zone so the client
can colour the rings (grey, yellow, red). The rings are drawn by the client partial in world scale around the grid's
centre, like the exclusion circles on the FTL map, and only while the grid is detected. The `Ring` blip shape stays
unused; the zones need two radii and a colour state, not a blip.

`EncounterZoneSystem` runs at 2 Hz per live encounter: `EntityLookupSystem` for grids within `warningRadius` of the
freighter's centre, plus mobs not on any grid (EVA players count). Membership: the grid's `CompanyComponent.CompanyName`
is in `owner.companies`, or for a mob its own `CompanyComponent`. A faction member on an unaffiliated ship is not a
member; the ship is what the zone sees. Per intruder it keeps `{ firstSeen, deepestZone, warnedAt }` so the warning
fires once per entry with a `warnCooldown` (60 s) and the fire zone fires once. Crossing the fire zone adds the grid
(and everyone aboard) to the encounter's `Hostiles` set, raises `WGEncounterHostileDeclaredEvent`, and moves the
encounter to `Engaged`. Hostiles never clear by leaving; the encounter remembers for the rest of its life.

Three other ways into `Engaged`, all raised by small subscriptions on the live grid: a non-member mob changes parent to
the freighter grid (boarding), the grid takes hull damage from a projectile whose shooter grid is a non-member
(`ShipWeaponProjectile`), or a freight entity leaves the grid.

Trigger zones are a reusable piece: a `WGTriggerZoneComponent` with radii and a membership predicate, with the encounter
subscribing to its enter/exit events, so outposts or stations can get the same "warn then fire" behaviour later.

### NPC system and NPC types

How a crew NPC works a job and switches to combat, the pilot and radio officer duties, crew alerting and the access
edit for doors are the `NpcCrew` module (`Docs/_WG/NpcCrew/design.md`), which stands on its own. RES supplies the
roster, the posts, the hostile list and the owner faction; the crew behaviour below is what it gets from `NpcCrew`.

Roster per bracket, spawned at marked spawn points in the grid (`WGEncounterSpawnPointComponent` with a `role`), as
`NpcCrew` crew with real loadouts, holstered until there is a fight; the loadout is part of the loot:

| Role | Base | Behaviour |
|---|---|---|
| Captain | `WGEncounterCaptain` (ranged, bridge) | Radio voice. All comms go through `EncounterCommsSystem` naming the captain as source; death silences faction updates and the follow-up call for help. |
| Pilot | `WGEncounterPilot` (ranged, helm) | Carries the `ShipSteererComponent`. On `Engaged` the autopilot stops (if the bracket heaves to) and the pilot fights like the rest. If the pilot dies while the bracket keeps moving, the ship coasts: no pilot, no steering. |
| Deckhand | `WGEncounterDeckhand` (melee or sidearm) | The numbers. Passive until alerted. |
| Marine | `WGEncounterMarine` (rifle, armour) | Military brackets. |
| Gunner | none | Ship guns are fired by `EncounterGunnerySystem`, not by a mob; a "gunner" NPC is just a deckhand placed in the gun room so there is someone to kill to make the guns stop (the system fires only while a gunner is alive, `requireGunner` per bracket). |
| Escort pilot | Mono `NpcStationAi*` core | Standard Mono ship AI with the encounter's target source. |
| Pirate captain | `WGEncounterLeader` | "Freighter under attack": the ship dies with him. |

Factions: crew are `NpcFactionMember` of `owner.npcFaction`. Players default to NanoTrasen, which is neutral to TSFMC
and PirateNF, so the crew ignore everyone until told otherwise. That is exactly the warning/fire behaviour: nothing
is hostile by prototype, hostility is per-entity through `NpcFactionSystem.AggroEntity` when the zone system declares
a hostile. Retaliation is already there (`NPCRetaliation`): shoot a deckhand through a window and it aggroes you.

Alerting. Vision is 10 tiles and there is no shared awareness, so `EncounterCrewSystem` does it: on `Engaged`, and
again whenever a non-member is aboard, every living crew member gets `AggroEntity` for each hostile mob aboard,
`AggroVisionRadius` on its blackboard raised to cover the ship, and `HTNSystem.Replan`. That makes the crew converge on
boarders through doors regardless of line of sight, which is the stated intent. Alert state resets to "guard" (vision
back to default, idle at post) 60 s after the last hostile aboard is dead or gone.

Doors. Pathfinding treats any door with an `AccessReaderComponent` as pry-only for NPCs, and base airlocks always have
one, so a stock interior would be pried open by its own crew. Encounter grids use `WGAirlockEncounterInterior` (an
airlock variant without the reader) for interior doors; boarders can open them too, which is fine, the crew is the
obstacle. Exterior airlocks keep their reader so boarders dock or breach, and crew pry them only when chasing someone
out.

Known traps: `HostileNPCDeletionSystem` ashes active NPCs hostile to NanoTrasen on a `ProtectedGrid` with
`KillHostileMobs`, so a freighter must never dock at a station (it despawns at its destination point instead) and crew
must stay neutral by prototype. Crew sleep when no player is within 32 tiles (`npc.player_pause_distance`), which is
free and correct. `npc.max_updates` (256) is shared by all NPCs round-robin; a cap of 10 crew per freighter and two
live encounters keeps RES under 40 of them.

### Autopilot (NPC controlled)

`EncounterAutopilotSystem` drives Mono's steerer directly: `ShipSteeringSystem.Steer(pilot, waypoint)` with
`Mode = GoToRange`, `Range` = arrival radius (60 m), `AvoidCollisions` on, `InRangeMaxSpeed` = cruise speed, then on
`Status == InRange` advance to the next waypoint and `Steer` again; at the last waypoint `Stop` and resolve
`Delivered`. No HTN on the pilot, so no sleeping pilot: the steerer answers `GetShuttleInputsEvent` whenever the mover
asks. If the pilot dies, `Stop` and leave the ship coasting; brackets that heave to have already stopped.

Speed: freighters cruise slowly (8–15 m/s) so a 10 km route takes 12 to 20 minutes, long enough to intercept. The
contact moves at the same `cruiseSpeed` so load/unload is position-continuous. A grid's real top speed depends on its
thrusters and mass, so `InRangeMaxSpeed` caps it and the map file must have enough thrust to reach it.

Routes (`EncounterRouteSystem`): origin and destination are two station grids from the snapshot taken on
`StationsGeneratedEvent` (stations, depots and POIs are all placed by `NFAdventureRuleSystem` at round start; player
ships are stations too and are filtered out by `ShuttleDeedComponent`). Start and end points are offset
`routeStandoff` (600 m) from each grid so the freighter never enters a station's traffic or its `ProtectedGrid`. The
segment is checked against the bounding boxes of every static grid grown by `routeMargin` (400 m); a hit inserts a
waypoint around the box's near corner and the check repeats. Debris and asteroid belts are not in the snapshot; the
steerer's collision avoidance and the live sweep handle those, and a Contact can't collide with anything because
unloaded chunks hold nothing.

Heave to: `Stop`, then the steerer is re-added with the current position as target so the ship brakes rather than
drifting (`GoToRange` with `Range` 5 does that), then removed once the velocity is near zero.

### Ship weapons

`EncounterGunnerySystem` replaces the HTN query for encounter grids: every 3 s, pick the nearest entry of the
encounter's `Hostiles` that is within gun range and on the same map, and call the same path `ShipTargetingSystem`
uses (`FireControlSystem.AttemptFire` on every `FireControllable` on the grid, `noServer: true`, leading by the
target's velocity). Guns fire only while `Engaged` and, if the bracket says `requireGunner`, while a gunner lives.
Destroying the gun entities themselves is the other way to silence them, and the one players will expect.

Escort fighters use Mono's ship HTN unchanged except for the target query: a `WGEncounterHostilesQuery` utility query
that returns the encounter's hostile grids instead of `NearbyNpcTargetsQuery`'s "every powered console". Until that
query exists, escorts would attack every player in 4 km including the faction's own responders, so escorts are gated on
it (milestone 3).

### Communications

All text is Fluent in `Resources/Locale/en-US/_WG/Encounters/`. `EncounterCommsSystem`:

- **Schedule announcement**: `ChatSystem.DispatchGlobalAnnouncement` with the owner's `announceSender` ("TSF Traffic
  Control"), the designation, origin and destination names. Military and nuclear brackets announce; the rest go only
  to the owner's channel so pirates hear about a Low freighter by listening in or by luck. A faction that wants its
  freight hunted less should run quieter freighters; that is a knob, not a rule.
- **Warning**: `RadioSystem.SendRadioMessage(captain, text, Traffic, grid)` with the intruder's IFF label
  (`SharedShuttleSystem.GetIFFLabel`) in the text. Shortband has a 1500 m range and needs a telecom server in range or
  `TelecomExemptComponent` on the radio source; the live grid gets the exempt component. The radio system drops an
  identical message already in flight, so the text carries the designation and intruder name and varies by template.
- **Hostile declared**: one mayday on the faction channel with sector coordinates rounded to 100 m, then one line per
  key event while the captain lives (boarded, pilot down, hostiles gone). No periodic sitreps; the radio reports things
  that happen, not time passing.
- **Delivered / destroyed / captain dead**: one line each on the faction channel; delivered also announces the payout.
- The captain speaks through `TrySendInGameICMessage` as well for local flavour on the bridge, but every line that
  matters goes out by `SendRadioMessage` from the captain entity, so his death ending the updates is literal.

### Payout system

Paid on `Delivered`, from `deliveredValue` = sum of `WGEncounterFreightComponent.Value` of freight still on the grid
(not an appraisal of the grid; `AppraiseGrid` is expensive and counts the hull). Qualifying players: any player whose
character is in `owner.companies`, aboard a member grid that was inside `escortRadius` for at least `escortMinShare`
(25 %) of the time between the first `Engaged` and arrival, or who dealt damage to a declared hostile (hull hits
tracked through `ShipWeaponProjectile` owner grid; mob kills through the damage source). Payout per player =
`bracket.escortShare` (20 %) × `deliveredValue` / qualifying players, capped by `bracket.escortCap` per player, paid
with `BankSystem.TryBankDeposit(mob, amount)`; offline players at arrival use the `TryBankDepositOffline` path keyed
by `NetUserId`. An untouched delivery pays nothing: no threat, no escort fee. Faction players who want steady money
should want their freighters attacked, which is the correct incentive.

Optional faction income: on delivery `CurrencyInjectionSystem.InjectCurrency(owner.company, amounts)` with a small
cut, so the faction uplink grows with safe traffic and the uplink's own channel announces it. Off by default; it is a
balance lever for the faction economy rather than part of RES.

Collusion (a friend triggers the fire zone and leaves, the escort collects) is bounded rather than solved: payout
needs at least one hostile grid to have been destroyed, disabled, or to have stayed in the fire zone for
`hostileMinTime` (3 min); payouts per account per hour are capped (`wg.encounters.payout_hourly_cap`); every payout is
logged with the hostile grids and their owners' accounts. The same hole exists in pirate bounties today and is handled
the same way, by admins reading logs.

### Loot and freight

Freight is entities with `WGEncounterFreightComponent { Value, Owner }` spawned into the hold at marked points from the
bracket's freight table. Three rules:

- **Own-faction worthlessness**: freight is contraband-like for its owner. Reuse the
  `HandleContrabandValueByCompany` pattern: a TSF character selling TSF freight gets nothing (and the pallet console
  says why). This is what stops a faction farming its own freighters.
- **Destructible**: freight containers have `Destructible` thresholds; a breached container spawns a `ruined` variant
  worth `ruinedFraction` (30 %) and drops contents. Obliterating the hull breaches most of them, which is the "small
  loot ratio". Boarding leaves them intact and still has to get them off the ship, which is the "best ratio" and also
  the slow one.
- **Counted**: freight leaving the grid (parent change) or being destroyed updates `deliveredValue` live, so the
  payout reflects exactly what arrived.

Weapons and nuclear material in the top brackets are real items from existing prototypes, with the same marker so they
count; that is where the "military transport" fantasy comes from, not from a bigger number.

### Cleanup, abandonment and limits

- A Live freighter players walk away from keeps flying. Untouched: it unloads back to a Contact and continues; it is
  still interceptable later. Engaged: it flies on, arrives, pays nothing or pays escorts, despawns. A stopped
  freighter with a dead crew or dead pilot is `Derelict` after `derelictTimeout` (45 min) with no player within
  `unloadRange`, then deleted; a boarded wreck with players still looting stays as long as someone is near.
- Destination arrival deletes the grid with `UnparentPlayersFromGrid`, so a stowaway is left floating at the
  destination point, not deleted.
- Round end: every encounter resolves `Abandoned`, grids are deleted, nothing is paid.
- Admin force-end is the same path.
- The two caps above, a 10-crew roster cap, and no atmosphere simulation shortcuts: a live freighter is a normal grid
  and costs what one costs. If profiling shows atmos dominating, the freighter maps can be built without a pressurised
  hold; the crew don't need air.

### Admin tools

`wg_encounter` (admin, `+ADMIN`): `list` (id, prototype, state, position, hostiles), `spawn <prototype> [near <player>]`
(skips Scheduled, loads live next to the admin or a player for testing), `load <id>` / `unload <id>`, `engage <id>`
(declare the admin's grid hostile), `resolve <id> <resolution>`, `pause` (scheduler off until `resume`). Everything
also shows in VV on the encounter entity. The scheduler's decisions log at Info.

### Tests

`Content.IntegrationTests/Tests/_WG/Encounters/`:

- Lifecycle: a test prototype with a tiny grid; spawn Scheduled, step to Contact, place a player grid inside
  `loadRange`, assert a grid exists with crew; move the player away, assert unload after the delay; mark Engaged,
  assert no unload.
- Zones: a non-member grid at warning radius raises exactly one warning and no hostiles; at fire radius raises
  `WGEncounterHostileDeclaredEvent` once and the encounter is Engaged; a member grid raises nothing.
- Payout (pure logic, `Content.Tests/_WG/Encounters/`): share and cap maths, qualification from a recorded escort
  timeline, zero payout on untouched delivery.
- Route (pure): a segment through a box gets a waypoint that clears the box by the margin; a clear segment gets none.
- Freight: breaching a container spawns the ruined variant with the right value; removing freight updates
  `deliveredValue`.
- Autopilot: a grid on a test map reaches two waypoints and stops; marked slow and skipped on CI if it proves flaky.

No `Destructive` pool settings; the lifecycle test reuses the pair and deletes what it spawned.

### Milestones

1. **Core**: prototypes, scheduler as a plain interval pacer, lifecycle without Contact (encounters load live at a
   random point on the route), zones and radar rings, crew with alerting, comms, freight, payout. Ships the Low and
   Medium TSF freighter, stationary (`cruiseSpeed` 0). Flip `encounters-freighters` to InProgress.
2. **Movement**: contacts, routes, autopilot, unload, heave to. Freighters actually transit.
3. **Brackets and escorts**: High, Military, Nuclear; the faction-aware ship target query; escort fighters; PDV and
   USSP owners. Flip `encounters-tiers`.
4. **Pirates and distress**: freighter under attack, engine fire and its trap variant. Flip `encounters-pirates`,
   `encounters-distress`.
5. **Storyteller**: follow-ups, faction presence weighting, budgets. Flip `encounters-storyteller`.

## Decisions and pushback

- **Lazy loading is kept, but only until something is touched.** A contact costs nothing and most freighters are never
  visited on a 10–27 km map, so the ghost is worth having. Re-ghosting a looted or damaged ship would need the grid's
  state serialised and restored; not worth it. Touched ships stay live until resolved. If it turns out players visit
  most freighters anyway, drop the Contact state and spawn live at announcement; the rest of the design doesn't change.
- **The freighter stops on the fire zone, not when the pilot "sees you".** Line of sight from a bridge NPC to a ship is
  arbitrary and invisible to the player; the fire zone is a line the player chose to cross and can see. Boarding a
  moving grid also requires matched velocity and a dock within 1.2 m and 15°, which is miserable, so heaving to is a
  gameplay concession and the top brackets deliberately don't make it.
- **Guns are a system, not a mob's HTN.** Mono's ship AI fires at every powered console within 4 km with no faction
  check; using it unchanged on a freighter means it shoots the faction's own rescuers. The encounter's hostile list is
  the only target source.
- **Faction = Monolith company, not NPC faction.** Players and purchased grids already carry `CompanyComponent`; NPC
  factions are for mobs. The mapping lives in the encounter prototype. There is no "Rogue" anywhere; the pirate side
  is PDV. A member on an unaffiliated ship is an intruder, because the zone sees ships.
- **No sector-wide label for the contact.** Wherever the user sees "mass scanner blip", read "a blip within scanner
  range". The announcement gives the route; finding the line is the game. Brackets can opt into broadcasting
  coordinates.
- **Payout needs a threat.** Paying escorts for an untouched delivery makes every freighter a free taxi for faction
  players. Zero for untouched, a share of delivered value otherwise, capped and logged.
- **Own-faction freight is worthless to its faction.** Otherwise the fastest money for a TSF crew is shooting TSF
  freighters from an unaffiliated hull.
- **Not a station event.** The basic scheduler's one-instance-per-prototype rule and timer behaviour don't fit
  concurrent encounters; a dedicated rule is simpler than fighting it.

## Open questions

Answers change the design; defaults are what the doc assumes.

1. **Which owners ship first?** Assumed TSF only in milestone 1 (it has a radio channel, an NPC faction and the
   most players). PDV has no channel; a `Pdv` radio channel would be a marked edit outside `_WG` or a Fluent-only
   "Shortband" fallback. USSP has `Ussp`.
2. **Should the fire-zone broadcast carry coordinates?** Assumed yes for the faction channel (responders need them)
   and no for the Shortband warning (pirates already know where they are). Alternative: spawn an `FTLBeacon` named
   "DISTRESS Freighter XX-121" so responders can jump straight in; cheaper to find, but every pirate in the sector sees
   it too.
3. **Do freighters stop or keep moving by default?** Assumed stop for Low–High, keep moving for Military and Nuclear.
   If boarding a moving ship turns out to be fun with tractor beams and tethers, lower the threshold.
4. **How loud are announcements?** Assumed only Military and Nuclear go sector-wide; the rest are on the owner's
   channel. Louder means more fights and faster farming of Low freighters.
5. **Escort fighters in v1 or later?** Assumed later (milestone 3) because the faction-aware target query has to exist
   first.
6. **Where does the pirate "Freighter under attack" freighter go after rescue?** Assumed a random station of its
   owner; it could instead head for the nearest one so the rescue pays sooner.
7. **Payout numbers.** 20 % of delivered value split among escorts, per-player cap by bracket, zero if untouched. Too
   low and nobody responds; too high and factions want their freighters attacked. Needs a week of logs.
8. **Should delivery feed the faction uplink (`InjectCurrency`)?** Assumed off. It ties RES to the faction economy
   balance, which is a separate conversation.
