# Remade

An NPC-driven horror threat, from DS13's necromorph game. A Monolith Scion gives free power, and the harder it is drawn
on the faster it climbs towards the Crescendo. On the way it brings whispers, corruption, the Remade and a Director that
hunts the crew. Names are Wolfgate's own: Marker is Monolith, necromorph is Remade, Unitologist is the Choir, convergence
is the Crescendo.

**What players do.** Wrench a Scion onto HV cable, stand a Tap within 4 m and set the draw. Drag a corpse onto the Tap to
take 5 off resonance, the only way down. Only Caelestinus Central buys a Scion: it sells only from a pallet on a main
station, for 5,000,000 plus as much again scaled by resonance (double at the Crescendo). The Lattice's hex field turns
any Scion back, so selling one means bringing an anchor down first. Corpses on corruption turn after 90 seconds, and one with no head never turns. Fire kills
corruption, and breaking a Scion (3000 damage) kills its corruption and starves its Remade.

**The siege** (`Siege/`, `WGSiegeSystem`). A Scion that reaches Harvest sends flesh ships against the reality anchors:
the first after 3 minutes, then one every 15 minutes, two a third sooner at Rapture, three twice as often at the Crescendo,
at most 7 alive. Each is a Choir-crewed `WGEncounterNecroticFleet` hull overgrown with corruption, carrying Remade, set
on the standing primary anchor nearest the Scion. Within 900 m it bleeds the anchor's shield, then its core once the
shield is down, and puts two Remade on the station every minute; the anchor's lances and drones answer it. When its anchor
falls it moves on to the next. Waves stop with the Unstringing. Tuning is in `WGSiegeCVars` (`wg.remade.siege.*`).
Inside a sealed hex field nothing of a Monolith outside it reaches anyone, and the hex counts as a sanctuary (see the
Lattice README); a standing anchor's ward keeps corruption off nearby grids (`WGCorruptionSystem.IsWarded`).

**Admins.** Spawn `WGMonolithScion` and `WGMonolithTap`, or `WGMonolithSplinter` for an outbreak on one ship. Set `Resonance` on `WGMonolithComponent` with VV to skip ahead:
15 Hymn, 35 Growth, 55 Harvest, 80 Rapture, 100 Crescendo.

**Entry points.** `WGMonolithMath` (the climb, stages and output), `WGMonolithSystem`, `WGMonolithTapSystem`,
`WGCorruptionSystem`, `WGRemadeConversionSystem` (rules from DS13-1.0's `conversion.dm`, each mob's cost and weights in its `wgRemadeMob`
prototype), `WGRemadeDirectorSystem` (phases Quiet, Stalk, Peak, Relax; sleeps with no crew in 64 m). Assets come from
`Tools/_WG/Remade/port_assets.py`, which holds the whole DS13-2.0 roster; only the slasher and infector have mobs so far.

**How they hit.** A Remade has no aim of its own, so each blow lands on a random part of its victim, by the victim's own
odds. Their claws hit hard enough for Wolfmed's deep-cut rules: a slasher's 24 slash can open an artery (22) and cut a
tendon (16) in one swing, so they bleed people out and take them apart rather than scratching. Giving them `Targeting`
would also change how damage they take is routed, which the Undying and the Crescendo tests catch.

**Limbs and abilities.** A Remade built of limbs (`WGRemadeLimbs`, Shared) shares each hit between its limbs by weight, or by a
player's targeting aim, and the torso takes only a fraction, so shooting it barely works. Slash and piercing sever easily, blunt and
heat poorly. Severing hides the limb's layer, drops it as an item and costs the mob a lump of damage, so dismemberment is the fast
kill. What each limb gives the mob (melee, speed, voice, sight, reanimation) is data on the limb. `WGRemadeAbilities` gives an NPC
abilities (`WGShoutAbility`, `WGChargeAbility`, `WGDodgeAbility`) that `WGAbilitySystem` runs for awake NPCs with a target; new ones
subclass `WGAbility`.

**The Director.** It plays the Monolith with hearing, nerves and tricks rather than omniscience. Gunfire, blasts and cries draw the
Remade; otherwise they drift towards the crew roughly, sharpening each time they find nobody. Waves start in far vents, rattle through
the ducts where the crew can hear, and burst out of several vents round the prey at once. Some Remade lie among the dead and rise when
approached or hit. A hurt Remade out of sight retreats to a vent and returns later, and lone stalkers follow a crewman at a distance
before committing. Between waves it works on one crewman at a time with flickers, whispers, false voices, scrawls, runes, doors, blown
lights and a biohazard lockdown, paid from a Will pool that grows with the stage. Each crewman has a stress value: frightened crew are
left alone, calm crew are pressed harder, and nobody gets a second wave straight after the first below Rapture.

**Roster.** The common Remade are in `Resources/Prototypes/_WG/Remade/roster.yml`: hopper, leaper (plain and black), spitter, exploder
(plain and black), puker and lurker. A hopper or leaper leaps across a room (`WGLeapAbility`) and stabs with its tail
(`WGTailStabAbility`); both need their legs. A spitter fires acid (`WGShootAbility`) and, like the lurker, fights from range through
`WGSkirmishOperator`. An exploder swells and bursts after a fuse (`WGExplodeAbility`, `WGExploderSystem`) while it has its pustule; a
pustule shot from range bursts on the spot, and one cut off up close just comes away. A puker vomits a cone of acid that burns and
slows (`WGVomitAbility`). A lurker (`WGSpineAbility`, `WGShellSystem`) opens its shell to fire one spine per tentacle and curls up when
hurt; its tentacles can only be hit while it is open.

**Growths.** The Director grows structures on corruption (`wgRemadeGrowth` prototypes, `WGGrowthSystem`). Nodes (`WGCorruptionBranch`,
`Root`, `Propagator`) feed corruption within their reach and raise a Monolith's tile cap past its stage limit, to 512 per hull; killing
one starves the tiles it fed. Cysts fire bio-bombs from walls at anyone in their arc, and snares trip and slow whoever steps on them.
Maws warn, grab after 3 seconds, bite, take limbs and eat the dead; free someone by clicking them and pulling, or kill the maw. Nests
slowly make up to two of their chosen Remade, harvesters turn plants, food and bodies nearby into biomass, eyes tell the Director where
crew walked, and lamps give a dim sickly light. The Director builds out of the crew's sight, keeping a reserve for waves: an Infector
walks to the site and works there, or a seed swells up slowly. Every growth goes dormant off corruption and dies when its Monolith goes.

**Elites.** The brute, twitcher, oracle twitcher, hunter, divider, tripod, the Undying and the enhanced infector are in
`Resources/Prototypes/_WG/Remade/elites.yml`. A brute takes a third of the damage from the front and one and a half times from behind
(`WGBruteArmourComponent`), slams all round itself, rams through doors and windows, and lobs acid. A twitcher slips bullets from a pool
that drains as it dodges (`WGDodgePoolComponent`), and blinks and charges. A hunter heals, regrows limbs and gets back up 15 s after
dying (`WGRegenComponent`, `WGRisesComponent`), unless it was burned, beheaded, mostly dismembered or killed again within two minutes. A
divider breaks into crawling arm, leg and head mobs as its limbs come off and when it dies (`WGDividerSystem`). A tripod hauls people in
with its tongue, leaps and swings. The Undying is announced to everyone on its grid, never runs, and folds up for a minute when it takes
lethal damage or loses every limb. The enhanced infector turns corpses in about half the time (`WGReanimateHasteSystem`).

Gifts (`Gifts/`). Each Gift is a dial on the Tap; the dials add as the root of their squares, capped at 1.5, to drive the climb.
Matter slowly fills a material silo on the Scion's ship; that stock can't be ejected as sheets, and what a lathe prints from it sells for
nothing (`WGNoResaleComponent`). Flesh heals humanoids in reach and slowly raises the dead through Wolfmed's revival; anyone raised is
marked and rises as Remade the moment they die again. Reach shortens jumps and widens radar on the Scion's grid, and every jump adds
resonance whatever the dial. A sharp tool or welder on an anchored Scion chips off a shard: a power cell that never runs down, sells while
its Scion is quiet, and roots into small patches of corruption once its Scion reaches Growth, until it's cut free. A rooted Scion holds
Matter, Flesh and Reach at full, and a sanctuary grid bars every Gift.

Sector (`Sector/`). `WGRemadeDiscoveryRule`, carried by the Contested Sector and Escalation presets and gated by
`wg.encounters.enabled` and `wg.remade.sector.enabled`, now and then starts a `wgRemadeSite`: an unlabelled hulk, marked on the sector map, with a dormant
Scion wrenched to its deck, bare or guarded by the Choir (`WGChoir`, friendly to the Remade, with a ship that holds beside the hulk). A
garbled radio line gives a rough position. One wild Scion at a time, and none soon after one is lost or sold. A Scion that is moved,
unwrenched or drawn on shows on every radar and the sector map, wider with resonance (`WGRemadeSignalSystem`); the Choir hunt the ship
carrying it and pirate boarders are sent after it. `WGEncounterRemadeAdrift` is a stranded freighter at Harvest with crew aboard and a
mayday. Admins use `wg_remade spawn [site] [here] | resonance <value> [uid] | status`.

Open items. A Remade that hears something walks to it: hunting keeps its target past the first step, and Remade the Director owns
stay awake out to its 64 m radius. The Undying dies for good with its Monolith, stays down on a powered stasis bed, and doesn't re-form
after 150 heat damage while down. A twitcher's dodge pool works against beams, and a dodging Remade steps aside from a near miss. A hunter
or Undying shows a lost limb growing back. A brute flings whoever it hits, a lurker dies with no tentacles, and a divider's head turns a
body it kills or walks to a corpse. What a corpse becomes depends on its sex, armour, legs, poison and job
(`WGRemadeConversionSystem.Weights`). Nothing grows and no Gift works within `remade.sanctuary_radius` (500 m) of CC, the only sanctuary by default
(`remade.sanctuary_stations`, matched against a grid's `BecomesStation` id; `WGRemadeSanctuaryComponent` marks any other grid).

Counterplay (`Counterplay/`). Corruption burned away leaves its tile scorched for 10 minutes, and nothing grows back there
(`WGScorchSystem`, through `WGCorruptionGrowAttemptEvent`). Necrotoxin (`WGNecrotoxin`: mercury, sulfur and ammonia, or a
`WGNecrotoxinPen`) poisons Remade through their blood whatever their armour and barely hurts crew; a corpse with a unit of it in the
blood can't turn. The plasma cutter (`WGRemadeCutter`, autolathe) throws a short arc that takes limbs off in a few shots. A Monolith
rooted at the Crescendo becomes the Prime (`WGPrimeSystem`): nothing harms it but a nuclear detonation within twice the payload's
erase radius plus 16 m (`WGNuclearDetonatedEvent`, from the Warheads module), a verse every 10 minutes that announces and feeds the
Director, and a warrant on the PDA board paid to whoever set the nuke off, or shared among those nearby.
Destroying any Monolith leaves a husk and turns its Remade feral (`WGFeralSystem`). The Tap is an autolathe flatpack. Guidebook pages
are in `Resources/ServerInfo/_WG/Remade/Guidebook/`.

Critters (`Critters/`). A dead animal turns like dead crew: it needs its head if it has a body, can't have a mind in it, and
shakes and bursts the same way. A small one (under 30 kg) rises as a hopper and a big one as a lurker, set by `animalWeight` and the
mass range on each `wgRemadeMob`; necrotoxin protects animal corpses too. A hopper with nothing to hunt climbs onto the nearest table
and is drawn under it (`WGCoverSystem`, through the Rodentia sneak flag), its sight shrunk to 4 m until prey, a hunt order or a wound
brings it out. A leaper with nothing to hunt stands against the nearest wall (`WGWallClingSystem`); leaping from there reaches 4 tiles
further, winds up in 0.6 of the time and hits 1.25 times as hard.

Variants (`variants.yml`, `Variants/`). Most DS13 variants are another skin on one species, picked by weight, so each is an
entity parented to its base with its own sprite sheet. The desiccated and carrion slashers, flayed puker and hound lurker follow DS13's
shares, with the base weights cut to match. The flesh brute only has finished torso art, so its limbs don't come off. The fennec is a
tougher, faster hopper from Growth. A Remade steering for a target that hasn't moved 0.5 m in 3 s breaks the anchored thing in its way
with its normal melee (`WGStuckBreakerSystem`); the pathfinder can't see railings, so without this a Remade presses against one forever.
Only a brute or the Undying breaks a wall, and only after 6 s stuck with nothing else in the way.

Corruption coverage (`Corruption/`). A Monolith may cover a share of the floor it can walk to on its hull: a quarter at Growth, over
half at Harvest, all of it at Rapture, and half as much again onto docked ships at Crescendo; small hulls keep 64, 160 and 256 tiles.
Tiles spread faster as the stage climbs and cross shut doors, windoors and airlock chambers (`WGCorruptionSystem.Doors.cs`). Each tile
works out which of its eight neighbours join it and the client picks the matching DS13 state (`WGCorruptionVisualizerSystem`), so a
carpet reads as one piece. A wall that corruption touches gets a growth over it (`WGCorruptionWall`) that burns like the floor and goes
with the corruption or the wall. Blood on corruption is drunk at 0.5 units a second and pays the Director 0.1 biomass a unit.

Dread and Ward (`Dread/`, `Ward/`). `WGDreadSystem` sweeps every living player once a second. A stone at Hymn or above reaches crew
near it, falling off to nothing at 30 to 220 m by stage; at Rapture lightly and at the Crescendo more so, it reaches the whole sector.
A player gets whispers (a line and a DS13 voice), glimpses (a Remade shown on that client alone for 0.2 to 0.4 s in a dark spot at the
edge of its view) and a warped screen (`dread.swsl`: wobble, colour split, vignette, drain), all scaled by stage and stress. Reduced
motion or `wg.remade.dread.visuals` 0 turns off glimpses and the moving warp. The biohazard alarm plays three times back to back at a
lockdown and at the Crescendo (`WGDreadAlarmSystem`), with the Crescendo song played to the whole round. Ward is the fifth Gift dial:
the Scion feeds the stock ship shield emitter for free, far above any generator. A rooted stone's field can't be broken, and
`WGWardSystem` pushes any ship that isn't the stone's own or docked to it back to the field's edge, while people on foot cross freely.

Shards, the dark and the bed. A Scion's shard gives its parent's dread in a small way: whoever carries it feels a third to a half of
the stone's strength, and anyone within 4 m (6 m rooted) less; none of it near CC. From Harvest a whisper bed plays in the player's own
head (`WGDreadBedSystem`: three whisper loops that never line up, a heartbeat and a drone, scaled by `wg.remade.dread.bed`), with a floor
at Rapture and the Crescendo, and wordless murmurs overlap from Rapture. From Harvest `WGDarknessSystem` breaks the lights on the
stone's ship and anything docked to it, nearest first: 12% at Harvest, 45% at Rapture, 92% at the Crescendo. The sounds are DS13's
only, with the ghost moans, taunting voices and screech removed (`Tools/_WG/Remade/port_dread.py` cuts the whispers). The Ward's field
is drawn by the Remade module (`WGWardOverlay`, `ward.swsl`): a dark red veined membrane that pulses, brightens where it's hit and sets
hard and bright when rooted, with Marker glyphs drifting through it.

Signals (`Signals/`). A stone at Hymn or above offers a ghost role in the normal Ghost Roles window (`WGSignalGate`): one Signal per
stone at Hymn, rising to five at the Crescendo. A Signal is an unseen eye (`WGSignal`) that sees through walls and in the dark, leashed
to the stone's ship and anything docked to it, its corruption and its rooted shards. It spends Psi (100, regained 0.4 to 2.2 a second
by stage) on the Director's tricks: flicker, whisper, false sound and scrawl from Hymn, open or bolt a door from Growth (`WGSignalMath`).
For 20 Psi it takes over a Remade of its stone within 8 m, whose thinking stops while the player uses its abilities as actions; the
Undying and Remade the Director is busy with can't be taken. Nothing works within the sanctuary, and a stone that is destroyed, quiet,
barred or switched off with `wg.remade.signals.enabled` lets its Signals go as ghosts. Everything a Signal does is admin-logged.

Environment (`Environment/`, `Morgue/`, `Vents/`, `Garrison/`). As a stone climbs it seeds plain sprites over its corruption: roots,
sacs, organs, bone spurs and gore on the floor, remains fused into walls, veins over doors and vents, glyph screens on consoles and mist
from Rapture, within a budget of about a quarter of its tiles, all gone with the corruption. A body in a morgue tray, body bag, cryo pod
or stasis bed on a ship with a stone at Hymn or above turns in 14–24 s, knocking first, then the tray bursts open; a loaded tray draws an
Infector. Each stone sends a small Remade through the vents every few minutes, rattling the grilles it passes; some lie in wait and leap
at a crewman within 3 m, denting the vent. Remade drag unwatched corpses into the vents and set them down in a tray, by a nest or on the
corruption, and a share of each wave lies as fake bodies. Each stone keeps a garrison of 2, 4, 7 and 10 guards by stage, with elites from
Rapture, paid from its own credit and replaced quickly; damage or a tool on the stone calls every Remade within 40 m onto the attacker.
`remade.garrison_enabled` turns the garrison off.

Betrayal (`Betrayal/`). The stone turns what the crew trust against them. A crewman who turns calls out one of their own last five chat
lines in their name every one to three minutes when crew are near (`WGBetrayalVoiceSystem`). Their headset pleads garbled over its own
channels from where they died a few times, and their suit sensor keeps reading alive on the crew monitor (`WGSensorGhostSystem`). When a
Director goes quiet to stalk, ambient sound, music and the whisper bed fade until a Remade first sees a player in the Peak, who gets a
DS13 stinger; under a stone a low score gains a layer with each step of resonance (`WGBetrayalScoreSystem`, `WGBetrayalMusicSystem`).
Players alone hear knocking behind doors, scraping in walls, breath at their back and footsteps that stop when they stop, and see
crewmates as Remade for a split second (`WGBetrayalFearSystem`, `WGBetrayalSensesSystem`, `WGBetrayalFlickerSystem`). Past stress 40 a
player gets a shaking aim, dropped items, nosebleeds, twitches, false announcements and PDA messages from the dead. From Growth the dead
near a stone shift only when nobody can see them (`WGRestlessCorpseSystem`). Client settings `wg.remade.betrayal.music`,
`wg.remade.betrayal.sounds`; server `wg.remade.betrayal.rate`. Audio is built by `Tools/_WG/Remade/port_betrayal.py`.

Build-up and omens (`Closets/`, `Echoes/`, `Omens/`). A stone's first Growth stays quiet for 150-210 s: no wave, stalker, ambush,
garrison or morgue burst (morgue bodies under a Hymn or building stone only knock). Part way through, one Remade follows a crewman and
slips into a vent once seen. The Director shuts Remade in empty lockers that breathe and scrape, and open on whoever opens the locker
or stands beside it. A crewman hiding in a locker drops out of its sight until they talk in there; then a Remade beside the locker tears
it open. A crewman who dies near a stone calls once on their own headset a minute or two later, naming the nearest beacon, and a fake
body is laid beside theirs (`WGRemadeEchoSystem`). These are real, shared sounds, unlike Betrayal's private ones: knocks on a shut door
near the focus (from Harvest, sometimes with a Remade behind it), scraping that moves along a wall, a Remade in front of a camera
someone is watching, and lights stuttering before each wave. Each stage climb plays a sting to every player in the round, at most one
per 45 s; the stings are mixed by `Tools/_WG/Remade/make_stage_stings.py` from a tgstation checkout.

The mender (`WGRemadeMender`, `WGReclaimSystem`, `mender.rsi` from `Tools/_WG/Remade/make_mender.py`) is a bleached hunter that
grows nothing back. Its limbs fly a few metres when they come off, and with three off it goes down. After 3 s thin tentacles
(`WGReclaimTentacleOverlay`, client) reach out to every severed limb within 6 m and drag it back at about a metre a second; each knits
back on, and once whole it gets up. A limb burnt to ash or blown apart, or held or carried out of reach for 8 s, leaves it down for
good, as does burning the body while it lies there. Severed limbs of any Remade now burn. It is a rare Rapture elite, one at a time.

The torment smite (`Torment/`) is an admin toggle under Smite on any mob: the player gets every whisper, glimpse, voice, warp, false
sense, mind trick, the bed and the full score as if a Crescendo stone stood beside them, wherever they are, and nobody else does.
Biohazard lockdown, beside it under Smite or `wg_remade lockdown <player or uid> [seconds] [range]`, bolts the powered doors round them
(9 m, a minute by default) under the biohazard alarm, the Director's lockdown with no stone needed.

Outbreaks, splinters and clearing (`Outbreaks/`, `splinter.yml`, `Sector/WGRemadeClearSystem.cs`). A stone at the Crescendo breaks
out on another crewed ship every two to three and a half minutes: two or three hoppers and slashers come out of an unseen vent near
someone aboard, and a shard of the stone roots there, so the dead aboard turn too. It all goes with the stone. `WGMonolithSplinter`
is the small event for one ship: an admin spawns it on a deck, it wakes at the Hymn and climbs on its own to the Harvest and no
further, its dread, score and stings stay off every other grid, and it has no Signals; it breaks at 800 damage. Away from a stone the sector
gets the score and a quieter bed rather than voices: whispers, murmurs and Betrayal's knocks only reach crew within the stone's own
radius. A turning corpse drops its gear and nothing else, and a gibbed Remade leaves no organs or parts (`WGRemadeGibSystem`). `wg_remade clear` deletes every stone, shard,
Remade, growth, corruption tile, Signal and stone sound and tells every client to stop the bed and score, as the last stone going
does on its own. `wg_remade outbreak [uid]` forces an outbreak.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Remade/Abilities/WGAbility.cs`](Abilities/WGAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGAbilitySystem.Conversion.cs`](Abilities/WGAbilitySystem.Conversion.cs)
- [`Content.Server/_WG/Remade/Abilities/WGAbilitySystem.cs`](Abilities/WGAbilitySystem.cs)
- [`Content.Server/_WG/Remade/Abilities/WGAbilitySystem.Elites.cs`](Abilities/WGAbilitySystem.Elites.cs)
- [`Content.Server/_WG/Remade/Abilities/WGAbilitySystem.Roster.cs`](Abilities/WGAbilitySystem.Roster.cs)
- [`Content.Server/_WG/Remade/Abilities/WGBlinkAbility.cs`](Abilities/WGBlinkAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGChargeAbility.cs`](Abilities/WGChargeAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGDodgeAbility.cs`](Abilities/WGDodgeAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGExplodeAbility.cs`](Abilities/WGExplodeAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGLatchAbility.cs`](Abilities/WGLatchAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGLeapAbility.cs`](Abilities/WGLeapAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGPounceAbility.cs`](Abilities/WGPounceAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGRamAbility.cs`](Abilities/WGRamAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGRemadeAbilitiesComponent.cs`](Abilities/WGRemadeAbilitiesComponent.cs)
- [`Content.Server/_WG/Remade/Abilities/WGShootAbility.cs`](Abilities/WGShootAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGShoutAbility.cs`](Abilities/WGShoutAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGSpineAbility.cs`](Abilities/WGSpineAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGSwingAbility.cs`](Abilities/WGSwingAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGTailStabAbility.cs`](Abilities/WGTailStabAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGTauntAbility.cs`](Abilities/WGTauntAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGTongueAbility.cs`](Abilities/WGTongueAbility.cs)
- [`Content.Server/_WG/Remade/Abilities/WGVomitAbility.cs`](Abilities/WGVomitAbility.cs)
- [`Content.Server/_WG/Remade/AI/WGPickCorpseOperator.cs`](AI/WGPickCorpseOperator.cs)
- [`Content.Server/_WG/Remade/AI/WGReanimateOperator.cs`](AI/WGReanimateOperator.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGBetrayalComponents.cs`](Betrayal/WGBetrayalComponents.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGBetrayalFearSystem.cs`](Betrayal/WGBetrayalFearSystem.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGBetrayalFearSystem.Mind.cs`](Betrayal/WGBetrayalFearSystem.Mind.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGBetrayalScoreSystem.cs`](Betrayal/WGBetrayalScoreSystem.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGBetrayalVoiceSystem.cs`](Betrayal/WGBetrayalVoiceSystem.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGRestlessCorpseSystem.cs`](Betrayal/WGRestlessCorpseSystem.cs)
- [`Content.Server/_WG/Remade/Betrayal/WGSensorGhostSystem.cs`](Betrayal/WGSensorGhostSystem.cs)
- [`Content.Server/_WG/Remade/Closets/WGClosetComponents.cs`](Closets/WGClosetComponents.cs)
- [`Content.Server/_WG/Remade/Closets/WGRemadeDirectorSystem.Closets.cs`](Closets/WGRemadeDirectorSystem.Closets.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeComponent.cs`](Conversion/WGRemadeComponent.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeConversionSystem.Conditions.cs`](Conversion/WGRemadeConversionSystem.Conditions.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeConversionSystem.cs`](Conversion/WGRemadeConversionSystem.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeGibSystem.cs`](Conversion/WGRemadeGibSystem.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeVoiceComponent.cs`](Conversion/WGRemadeVoiceComponent.cs)
- [`Content.Server/_WG/Remade/Conversion/WGRemadeVoiceSystem.cs`](Conversion/WGRemadeVoiceSystem.cs)
- [`Content.Server/_WG/Remade/Conversion/WGTakesBodyComponent.cs`](Conversion/WGTakesBodyComponent.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionComponent.cs`](Corruption/WGCorruptionComponent.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Blood.cs`](Corruption/WGCorruptionSystem.Blood.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.cs`](Corruption/WGCorruptionSystem.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Doors.cs`](Corruption/WGCorruptionSystem.Doors.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Loose.cs`](Corruption/WGCorruptionSystem.Loose.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Nodes.cs`](Corruption/WGCorruptionSystem.Nodes.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Sanctuary.cs`](Corruption/WGCorruptionSystem.Sanctuary.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionSystem.Smoothing.cs`](Corruption/WGCorruptionSystem.Smoothing.cs)
- [`Content.Server/_WG/Remade/Corruption/WGCorruptionWallComponent.cs`](Corruption/WGCorruptionWallComponent.cs)
- [`Content.Server/_WG/Remade/Corruption/WGRemadeSanctuaryComponent.cs`](Corruption/WGRemadeSanctuaryComponent.cs)
- [`Content.Server/_WG/Remade/Corruption/WGRemadeSanctuaryCVars.cs`](Corruption/WGRemadeSanctuaryCVars.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGCounterplayEvents.cs`](Counterplay/WGCounterplayEvents.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGFeralComponent.cs`](Counterplay/WGFeralComponent.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGFeralSystem.cs`](Counterplay/WGFeralSystem.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGNecrotoxinProtectedComponent.cs`](Counterplay/WGNecrotoxinProtectedComponent.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGNecrotoxinSystem.cs`](Counterplay/WGNecrotoxinSystem.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGPrimeComponent.cs`](Counterplay/WGPrimeComponent.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGPrimeSystem.cs`](Counterplay/WGPrimeSystem.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGScorchedGridComponent.cs`](Counterplay/WGScorchedGridComponent.cs)
- [`Content.Server/_WG/Remade/Counterplay/WGScorchSystem.cs`](Counterplay/WGScorchSystem.cs)
- [`Content.Server/_WG/Remade/Critters/WGAbilitySystem.Critters.cs`](Critters/WGAbilitySystem.Critters.cs)
- [`Content.Server/_WG/Remade/Critters/WGCoverSystem.cs`](Critters/WGCoverSystem.cs)
- [`Content.Server/_WG/Remade/Critters/WGCritterComponents.cs`](Critters/WGCritterComponents.cs)
- [`Content.Server/_WG/Remade/Critters/WGCritterOperators.cs`](Critters/WGCritterOperators.cs)
- [`Content.Server/_WG/Remade/Critters/WGRemadeConversionSystem.Animals.cs`](Critters/WGRemadeConversionSystem.Animals.cs)
- [`Content.Server/_WG/Remade/Critters/WGWallClingSystem.cs`](Critters/WGWallClingSystem.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeBehaviourComponents.cs`](Director/WGRemadeBehaviourComponents.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorComponent.cs`](Director/WGRemadeDirectorComponent.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Behaviour.cs`](Director/WGRemadeDirectorSystem.Behaviour.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Build.cs`](Director/WGRemadeDirectorSystem.Build.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.cs`](Director/WGRemadeDirectorSystem.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Dead.cs`](Director/WGRemadeDirectorSystem.Dead.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Dread.cs`](Director/WGRemadeDirectorSystem.Dread.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Hearing.cs`](Director/WGRemadeDirectorSystem.Hearing.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Speech.cs`](Director/WGRemadeDirectorSystem.Speech.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Tension.cs`](Director/WGRemadeDirectorSystem.Tension.cs)
- [`Content.Server/_WG/Remade/Director/WGRemadeDirectorSystem.Vents.cs`](Director/WGRemadeDirectorSystem.Vents.cs)
- [`Content.Server/_WG/Remade/Dread/WGDarknessSystem.cs`](Dread/WGDarknessSystem.cs)
- [`Content.Server/_WG/Remade/Dread/WGDreadAlarmSystem.cs`](Dread/WGDreadAlarmSystem.cs)
- [`Content.Server/_WG/Remade/Dread/WGDreadSystem.cs`](Dread/WGDreadSystem.cs)
- [`Content.Server/_WG/Remade/Echoes/WGRemadeDirectorSystem.Echoes.cs`](Echoes/WGRemadeDirectorSystem.Echoes.cs)
- [`Content.Server/_WG/Remade/Echoes/WGRemadeEchoSystem.cs`](Echoes/WGRemadeEchoSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGBruteArmourComponent.cs`](Elites/WGBruteArmourComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGBruteSystem.cs`](Elites/WGBruteSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGDividerComponent.cs`](Elites/WGDividerComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGDividerSystem.cs`](Elites/WGDividerSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGDodgePoolComponent.cs`](Elites/WGDodgePoolComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGDodgePoolSystem.cs`](Elites/WGDodgePoolSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGEliteSystem.cs`](Elites/WGEliteSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGFlingOnHitComponent.cs`](Elites/WGFlingOnHitComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGReanimateHasteComponent.cs`](Elites/WGReanimateHasteComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGReanimateHasteSystem.cs`](Elites/WGReanimateHasteSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGReclaimComponent.cs`](Elites/WGReclaimComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGReclaimSystem.cs`](Elites/WGReclaimSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGRegenComponent.cs`](Elites/WGRegenComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGRegenSystem.cs`](Elites/WGRegenSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGRemadeLimbsSystem.Regrow.cs`](Elites/WGRemadeLimbsSystem.Regrow.cs)
- [`Content.Server/_WG/Remade/Elites/WGRisesComponent.cs`](Elites/WGRisesComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGRisesSystem.cs`](Elites/WGRisesSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGTauntBuffComponent.cs`](Elites/WGTauntBuffComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGTauntSystem.cs`](Elites/WGTauntSystem.cs)
- [`Content.Server/_WG/Remade/Elites/WGUndyingComponent.cs`](Elites/WGUndyingComponent.cs)
- [`Content.Server/_WG/Remade/Elites/WGUndyingSystem.cs`](Elites/WGUndyingSystem.cs)
- [`Content.Server/_WG/Remade/Environment/WGEnvDecorComponents.cs`](Environment/WGEnvDecorComponents.cs)
- [`Content.Server/_WG/Remade/Environment/WGEnvDecorSystem.cs`](Environment/WGEnvDecorSystem.cs)
- [`Content.Server/_WG/Remade/Garrison/WGGarrisonComponents.cs`](Garrison/WGGarrisonComponents.cs)
- [`Content.Server/_WG/Remade/Garrison/WGGarrisonCVars.cs`](Garrison/WGGarrisonCVars.cs)
- [`Content.Server/_WG/Remade/Garrison/WGGarrisonSystem.cs`](Garrison/WGGarrisonSystem.cs)
- [`Content.Server/_WG/Remade/Garrison/WGRemadeDirectorSystem.Garrison.cs`](Garrison/WGRemadeDirectorSystem.Garrison.cs)
- [`Content.Server/_WG/Remade/Gifts/WGCorruptionSystem.Shards.cs`](Gifts/WGCorruptionSystem.Shards.cs)
- [`Content.Server/_WG/Remade/Gifts/WGFleshComponents.cs`](Gifts/WGFleshComponents.cs)
- [`Content.Server/_WG/Remade/Gifts/WGFleshSystem.cs`](Gifts/WGFleshSystem.cs)
- [`Content.Server/_WG/Remade/Gifts/WGMatterComponents.cs`](Gifts/WGMatterComponents.cs)
- [`Content.Server/_WG/Remade/Gifts/WGMatterSystem.cs`](Gifts/WGMatterSystem.cs)
- [`Content.Server/_WG/Remade/Gifts/WGReachSystem.cs`](Gifts/WGReachSystem.cs)
- [`Content.Server/_WG/Remade/Gifts/WGShardComponent.cs`](Gifts/WGShardComponent.cs)
- [`Content.Server/_WG/Remade/Gifts/WGShardSystem.cs`](Gifts/WGShardSystem.cs)
- [`Content.Server/_WG/Remade/Limbs/WGRemadeLimbsSystem.cs`](Limbs/WGRemadeLimbsSystem.cs)
- [`Content.Server/_WG/Remade/Monolith/WGMonolithComponent.cs`](Monolith/WGMonolithComponent.cs)
- [`Content.Server/_WG/Remade/Monolith/WGMonolithSystem.cs`](Monolith/WGMonolithSystem.cs)
- [`Content.Server/_WG/Remade/Monolith/WGMonolithTapSystem.cs`](Monolith/WGMonolithTapSystem.cs)
- [`Content.Server/_WG/Remade/Morgue/WGMorgueComponents.cs`](Morgue/WGMorgueComponents.cs)
- [`Content.Server/_WG/Remade/Morgue/WGMorgueSystem.cs`](Morgue/WGMorgueSystem.cs)
- [`Content.Server/_WG/Remade/Morgue/WGRemadeDirectorSystem.Fakes.cs`](Morgue/WGRemadeDirectorSystem.Fakes.cs)
- [`Content.Server/_WG/Remade/Morgue/WGRemadeDirectorSystem.Morgue.cs`](Morgue/WGRemadeDirectorSystem.Morgue.cs)
- [`Content.Server/_WG/Remade/Omens/WGRemadeDirectorSystem.Omens.cs`](Omens/WGRemadeDirectorSystem.Omens.cs)
- [`Content.Server/_WG/Remade/Outbreaks/WGRemadeDirectorSystem.Outbreaks.cs`](Outbreaks/WGRemadeDirectorSystem.Outbreaks.cs)
- [`Content.Server/_WG/Remade/Roster/WGDiesWithoutLimbsComponent.cs`](Roster/WGDiesWithoutLimbsComponent.cs)
- [`Content.Server/_WG/Remade/Roster/WGExploderSystem.cs`](Roster/WGExploderSystem.cs)
- [`Content.Server/_WG/Remade/Roster/WGLimbDeathSystem.cs`](Roster/WGLimbDeathSystem.cs)
- [`Content.Server/_WG/Remade/Roster/WGRemadeExploderComponent.cs`](Roster/WGRemadeExploderComponent.cs)
- [`Content.Server/_WG/Remade/Roster/WGRemadeShellComponent.cs`](Roster/WGRemadeShellComponent.cs)
- [`Content.Server/_WG/Remade/Roster/WGRosterSystem.cs`](Roster/WGRosterSystem.cs)
- [`Content.Server/_WG/Remade/Roster/WGShellSystem.cs`](Roster/WGShellSystem.cs)
- [`Content.Server/_WG/Remade/Roster/WGSkirmishOperator.cs`](Roster/WGSkirmishOperator.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeClearSystem.cs`](Sector/WGRemadeClearSystem.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeCommand.cs`](Sector/WGRemadeCommand.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeDiscoveryRuleComponent.cs`](Sector/WGRemadeDiscoveryRuleComponent.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeDiscoveryRuleSystem.cs`](Sector/WGRemadeDiscoveryRuleSystem.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeSignalSystem.cs`](Sector/WGRemadeSignalSystem.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeSiteComponent.cs`](Sector/WGRemadeSiteComponent.cs)
- [`Content.Server/_WG/Remade/Sector/WGRemadeSiteSystem.cs`](Sector/WGRemadeSiteSystem.cs)
- [`Content.Server/_WG/Remade/Sector/WGScionSignalComponent.cs`](Sector/WGScionSignalComponent.cs)
- [`Content.Server/_WG/Remade/Siege/WGSiegeComponents.cs`](Siege/WGSiegeComponents.cs)
- [`Content.Server/_WG/Remade/Siege/WGSiegeCVars.cs`](Siege/WGSiegeCVars.cs)
- [`Content.Server/_WG/Remade/Siege/WGSiegeSystem.cs`](Siege/WGSiegeSystem.cs)
- [`Content.Server/_WG/Remade/Signals/WGAbilitySystem.Signals.cs`](Signals/WGAbilitySystem.Signals.cs)
- [`Content.Server/_WG/Remade/Signals/WGRemadeDirectorSystem.Signals.cs`](Signals/WGRemadeDirectorSystem.Signals.cs)
- [`Content.Server/_WG/Remade/Signals/WGSignalComponents.cs`](Signals/WGSignalComponents.cs)
- [`Content.Server/_WG/Remade/Signals/WGSignalSystem.cs`](Signals/WGSignalSystem.cs)
- [`Content.Server/_WG/Remade/Signals/WGSignalSystem.Leash.cs`](Signals/WGSignalSystem.Leash.cs)
- [`Content.Server/_WG/Remade/Signals/WGSignalSystem.Possession.cs`](Signals/WGSignalSystem.Possession.cs)
- [`Content.Server/_WG/Remade/Signals/WGSignalSystem.Tricks.cs`](Signals/WGSignalSystem.Tricks.cs)
- [`Content.Server/_WG/Remade/Structures/WGCystSystem.cs`](Structures/WGCystSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGEyeSystem.cs`](Structures/WGEyeSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGGrowthComponents.cs`](Structures/WGGrowthComponents.cs)
- [`Content.Server/_WG/Remade/Structures/WGGrowthSystem.cs`](Structures/WGGrowthSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGHarvesterSystem.cs`](Structures/WGHarvesterSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGMawSystem.cs`](Structures/WGMawSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGNestSystem.cs`](Structures/WGNestSystem.cs)
- [`Content.Server/_WG/Remade/Structures/WGSnareSystem.cs`](Structures/WGSnareSystem.cs)
- [`Content.Server/_WG/Remade/Torment/WGTormentedComponent.cs`](Torment/WGTormentedComponent.cs)
- [`Content.Server/_WG/Remade/Torment/WGTormentSystem.cs`](Torment/WGTormentSystem.cs)
- [`Content.Server/_WG/Remade/Variants/WGStuckBreakerComponent.cs`](Variants/WGStuckBreakerComponent.cs)
- [`Content.Server/_WG/Remade/Variants/WGStuckBreakerSystem.cs`](Variants/WGStuckBreakerSystem.cs)
- [`Content.Server/_WG/Remade/Vents/WGAbilitySystem.Ambush.cs`](Vents/WGAbilitySystem.Ambush.cs)
- [`Content.Server/_WG/Remade/Vents/WGRemadeDirectorSystem.Haul.cs`](Vents/WGRemadeDirectorSystem.Haul.cs)
- [`Content.Server/_WG/Remade/Vents/WGRemadeDirectorSystem.VentRun.cs`](Vents/WGRemadeDirectorSystem.VentRun.cs)
- [`Content.Server/_WG/Remade/Vents/WGVentComponents.cs`](Vents/WGVentComponents.cs)
- [`Content.Server/_WG/Remade/Vents/WGVentSystem.cs`](Vents/WGVentSystem.cs)
- [`Content.Server/_WG/Remade/Ward/WGWardComponents.cs`](Ward/WGWardComponents.cs)
- [`Content.Server/_WG/Remade/Ward/WGWardSystem.cs`](Ward/WGWardSystem.cs)

### Shared

- [`Content.Shared/_WG/Remade/Betrayal/WGBetrayalShared.cs`](../../../Content.Shared/_WG/Remade/Betrayal/WGBetrayalShared.cs)
- [`Content.Shared/_WG/Remade/Betrayal/WGShakenAim.cs`](../../../Content.Shared/_WG/Remade/Betrayal/WGShakenAim.cs)
- [`Content.Shared/_WG/Remade/Corruption/WGCorruptionVisuals.cs`](../../../Content.Shared/_WG/Remade/Corruption/WGCorruptionVisuals.cs)
- [`Content.Shared/_WG/Remade/Dread/WGDreadCVars.cs`](../../../Content.Shared/_WG/Remade/Dread/WGDreadCVars.cs)
- [`Content.Shared/_WG/Remade/Dread/WGDreadEvents.cs`](../../../Content.Shared/_WG/Remade/Dread/WGDreadEvents.cs)
- [`Content.Shared/_WG/Remade/Dread/WGDreadMath.cs`](../../../Content.Shared/_WG/Remade/Dread/WGDreadMath.cs)
- [`Content.Shared/_WG/Remade/Limbs/SharedWGRemadeLimbsSystem.cs`](../../../Content.Shared/_WG/Remade/Limbs/SharedWGRemadeLimbsSystem.cs)
- [`Content.Shared/_WG/Remade/Limbs/WGRemadeLimbsComponent.cs`](../../../Content.Shared/_WG/Remade/Limbs/WGRemadeLimbsComponent.cs)
- [`Content.Shared/_WG/Remade/Sector/WGRemadeSectorCVars.cs`](../../../Content.Shared/_WG/Remade/Sector/WGRemadeSectorCVars.cs)
- [`Content.Shared/_WG/Remade/Sector/WGRemadeSitePrototype.cs`](../../../Content.Shared/_WG/Remade/Sector/WGRemadeSitePrototype.cs)
- [`Content.Shared/_WG/Remade/Signals/WGSignalComponent.cs`](../../../Content.Shared/_WG/Remade/Signals/WGSignalComponent.cs)
- [`Content.Shared/_WG/Remade/Signals/WGSignalCVars.cs`](../../../Content.Shared/_WG/Remade/Signals/WGSignalCVars.cs)
- [`Content.Shared/_WG/Remade/Signals/WGSignalEvents.cs`](../../../Content.Shared/_WG/Remade/Signals/WGSignalEvents.cs)
- [`Content.Shared/_WG/Remade/Signals/WGSignalMath.cs`](../../../Content.Shared/_WG/Remade/Signals/WGSignalMath.cs)
- [`Content.Shared/_WG/Remade/Structures/WGGrowthShared.cs`](../../../Content.Shared/_WG/Remade/Structures/WGGrowthShared.cs)
- [`Content.Shared/_WG/Remade/Ward/WGWardFieldComponent.cs`](../../../Content.Shared/_WG/Remade/Ward/WGWardFieldComponent.cs)
- [`Content.Shared/_WG/Remade/WGHeavyPull.cs`](../../../Content.Shared/_WG/Remade/WGHeavyPull.cs)
- [`Content.Shared/_WG/Remade/WGMonolithMath.cs`](../../../Content.Shared/_WG/Remade/WGMonolithMath.cs)
- [`Content.Shared/_WG/Remade/WGMonolithTapComponent.cs`](../../../Content.Shared/_WG/Remade/WGMonolithTapComponent.cs)
- [`Content.Shared/_WG/Remade/WGMonolithTapDropSystem.cs`](../../../Content.Shared/_WG/Remade/WGMonolithTapDropSystem.cs)
- [`Content.Shared/_WG/Remade/WGMonolithUi.cs`](../../../Content.Shared/_WG/Remade/WGMonolithUi.cs)
- [`Content.Shared/_WG/Remade/WGRemadeAppearsDeadComponent.cs`](../../../Content.Shared/_WG/Remade/WGRemadeAppearsDeadComponent.cs)
- [`Content.Shared/_WG/Remade/WGRemadeAppearsDeadSystem.cs`](../../../Content.Shared/_WG/Remade/WGRemadeAppearsDeadSystem.cs)
- [`Content.Shared/_WG/Remade/WGRemadeMobPrototype.cs`](../../../Content.Shared/_WG/Remade/WGRemadeMobPrototype.cs)
- [`Content.Shared/_WG/Remade/WGRemadeShellVisuals.cs`](../../../Content.Shared/_WG/Remade/WGRemadeShellVisuals.cs)

### Client

- [`Content.Client/_WG/Remade/Betrayal/AmbientSoundSystem.Betrayal.cs`](../../../Content.Client/_WG/Remade/Betrayal/AmbientSoundSystem.Betrayal.cs)
- [`Content.Client/_WG/Remade/Betrayal/ContentAudioSystem.Betrayal.cs`](../../../Content.Client/_WG/Remade/Betrayal/ContentAudioSystem.Betrayal.cs)
- [`Content.Client/_WG/Remade/Betrayal/WGBetrayalFlickerSystem.cs`](../../../Content.Client/_WG/Remade/Betrayal/WGBetrayalFlickerSystem.cs)
- [`Content.Client/_WG/Remade/Betrayal/WGBetrayalMusicSystem.cs`](../../../Content.Client/_WG/Remade/Betrayal/WGBetrayalMusicSystem.cs)
- [`Content.Client/_WG/Remade/Betrayal/WGBetrayalSensesSystem.cs`](../../../Content.Client/_WG/Remade/Betrayal/WGBetrayalSensesSystem.cs)
- [`Content.Client/_WG/Remade/Betrayal/WGDreadBedSystem.Betrayal.cs`](../../../Content.Client/_WG/Remade/Betrayal/WGDreadBedSystem.Betrayal.cs)
- [`Content.Client/_WG/Remade/Corruption/WGCorruptionVisualizerSystem.cs`](../../../Content.Client/_WG/Remade/Corruption/WGCorruptionVisualizerSystem.cs)
- [`Content.Client/_WG/Remade/Dread/WGDreadBedSystem.cs`](../../../Content.Client/_WG/Remade/Dread/WGDreadBedSystem.cs)
- [`Content.Client/_WG/Remade/Dread/WGDreadGlimpseSystem.cs`](../../../Content.Client/_WG/Remade/Dread/WGDreadGlimpseSystem.cs)
- [`Content.Client/_WG/Remade/Dread/WGDreadOverlay.cs`](../../../Content.Client/_WG/Remade/Dread/WGDreadOverlay.cs)
- [`Content.Client/_WG/Remade/Dread/WGDreadVisionSystem.cs`](../../../Content.Client/_WG/Remade/Dread/WGDreadVisionSystem.cs)
- [`Content.Client/_WG/Remade/Dread/WGRemadeClearSystem.cs`](../../../Content.Client/_WG/Remade/Dread/WGRemadeClearSystem.cs)
- [`Content.Client/_WG/Remade/Limbs/WGReclaimTentacleOverlay.cs`](../../../Content.Client/_WG/Remade/Limbs/WGReclaimTentacleOverlay.cs)
- [`Content.Client/_WG/Remade/Limbs/WGReclaimTentacleSystem.cs`](../../../Content.Client/_WG/Remade/Limbs/WGReclaimTentacleSystem.cs)
- [`Content.Client/_WG/Remade/Limbs/WGRemadeLimbVisualizerSystem.cs`](../../../Content.Client/_WG/Remade/Limbs/WGRemadeLimbVisualizerSystem.cs)
- [`Content.Client/_WG/Remade/Signals/WGSignalHud.cs`](../../../Content.Client/_WG/Remade/Signals/WGSignalHud.cs)
- [`Content.Client/_WG/Remade/Signals/WGSignalOverlay.cs`](../../../Content.Client/_WG/Remade/Signals/WGSignalOverlay.cs)
- [`Content.Client/_WG/Remade/Signals/WGSignalVisionSystem.cs`](../../../Content.Client/_WG/Remade/Signals/WGSignalVisionSystem.cs)
- [`Content.Client/_WG/Remade/Ward/WGWardGlyphOverlay.cs`](../../../Content.Client/_WG/Remade/Ward/WGWardGlyphOverlay.cs)
- [`Content.Client/_WG/Remade/Ward/WGWardOverlay.cs`](../../../Content.Client/_WG/Remade/Ward/WGWardOverlay.cs)
- [`Content.Client/_WG/Remade/Ward/WGWardOverlaySystem.cs`](../../../Content.Client/_WG/Remade/Ward/WGWardOverlaySystem.cs)
- [`Content.Client/_WG/Remade/WGGlyphButton.cs`](../../../Content.Client/_WG/Remade/WGGlyphButton.cs)
- [`Content.Client/_WG/Remade/WGMonolithTapBoundUserInterface.cs`](../../../Content.Client/_WG/Remade/WGMonolithTapBoundUserInterface.cs)
- [`Content.Client/_WG/Remade/WGMonolithTapWindow.xaml`](../../../Content.Client/_WG/Remade/WGMonolithTapWindow.xaml)
- [`Content.Client/_WG/Remade/WGMonolithTapWindow.xaml.cs`](../../../Content.Client/_WG/Remade/WGMonolithTapWindow.xaml.cs)
- [`Content.Client/_WG/Remade/WGMonolithTheme.cs`](../../../Content.Client/_WG/Remade/WGMonolithTheme.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Remade/AbilitiesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/AbilitiesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/BetrayalTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/BetrayalTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/CorruptionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/CorruptionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/CounterplayTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/CounterplayTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/CrittersTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/CrittersTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/DirectorBehaviourTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/DirectorBehaviourTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/DreadTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/DreadTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/ElitesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/ElitesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/EnvironmentTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/EnvironmentTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/GiftsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/GiftsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/LimbsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/LimbsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/MenderTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/MenderTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/OmensTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/OmensTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/OpenItemsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/OpenItemsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/OutbreakTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/OutbreakTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/RosterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/RosterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/SectorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/SectorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/SiegeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/SiegeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/SignalsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/SignalsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/StructuresTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/StructuresTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/SyntheticConversionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/SyntheticConversionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/TormentTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/TormentTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/VariantsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/VariantsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/WardTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/WardTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/WGRemadeDirectorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/WGRemadeDirectorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Remade/WGRemadeSliceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Remade/WGRemadeSliceTest.cs)

### Unit tests

- [`Content.Tests/_WG/Remade/WGDreadMathTest.cs`](../../../Content.Tests/_WG/Remade/WGDreadMathTest.cs)
- [`Content.Tests/_WG/Remade/WGGiftsMathTest.cs`](../../../Content.Tests/_WG/Remade/WGGiftsMathTest.cs)
- [`Content.Tests/_WG/Remade/WGMonolithMathTest.cs`](../../../Content.Tests/_WG/Remade/WGMonolithMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Remade/betrayal.yml`](../../../Resources/Prototypes/_WG/Remade/betrayal.yml)
- [`Resources/Prototypes/_WG/Remade/corruption.yml`](../../../Resources/Prototypes/_WG/Remade/corruption.yml)
- [`Resources/Prototypes/_WG/Remade/counterplay.yml`](../../../Resources/Prototypes/_WG/Remade/counterplay.yml)
- [`Resources/Prototypes/_WG/Remade/critters.yml`](../../../Resources/Prototypes/_WG/Remade/critters.yml)
- [`Resources/Prototypes/_WG/Remade/director.yml`](../../../Resources/Prototypes/_WG/Remade/director.yml)
- [`Resources/Prototypes/_WG/Remade/dread.yml`](../../../Resources/Prototypes/_WG/Remade/dread.yml)
- [`Resources/Prototypes/_WG/Remade/elites.yml`](../../../Resources/Prototypes/_WG/Remade/elites.yml)
- [`Resources/Prototypes/_WG/Remade/environment.yml`](../../../Resources/Prototypes/_WG/Remade/environment.yml)
- [`Resources/Prototypes/_WG/Remade/gifts.yml`](../../../Resources/Prototypes/_WG/Remade/gifts.yml)
- [`Resources/Prototypes/_WG/Remade/mobs.yml`](../../../Resources/Prototypes/_WG/Remade/mobs.yml)
- [`Resources/Prototypes/_WG/Remade/monolith.yml`](../../../Resources/Prototypes/_WG/Remade/monolith.yml)
- [`Resources/Prototypes/_WG/Remade/openitems.yml`](../../../Resources/Prototypes/_WG/Remade/openitems.yml)
- [`Resources/Prototypes/_WG/Remade/roster.yml`](../../../Resources/Prototypes/_WG/Remade/roster.yml)
- [`Resources/Prototypes/_WG/Remade/sector.yml`](../../../Resources/Prototypes/_WG/Remade/sector.yml)
- [`Resources/Prototypes/_WG/Remade/siege.yml`](../../../Resources/Prototypes/_WG/Remade/siege.yml)
- [`Resources/Prototypes/_WG/Remade/signals.yml`](../../../Resources/Prototypes/_WG/Remade/signals.yml)
- [`Resources/Prototypes/_WG/Remade/sounds.yml`](../../../Resources/Prototypes/_WG/Remade/sounds.yml)
- [`Resources/Prototypes/_WG/Remade/splinter.yml`](../../../Resources/Prototypes/_WG/Remade/splinter.yml)
- [`Resources/Prototypes/_WG/Remade/structures.yml`](../../../Resources/Prototypes/_WG/Remade/structures.yml)
- [`Resources/Prototypes/_WG/Remade/variants.yml`](../../../Resources/Prototypes/_WG/Remade/variants.yml)
- [`Resources/Prototypes/_WG/Remade/ward.yml`](../../../Resources/Prototypes/_WG/Remade/ward.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Remade/betrayal.ftl`](../../../Resources/Locale/en-US/_WG/Remade/betrayal.ftl)
- [`Resources/Locale/en-US/_WG/Remade/corruption.ftl`](../../../Resources/Locale/en-US/_WG/Remade/corruption.ftl)
- [`Resources/Locale/en-US/_WG/Remade/counterplay.ftl`](../../../Resources/Locale/en-US/_WG/Remade/counterplay.ftl)
- [`Resources/Locale/en-US/_WG/Remade/director.ftl`](../../../Resources/Locale/en-US/_WG/Remade/director.ftl)
- [`Resources/Locale/en-US/_WG/Remade/dread.ftl`](../../../Resources/Locale/en-US/_WG/Remade/dread.ftl)
- [`Resources/Locale/en-US/_WG/Remade/echoes.ftl`](../../../Resources/Locale/en-US/_WG/Remade/echoes.ftl)
- [`Resources/Locale/en-US/_WG/Remade/elites.ftl`](../../../Resources/Locale/en-US/_WG/Remade/elites.ftl)
- [`Resources/Locale/en-US/_WG/Remade/environment.ftl`](../../../Resources/Locale/en-US/_WG/Remade/environment.ftl)
- [`Resources/Locale/en-US/_WG/Remade/gifts.ftl`](../../../Resources/Locale/en-US/_WG/Remade/gifts.ftl)
- [`Resources/Locale/en-US/_WG/Remade/limbs.ftl`](../../../Resources/Locale/en-US/_WG/Remade/limbs.ftl)
- [`Resources/Locale/en-US/_WG/Remade/openitems.ftl`](../../../Resources/Locale/en-US/_WG/Remade/openitems.ftl)
- [`Resources/Locale/en-US/_WG/Remade/remade.ftl`](../../../Resources/Locale/en-US/_WG/Remade/remade.ftl)
- [`Resources/Locale/en-US/_WG/Remade/roster.ftl`](../../../Resources/Locale/en-US/_WG/Remade/roster.ftl)
- [`Resources/Locale/en-US/_WG/Remade/sector.ftl`](../../../Resources/Locale/en-US/_WG/Remade/sector.ftl)
- [`Resources/Locale/en-US/_WG/Remade/siege.ftl`](../../../Resources/Locale/en-US/_WG/Remade/siege.ftl)
- [`Resources/Locale/en-US/_WG/Remade/signals.ftl`](../../../Resources/Locale/en-US/_WG/Remade/signals.ftl)
- [`Resources/Locale/en-US/_WG/Remade/structures.ftl`](../../../Resources/Locale/en-US/_WG/Remade/structures.ftl)
- [`Resources/Locale/en-US/_WG/Remade/variants.ftl`](../../../Resources/Locale/en-US/_WG/Remade/variants.ftl)

### Textures

- [`Resources/Textures/_WG/Remade/Effects/cutter.rsi/`](../../../Resources/Textures/_WG/Remade/Effects/cutter.rsi/)
- [`Resources/Textures/_WG/Remade/Effects/projectiles.rsi/`](../../../Resources/Textures/_WG/Remade/Effects/projectiles.rsi/)
- [`Resources/Textures/_WG/Remade/Effects/runes.rsi/`](../../../Resources/Textures/_WG/Remade/Effects/runes.rsi/)
- [`Resources/Textures/_WG/Remade/Effects/swinging_limbs.rsi/`](../../../Resources/Textures/_WG/Remade/Effects/swinging_limbs.rsi/)
- [`Resources/Textures/_WG/Remade/Environment/cocoon.rsi/`](../../../Resources/Textures/_WG/Remade/Environment/cocoon.rsi/)
- [`Resources/Textures/_WG/Remade/Environment/decor.rsi/`](../../../Resources/Textures/_WG/Remade/Environment/decor.rsi/)
- [`Resources/Textures/_WG/Remade/Interface/corruption_actions.rsi/`](../../../Resources/Textures/_WG/Remade/Interface/corruption_actions.rsi/)
- [`Resources/Textures/_WG/Remade/Interface/signal_actions.rsi/`](../../../Resources/Textures/_WG/Remade/Interface/signal_actions.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/brute.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/brute.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/brute_flesh.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/brute_flesh.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/divider.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/divider.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/divider_components.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/divider_components.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/divider_components_large.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/divider_components_large.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/exploder.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/exploder.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/exploder_enhanced.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/exploder_enhanced.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/fennec.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/fennec.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/guardian.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/guardian.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/hopper.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/hopper.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/hunter.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/hunter.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/infector.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/infector.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/infector_enhanced.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/infector_enhanced.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/leaper.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/leaper.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/leaper_enhanced.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/leaper_enhanced.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/lurker.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/lurker.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/lurker_malo.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/lurker_malo.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/mender.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/mender.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/puker.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/puker.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/puker_flayed.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/puker_flayed.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/slasher.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/slasher.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/slasher_carrion.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/slasher_carrion.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/slasher_desiccated.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/slasher_desiccated.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/slasher_enhanced.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/slasher_enhanced.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/spitter.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/spitter.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/swarmer.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/swarmer.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/tripod.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/tripod.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/twitcher.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/twitcher.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/twitcher_oracle.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/twitcher_oracle.rsi/)
- [`Resources/Textures/_WG/Remade/Mobs/undying.rsi/`](../../../Resources/Textures/_WG/Remade/Mobs/undying.rsi/)
- [`Resources/Textures/_WG/Remade/Objects/cutter.rsi/`](../../../Resources/Textures/_WG/Remade/Objects/cutter.rsi/)
- [`Resources/Textures/_WG/Remade/Objects/organs.rsi/`](../../../Resources/Textures/_WG/Remade/Objects/organs.rsi/)
- [`Resources/Textures/_WG/Remade/Objects/shard.rsi/`](../../../Resources/Textures/_WG/Remade/Objects/shard.rsi/)
- [`Resources/Textures/_WG/Remade/Shaders/dread.swsl`](../../../Resources/Textures/_WG/Remade/Shaders/dread.swsl)
- [`Resources/Textures/_WG/Remade/Shaders/ward.swsl`](../../../Resources/Textures/_WG/Remade/Shaders/ward.swsl)
- [`Resources/Textures/_WG/Remade/Structures/corruption.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/corruption.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/growth_large.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/growth_large.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/growth_wall.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/growth_wall.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/growths.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/growths.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/harvester.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/harvester.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/nest.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/nest.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/prime.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/prime.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/scion.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/scion.rsi/)
- [`Resources/Textures/_WG/Remade/Structures/wall_growth.rsi/`](../../../Resources/Textures/_WG/Remade/Structures/wall_growth.rsi/)

### Audio

- [`Resources/Audio/_WG/Remade/attacks/big_swoosh_1.ogg`](../../../Resources/Audio/_WG/Remade/attacks/big_swoosh_1.ogg)
- [`Resources/Audio/_WG/Remade/attacks/big_swoosh_2.ogg`](../../../Resources/Audio/_WG/Remade/attacks/big_swoosh_2.ogg)
- [`Resources/Audio/_WG/Remade/attacks/big_swoosh_3.ogg`](../../../Resources/Audio/_WG/Remade/attacks/big_swoosh_3.ogg)
- [`Resources/Audio/_WG/Remade/attributions.yml`](../../../Resources/Audio/_WG/Remade/attributions.yml)
- [`Resources/Audio/_WG/Remade/betrayal/attributions.yml`](../../../Resources/Audio/_WG/Remade/betrayal/attributions.yml)
- [`Resources/Audio/_WG/Remade/betrayal/breath_1.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/breath_1.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/breath_2.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/breath_2.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/breath_3.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/breath_3.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/knock_1.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/knock_1.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/knock_2.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/knock_2.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/knock_3.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/knock_3.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/knock_4.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/knock_4.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_credits_rock.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_credits_rock.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_credits_violin.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_credits_violin.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_danik_speech.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_danik_speech.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_nicole.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_nicole.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_pensive.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_pensive.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/music_unitology.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/music_unitology.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/scrape_1.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/scrape_1.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/scrape_2.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/scrape_2.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/scrape_3.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/scrape_3.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_1.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_1.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_10.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_10.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_2.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_2.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_3.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_3.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_4.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_4.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_5.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_5.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_6.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_6.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_7.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_7.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_8.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_8.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/step_9.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/step_9.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/stinger_1.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/stinger_1.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/stinger_2.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/stinger_2.ogg)
- [`Resources/Audio/_WG/Remade/betrayal/stinger_3.ogg`](../../../Resources/Audio/_WG/Remade/betrayal/stinger_3.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_death.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_death.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_pain_extreme.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_pain_extreme.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/brute/brute_shout_long.ogg`](../../../Resources/Audio/_WG/Remade/brute/brute_shout_long.ogg)
- [`Resources/Audio/_WG/Remade/cutter/plasma_cutter.ogg`](../../../Resources/Audio/_WG/Remade/cutter/plasma_cutter.ogg)
- [`Resources/Audio/_WG/Remade/cyst/cyst_fire_1.ogg`](../../../Resources/Audio/_WG/Remade/cyst/cyst_fire_1.ogg)
- [`Resources/Audio/_WG/Remade/cyst/cyst_fire_2.ogg`](../../../Resources/Audio/_WG/Remade/cyst/cyst_fire_2.ogg)
- [`Resources/Audio/_WG/Remade/cyst/cyst_fire_3.ogg`](../../../Resources/Audio/_WG/Remade/cyst/cyst_fire_3.ogg)
- [`Resources/Audio/_WG/Remade/cyst/cyst_fire_4.ogg`](../../../Resources/Audio/_WG/Remade/cyst/cyst_fire_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/arm_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/arm_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/head_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/head_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/head_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/head_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/head_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/head_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/head_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/head_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/component/leg_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/divider/component/leg_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_death.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_death.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_posession.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_posession.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_shout_long_6.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_shout_long_6.ogg)
- [`Resources/Audio/_WG/Remade/divider/divider_split.ogg`](../../../Resources/Audio/_WG/Remade/divider/divider_split.ogg)
- [`Resources/Audio/_WG/Remade/dread/attributions.yml`](../../../Resources/Audio/_WG/Remade/dread/attributions.yml)
- [`Resources/Audio/_WG/Remade/dread/bed_drone.ogg`](../../../Resources/Audio/_WG/Remade/dread/bed_drone.ogg)
- [`Resources/Audio/_WG/Remade/dread/bed_whisper_a.ogg`](../../../Resources/Audio/_WG/Remade/dread/bed_whisper_a.ogg)
- [`Resources/Audio/_WG/Remade/dread/bed_whisper_b.ogg`](../../../Resources/Audio/_WG/Remade/dread/bed_whisper_b.ogg)
- [`Resources/Audio/_WG/Remade/dread/bed_whisper_c.ogg`](../../../Resources/Audio/_WG/Remade/dread/bed_whisper_c.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_far_noise.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_far_noise.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_growl_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_growl_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_growl_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_growl_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_growl_3.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_growl_3.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_heart_beat.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_heart_beat.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_very_far_noise.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_very_far_noise.ogg)
- [`Resources/Audio/_WG/Remade/dread/menace_wail.ogg`](../../../Resources/Audio/_WG/Remade/dread/menace_wail.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_behind_you_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_behind_you_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_behind_you_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_behind_you_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_look_up_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_look_up_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_look_up_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_look_up_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_over_here_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_over_here_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_over_here_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_over_here_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_over_here_3.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_over_here_3.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_turn_around_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_turn_around_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/voice_turn_around_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/voice_turn_around_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_back_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_back_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_back_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_back_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_back_3.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_back_3.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_back_4.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_back_4.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_hit_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_hit_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_hit_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_hit_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_hit_3.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_hit_3.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_hit_4.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_hit_4.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_low_1.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_low_1.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_low_2.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_low_2.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_low_3.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_low_3.ogg)
- [`Resources/Audio/_WG/Remade/dread/whisper_low_4.ogg`](../../../Resources/Audio/_WG/Remade/dread/whisper_low_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_blast_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_blast_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_blast_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_blast_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_blast_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_blast_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_blast_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_blast_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_blast_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_blast_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_death_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_death_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_death_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_death_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_death_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_death_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_7.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_7.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_5.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_5.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_6.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_6.ogg)
- [`Resources/Audio/_WG/Remade/exploder/exploder_speech_7.ogg`](../../../Resources/Audio/_WG/Remade/exploder/exploder_speech_7.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_5.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_5.ogg)
- [`Resources/Audio/_WG/Remade/footstep/brute_step_6.ogg`](../../../Resources/Audio/_WG/Remade/footstep/brute_step_6.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_6.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_6.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_7.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_7.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/exploder_footstep_multi_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/infector_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/infector_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/infector_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/infector_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/infector_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/infector_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/infector_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/infector_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/leaper_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/leaper_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/leaper_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/leaper_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/leaper_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/leaper_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/leaper_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/leaper_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/leaper_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/footstep/leaper_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_6.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_6.ogg)
- [`Resources/Audio/_WG/Remade/footstep/lurker_footstep_7.ogg`](../../../Resources/Audio/_WG/Remade/footstep/lurker_footstep_7.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/footstep/tripod_footstep_6.ogg`](../../../Resources/Audio/_WG/Remade/footstep/tripod_footstep_6.ogg)
- [`Resources/Audio/_WG/Remade/footstep/twitcher_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/twitcher_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/twitcher_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/twitcher_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/twitcher_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/twitcher_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/twitcher_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/twitcher_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/footstep/undying_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/footstep/undying_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/footstep/undying_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/footstep/undying_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/footstep/undying_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/footstep/undying_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/footstep/undying_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/footstep/undying_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_death_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_death_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_death_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_death_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_death_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_death_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_death_4.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_death_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_flap_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_flap_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_flap_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_flap_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_flap_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_flap_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_flap_4.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_flap_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_flap_5.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_flap_5.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_work_1.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_work_1.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_work_2.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_work_2.ogg)
- [`Resources/Audio/_WG/Remade/infector/infector_work_3.ogg`](../../../Resources/Audio/_WG/Remade/infector/infector_work_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_claw_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_claw_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_claw_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_claw_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_death_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_death_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_death_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_death_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_death_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_death_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_tailswing_1.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_tailswing_1.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_tailswing_2.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_tailswing_2.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_tailswing_3.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_tailswing_3.ogg)
- [`Resources/Audio/_WG/Remade/leaper/leaper_tailswing_4.ogg`](../../../Resources/Audio/_WG/Remade/leaper/leaper_tailswing_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_attack_9.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_attack_9.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_4.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_5.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_5.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_climb_6.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_climb_6.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_death_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_death_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_death_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_death_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_death_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_death_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_7.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_7.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/lurker_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/lurker/lurker_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_fire_1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_fire_1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_fire_2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_fire_2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_fire_3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_fire_3.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_woosh1.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_woosh1.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_woosh2.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_woosh2.ogg)
- [`Resources/Audio/_WG/Remade/lurker/spine_woosh3.ogg`](../../../Resources/Audio/_WG/Remade/lurker/spine_woosh3.ogg)
- [`Resources/Audio/_WG/Remade/monolith/alarm.ogg`](../../../Resources/Audio/_WG/Remade/monolith/alarm.ogg)
- [`Resources/Audio/_WG/Remade/monolith/crescendo_song.ogg`](../../../Resources/Audio/_WG/Remade/monolith/crescendo_song.ogg)
- [`Resources/Audio/_WG/Remade/monolith/stage_growth.ogg`](../../../Resources/Audio/_WG/Remade/monolith/stage_growth.ogg)
- [`Resources/Audio/_WG/Remade/monolith/stage_harvest.ogg`](../../../Resources/Audio/_WG/Remade/monolith/stage_harvest.ogg)
- [`Resources/Audio/_WG/Remade/monolith/stage_hymn.ogg`](../../../Resources/Audio/_WG/Remade/monolith/stage_hymn.ogg)
- [`Resources/Audio/_WG/Remade/monolith/stage_rapture.ogg`](../../../Resources/Audio/_WG/Remade/monolith/stage_rapture.ogg)
- [`Resources/Audio/_WG/Remade/monolith/throb.ogg`](../../../Resources/Audio/_WG/Remade/monolith/throb.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_death_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_death_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_death_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_death_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_death_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_death_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_4.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_4.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_5.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_5.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_6.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_6.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_7.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_7.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_8.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_8.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_footstep_9.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_footstep_9.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/puker/puker_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/puker/puker_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_death_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_death_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_death_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_death_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_death_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_death_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_death_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_death_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher/slasher_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher/slasher_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_death_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_extreme.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_pain_extreme.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_6.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_shout_long_6.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/slasher_enhanced/eslasher_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_9.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_9.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_attack_extreme.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_attack_extreme.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_death_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_death_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_death_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_death_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_death_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_death_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_8.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_8.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_extreme.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_extreme.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_pain_extreme_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_pain_extreme_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/spitter/spitter_speech_5.ogg`](../../../Resources/Audio/_WG/Remade/spitter/spitter_speech_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_attack_9.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_attack_9.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_death_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_death_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_death_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_death_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_death_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_death_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_death_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_death_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_shout_long_6.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_shout_long_6.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_extract_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_3.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_4.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_4.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_5.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongue_lash_5.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_1.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_1.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_2.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_2.ogg)
- [`Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_3.ogg`](../../../Resources/Audio/_WG/Remade/tripod/tripod_tongueforce_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_death_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_death_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_death_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_death_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_death_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_death_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_death_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_death_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_8.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_8.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_9.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_9.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_pain_extreme.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_pain_extreme.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_5.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_shout_long_5.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/twitcher/twitcher_speech_5.ogg`](../../../Resources/Audio/_WG/Remade/twitcher/twitcher_speech_5.ogg)
- [`Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/Ubermorph_spawn_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_5.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_5.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_6.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_6.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_7.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_7.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_attack_8.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_attack_8.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_5.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_5.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_6.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_6.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_7.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_7.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_pain_8.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_pain_8.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_regen_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_regen_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_regen_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_regen_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_5.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_5.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_6.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_6.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_7.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_7.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_8.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_8.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_long_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_long_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_long_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_long_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_long_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_long_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_shout_long_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_shout_long_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_1.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_1.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_2.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_2.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_3.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_3.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_4.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_4.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_5.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_5.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_6.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_6.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_7.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_7.ogg)
- [`Resources/Audio/_WG/Remade/undying/undying_speech_8.ogg`](../../../Resources/Audio/_WG/Remade/undying/undying_speech_8.ogg)

### Guidebook

- [`Resources/ServerInfo/_WG/Remade/Guidebook/Ending.xml`](../../../Resources/ServerInfo/_WG/Remade/Guidebook/Ending.xml)
- [`Resources/ServerInfo/_WG/Remade/Guidebook/Fighting.xml`](../../../Resources/ServerInfo/_WG/Remade/Guidebook/Fighting.xml)
- [`Resources/ServerInfo/_WG/Remade/Guidebook/Monolith.xml`](../../../Resources/ServerInfo/_WG/Remade/Guidebook/Monolith.xml)
- [`Resources/ServerInfo/_WG/Remade/Guidebook/Remade.xml`](../../../Resources/ServerInfo/_WG/Remade/Guidebook/Remade.xml)

### Tools

- [`Tools/_WG/Remade/make_mender.py`](../../../Tools/_WG/Remade/make_mender.py)
- [`Tools/_WG/Remade/make_stage_stings.py`](../../../Tools/_WG/Remade/make_stage_stings.py)
- [`Tools/_WG/Remade/port_assets.py`](../../../Tools/_WG/Remade/port_assets.py)
- [`Tools/_WG/Remade/port_betrayal.py`](../../../Tools/_WG/Remade/port_betrayal.py)
- [`Tools/_WG/Remade/port_counterplay.py`](../../../Tools/_WG/Remade/port_counterplay.py)
- [`Tools/_WG/Remade/port_dread.py`](../../../Tools/_WG/Remade/port_dread.py)
- [`Tools/_WG/Remade/port_environment.py`](../../../Tools/_WG/Remade/port_environment.py)
- [`Tools/_WG/Remade/render_corruption.py`](../../../Tools/_WG/Remade/render_corruption.py)

### Docs

- [`Docs/_WG/Remade/PORT_BRIEF.md`](../../../Docs/_WG/Remade/PORT_BRIEF.md)

## Non-modular edits

- [`Content.Server/Lathe/LatheSystem.cs`](../../Lathe/LatheSystem.cs): lets a print from Monolith-made stock mark its product unsellable
- [`Content.Server/Materials/MaterialStorageSystem.cs`](../../Materials/MaterialStorageSystem.cs): Monolith-made material stays in the silo, or it would sell as sheets
- [`Resources/Fonts/_DS13/`](../../../Resources/Fonts/_DS13/): DS13's Shage Marker-script font (CC-BY 3.0, BlueNexus) for the Monolith Tap's glyphs.
- [`Resources/Prototypes/_NF/Guidebook/nf14.yml`](../../../Resources/Prototypes/_NF/Guidebook/nf14.yml): the Monolith, the Remade and how to end an outbreak
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): the plasma cutter and the monolith tap flatpack

<!-- WOLFGATE-GENERATED END -->
