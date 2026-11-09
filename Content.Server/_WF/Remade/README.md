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

**The siege** (`Siege/`, `WFSiegeSystem`). A Scion that reaches Harvest sends flesh ships against the reality anchors:
the first after 3 minutes, then one every 15 minutes, two a third sooner at Rapture, three twice as often at the Crescendo,
at most 7 alive. Each is a Choir-crewed `WFEncounterNecroticFleet` hull overgrown with corruption, carrying Remade, set
on the standing primary anchor nearest the Scion. Within 900 m it bleeds the anchor's shield, then its core once the
shield is down, and puts two Remade on the station every minute; the anchor's lances and drones answer it. When its anchor
falls it moves on to the next. Waves stop with the Unstringing. Tuning is in `WFSiegeCVars` (`wf.remade.siege.*`).
Inside a sealed hex field nothing of a Monolith outside it reaches anyone, and the hex counts as a sanctuary (see the
Lattice README); a standing anchor's ward keeps corruption off nearby grids (`WFCorruptionSystem.IsWarded`).

**Admins.** Spawn `WFMonolithScion` and `WFMonolithTap`, or `WFMonolithSplinter` for an outbreak on one ship. Set `Resonance` on `WFMonolithComponent` with VV to skip ahead:
15 Hymn, 35 Growth, 55 Harvest, 80 Rapture, 100 Crescendo.

**Entry points.** `WFMonolithMath` (the climb, stages and output), `WFMonolithSystem`, `WFMonolithTapSystem`,
`WFCorruptionSystem`, `WFRemadeConversionSystem` (rules from DS13-1.0's `conversion.dm`, each mob's cost and weights in its `wfRemadeMob`
prototype), `WFRemadeDirectorSystem` (phases Quiet, Stalk, Peak, Relax; sleeps with no crew in 64 m). Assets come from
`Tools/_WF/Remade/port_assets.py`, which holds the whole DS13-2.0 roster; only the slasher and infector have mobs so far.

**How they hit.** A Remade has no aim of its own, so each blow lands on a random part of its victim, by the victim's own
odds. Their claws hit hard enough for Wolfmed's deep-cut rules: a slasher's 24 slash can open an artery (22) and cut a
tendon (16) in one swing, so they bleed people out and take them apart rather than scratching. Giving them `Targeting`
would also change how damage they take is routed, which the Undying and the Crescendo tests catch.

**Limbs and abilities.** A Remade built of limbs (`WFRemadeLimbs`, Shared) shares each hit between its limbs by weight, or by a
player's targeting aim, and the torso takes only a fraction, so shooting it barely works. Slash and piercing sever easily, blunt and
heat poorly. Severing hides the limb's layer, drops it as an item and costs the mob a lump of damage, so dismemberment is the fast
kill. What each limb gives the mob (melee, speed, voice, sight, reanimation) is data on the limb. `WFRemadeAbilities` gives an NPC
abilities (`WFShoutAbility`, `WFChargeAbility`, `WFDodgeAbility`) that `WFAbilitySystem` runs for awake NPCs with a target; new ones
subclass `WFAbility`.

**The Director.** It plays the Monolith with hearing, nerves and tricks rather than omniscience. Gunfire, blasts and cries draw the
Remade; otherwise they drift towards the crew roughly, sharpening each time they find nobody. Waves start in far vents, rattle through
the ducts where the crew can hear, and burst out of several vents round the prey at once. Some Remade lie among the dead and rise when
approached or hit. A hurt Remade out of sight retreats to a vent and returns later, and lone stalkers follow a crewman at a distance
before committing. Between waves it works on one crewman at a time with flickers, whispers, false voices, scrawls, runes, doors, blown
lights and a biohazard lockdown, paid from a Will pool that grows with the stage. Each crewman has a stress value: frightened crew are
left alone, calm crew are pressed harder, and nobody gets a second wave straight after the first below Rapture.

**Roster.** The common Remade are in `Resources/Prototypes/_WF/Remade/roster.yml`: hopper, leaper (plain and black), spitter, exploder
(plain and black), puker and lurker. A hopper or leaper leaps across a room (`WFLeapAbility`) and stabs with its tail
(`WFTailStabAbility`); both need their legs. A spitter fires acid (`WFShootAbility`) and, like the lurker, fights from range through
`WFSkirmishOperator`. An exploder swells and bursts after a fuse (`WFExplodeAbility`, `WFExploderSystem`) while it has its pustule; a
pustule shot from range bursts on the spot, and one cut off up close just comes away. A puker vomits a cone of acid that burns and
slows (`WFVomitAbility`). A lurker (`WFSpineAbility`, `WFShellSystem`) opens its shell to fire one spine per tentacle and curls up when
hurt; its tentacles can only be hit while it is open.

**Growths.** The Director grows structures on corruption (`wfRemadeGrowth` prototypes, `WFGrowthSystem`). Nodes (`WFCorruptionBranch`,
`Root`, `Propagator`) feed corruption within their reach and raise a Monolith's tile cap past its stage limit, to 512 per hull; killing
one starves the tiles it fed. Cysts fire bio-bombs from walls at anyone in their arc, and snares trip and slow whoever steps on them.
Maws warn, grab after 3 seconds, bite, take limbs and eat the dead; free someone by clicking them and pulling, or kill the maw. Nests
slowly make up to two of their chosen Remade, harvesters turn plants, food and bodies nearby into biomass, eyes tell the Director where
crew walked, and lamps give a dim sickly light. The Director builds out of the crew's sight, keeping a reserve for waves: an Infector
walks to the site and works there, or a seed swells up slowly. Every growth goes dormant off corruption and dies when its Monolith goes.

**Elites.** The brute, twitcher, oracle twitcher, hunter, divider, tripod, the Undying and the enhanced infector are in
`Resources/Prototypes/_WF/Remade/elites.yml`. A brute takes a third of the damage from the front and one and a half times from behind
(`WFBruteArmourComponent`), slams all round itself, rams through doors and windows, and lobs acid. A twitcher slips bullets from a pool
that drains as it dodges (`WFDodgePoolComponent`), and blinks and charges. A hunter heals, regrows limbs and gets back up 15 s after
dying (`WFRegenComponent`, `WFRisesComponent`), unless it was burned, beheaded, mostly dismembered or killed again within two minutes. A
divider breaks into crawling arm, leg and head mobs as its limbs come off and when it dies (`WFDividerSystem`). A tripod hauls people in
with its tongue, leaps and swings. The Undying is announced to everyone on its grid, never runs, and folds up for a minute when it takes
lethal damage or loses every limb. The enhanced infector turns corpses in about half the time (`WFReanimateHasteSystem`).

Gifts (`Gifts/`). Each Gift is a dial on the Tap; the dials add as the root of their squares, capped at 1.5, to drive the climb.
Matter slowly fills a material silo on the Scion's ship; that stock can't be ejected as sheets, and what a lathe prints from it sells for
nothing (`WFNoResaleComponent`). Flesh heals humanoids in reach and slowly raises the dead through Wolfmed's revival; anyone raised is
marked and rises as Remade the moment they die again. Reach shortens jumps and widens radar on the Scion's grid, and every jump adds
resonance whatever the dial. A sharp tool or welder on an anchored Scion chips off a shard: a power cell that never runs down, sells while
its Scion is quiet, and roots into small patches of corruption once its Scion reaches Growth, until it's cut free. A rooted Scion holds
Matter, Flesh and Reach at full, and a sanctuary grid bars every Gift.

Sector (`Sector/`). `WFRemadeDiscoveryRule`, carried by the Contested Sector and Escalation presets and gated by
`wf.encounters.enabled` and `wf.remade.sector.enabled`, now and then starts a `wfRemadeSite`: an unlabelled hulk, marked on the sector map, with a dormant
Scion wrenched to its deck, bare or guarded by the Choir (`WFChoir`, friendly to the Remade, with a ship that holds beside the hulk). A
garbled radio line gives a rough position. One wild Scion at a time, and none soon after one is lost or sold. A Scion that is moved,
unwrenched or drawn on shows on every radar and the sector map, wider with resonance (`WFRemadeSignalSystem`); the Choir hunt the ship
carrying it and pirate boarders are sent after it. `WFEncounterRemadeAdrift` is a stranded freighter at Harvest with crew aboard and a
mayday. Admins use `wf_remade spawn [site] [here] | resonance <value> [uid] | status`.

Open items. A Remade that hears something walks to it: hunting keeps its target past the first step, and Remade the Director owns
stay awake out to its 64 m radius. The Undying dies for good with its Monolith, stays down on a powered stasis bed, and doesn't re-form
after 150 heat damage while down. A twitcher's dodge pool works against beams, and a dodging Remade steps aside from a near miss. A hunter
or Undying shows a lost limb growing back. A brute flings whoever it hits, a lurker dies with no tentacles, and a divider's head turns a
body it kills or walks to a corpse. What a corpse becomes depends on its sex, armour, legs, poison and job
(`WFRemadeConversionSystem.Weights`). Nothing grows and no Gift works within `remade.sanctuary_radius` (500 m) of CC, the only sanctuary by default
(`remade.sanctuary_stations`, matched against a grid's `BecomesStation` id; `WFRemadeSanctuaryComponent` marks any other grid).

Counterplay (`Counterplay/`). Corruption burned away leaves its tile scorched for 10 minutes, and nothing grows back there
(`WFScorchSystem`, through `WFCorruptionGrowAttemptEvent`). Necrotoxin (`WFNecrotoxin`: mercury, sulfur and ammonia, or a
`WFNecrotoxinPen`) poisons Remade through their blood whatever their armour and barely hurts crew; a corpse with a unit of it in the
blood can't turn. The plasma cutter (`WFRemadeCutter`, autolathe) throws a short arc that takes limbs off in a few shots. A Monolith
rooted at the Crescendo becomes the Prime (`WFPrimeSystem`): nothing harms it but a nuclear detonation within twice the payload's
erase radius plus 16 m (`WFNuclearDetonatedEvent`, from the Warheads module), a verse every 10 minutes that announces and feeds the
Director, and a warrant on the PDA board paid to whoever set the nuke off, or shared among those nearby.
Destroying any Monolith leaves a husk and turns its Remade feral (`WFFeralSystem`). The Tap is an autolathe flatpack. Guidebook pages
are in `Resources/ServerInfo/_WF/Remade/Guidebook/`.

Critters (`Critters/`). A dead animal turns like dead crew: it needs its head if it has a body, can't have a mind in it, and
shakes and bursts the same way. A small one (under 30 kg) rises as a hopper and a big one as a lurker, set by `animalWeight` and the
mass range on each `wfRemadeMob`; necrotoxin protects animal corpses too. A hopper with nothing to hunt climbs onto the nearest table
and is drawn under it (`WFCoverSystem`, through the Rodentia sneak flag), its sight shrunk to 4 m until prey, a hunt order or a wound
brings it out. A leaper with nothing to hunt stands against the nearest wall (`WFWallClingSystem`); leaping from there reaches 4 tiles
further, winds up in 0.6 of the time and hits 1.25 times as hard.

Variants (`variants.yml`, `Variants/`). Most DS13 variants are another skin on one species, picked by weight, so each is an
entity parented to its base with its own sprite sheet. The desiccated and carrion slashers, flayed puker and hound lurker follow DS13's
shares, with the base weights cut to match. The flesh brute only has finished torso art, so its limbs don't come off. The fennec is a
tougher, faster hopper from Growth. A Remade steering for a target that hasn't moved 0.5 m in 3 s breaks the anchored thing in its way
with its normal melee (`WFStuckBreakerSystem`); the pathfinder can't see railings, so without this a Remade presses against one forever.
Only a brute or the Undying breaks a wall, and only after 6 s stuck with nothing else in the way.

Corruption coverage (`Corruption/`). A Monolith may cover a share of the floor it can walk to on its hull: a quarter at Growth, over
half at Harvest, all of it at Rapture, and half as much again onto docked ships at Crescendo; small hulls keep 64, 160 and 256 tiles.
Tiles spread faster as the stage climbs and cross shut doors, windoors and airlock chambers (`WFCorruptionSystem.Doors.cs`). Each tile
works out which of its eight neighbours join it and the client picks the matching DS13 state (`WFCorruptionVisualizerSystem`), so a
carpet reads as one piece. A wall that corruption touches gets a growth over it (`WFCorruptionWall`) that burns like the floor and goes
with the corruption or the wall. Blood on corruption is drunk at 0.5 units a second and pays the Director 0.1 biomass a unit.

Dread and Ward (`Dread/`, `Ward/`). `WFDreadSystem` sweeps every living player once a second. A stone at Hymn or above reaches crew
near it, falling off to nothing at 30 to 220 m by stage; at Rapture lightly and at the Crescendo more so, it reaches the whole sector.
A player gets whispers (a line and a DS13 voice), glimpses (a Remade shown on that client alone for 0.2 to 0.4 s in a dark spot at the
edge of its view) and a warped screen (`dread.swsl`: wobble, colour split, vignette, drain), all scaled by stage and stress. Reduced
motion or `wf.remade.dread.visuals` 0 turns off glimpses and the moving warp. The biohazard alarm plays three times back to back at a
lockdown and at the Crescendo (`WFDreadAlarmSystem`), with the Crescendo song played to the whole round. Ward is the fifth Gift dial:
the Scion feeds the stock ship shield emitter for free, far above any generator. A rooted stone's field can't be broken, and
`WFWardSystem` pushes any ship that isn't the stone's own or docked to it back to the field's edge, while people on foot cross freely.

Shards, the dark and the bed. A Scion's shard gives its parent's dread in a small way: whoever carries it feels a third to a half of
the stone's strength, and anyone within 4 m (6 m rooted) less; none of it near CC. From Harvest a whisper bed plays in the player's own
head (`WFDreadBedSystem`: three whisper loops that never line up, a heartbeat and a drone, scaled by `wf.remade.dread.bed`), with a floor
at Rapture and the Crescendo, and wordless murmurs overlap from Rapture. From Harvest `WFDarknessSystem` breaks the lights on the
stone's ship and anything docked to it, nearest first: 12% at Harvest, 45% at Rapture, 92% at the Crescendo. The sounds are DS13's
only, with the ghost moans, taunting voices and screech removed (`Tools/_WF/Remade/port_dread.py` cuts the whispers). The Ward's field
is drawn by the Remade module (`WFWardOverlay`, `ward.swsl`): a dark red veined membrane that pulses, brightens where it's hit and sets
hard and bright when rooted, with Marker glyphs drifting through it.

Signals (`Signals/`). A stone at Hymn or above offers a ghost role in the normal Ghost Roles window (`WFSignalGate`): one Signal per
stone at Hymn, rising to five at the Crescendo. A Signal is an unseen eye (`WFSignal`) that sees through walls and in the dark, leashed
to the stone's ship and anything docked to it, its corruption and its rooted shards. It spends Psi (100, regained 0.4 to 2.2 a second
by stage) on the Director's tricks: flicker, whisper, false sound and scrawl from Hymn, open or bolt a door from Growth (`WFSignalMath`).
For 20 Psi it takes over a Remade of its stone within 8 m, whose thinking stops while the player uses its abilities as actions; the
Undying and Remade the Director is busy with can't be taken. Nothing works within the sanctuary, and a stone that is destroyed, quiet,
barred or switched off with `wf.remade.signals.enabled` lets its Signals go as ghosts. Everything a Signal does is admin-logged.

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
lines in their name every one to three minutes when crew are near (`WFBetrayalVoiceSystem`). Their headset pleads garbled over its own
channels from where they died a few times, and their suit sensor keeps reading alive on the crew monitor (`WFSensorGhostSystem`). When a
Director goes quiet to stalk, ambient sound, music and the whisper bed fade until a Remade first sees a player in the Peak, who gets a
DS13 stinger; under a stone a low score gains a layer with each step of resonance (`WFBetrayalScoreSystem`, `WFBetrayalMusicSystem`).
Players alone hear knocking behind doors, scraping in walls, breath at their back and footsteps that stop when they stop, and see
crewmates as Remade for a split second (`WFBetrayalFearSystem`, `WFBetrayalSensesSystem`, `WFBetrayalFlickerSystem`). Past stress 40 a
player gets a shaking aim, dropped items, nosebleeds, twitches, false announcements and PDA messages from the dead. From Growth the dead
near a stone shift only when nobody can see them (`WFRestlessCorpseSystem`). Client settings `wf.remade.betrayal.music`,
`wf.remade.betrayal.sounds`; server `wf.remade.betrayal.rate`. Audio is built by `Tools/_WF/Remade/port_betrayal.py`.

Build-up and omens (`Closets/`, `Echoes/`, `Omens/`). A stone's first Growth stays quiet for 150-210 s: no wave, stalker, ambush,
garrison or morgue burst (morgue bodies under a Hymn or building stone only knock). Part way through, one Remade follows a crewman and
slips into a vent once seen. The Director shuts Remade in empty lockers that breathe and scrape, and open on whoever opens the locker
or stands beside it. A crewman hiding in a locker drops out of its sight until they talk in there; then a Remade beside the locker tears
it open. A crewman who dies near a stone calls once on their own headset a minute or two later, naming the nearest beacon, and a fake
body is laid beside theirs (`WFRemadeEchoSystem`). These are real, shared sounds, unlike Betrayal's private ones: knocks on a shut door
near the focus (from Harvest, sometimes with a Remade behind it), scraping that moves along a wall, a Remade in front of a camera
someone is watching, and lights stuttering before each wave. Each stage climb plays a sting to every player in the round, at most one
per 45 s; the stings are mixed by `Tools/_WF/Remade/make_stage_stings.py` from a tgstation checkout.

The mender (`WFRemadeMender`, `WFReclaimSystem`, `mender.rsi` from `Tools/_WF/Remade/make_mender.py`) is a bleached hunter that
grows nothing back. Its limbs fly a few metres when they come off, and with three off it goes down. After 3 s thin tentacles
(`WFReclaimTentacleOverlay`, client) reach out to every severed limb within 6 m and drag it back at about a metre a second; each knits
back on, and once whole it gets up. A limb burnt to ash or blown apart, or held or carried out of reach for 8 s, leaves it down for
good, as does burning the body while it lies there. Severed limbs of any Remade now burn. It is a rare Rapture elite, one at a time.

The torment smite (`Torment/`) is an admin toggle under Smite on any mob: the player gets every whisper, glimpse, voice, warp, false
sense, mind trick, the bed and the full score as if a Crescendo stone stood beside them, wherever they are, and nobody else does.
Biohazard lockdown, beside it under Smite or `wf_remade lockdown <player or uid> [seconds] [range]`, bolts the powered doors round them
(9 m, a minute by default) under the biohazard alarm, the Director's lockdown with no stone needed.

Outbreaks, splinters and clearing (`Outbreaks/`, `splinter.yml`, `Sector/WFRemadeClearSystem.cs`). A stone at the Crescendo breaks
out on another crewed ship every two to three and a half minutes: two or three hoppers and slashers come out of an unseen vent near
someone aboard, and a shard of the stone roots there, so the dead aboard turn too. It all goes with the stone. `WFMonolithSplinter`
is the small event for one ship: an admin spawns it on a deck, it wakes at the Hymn and climbs on its own to the Harvest and no
further, its dread, score and stings stay off every other grid, and it has no Signals; it breaks at 800 damage. Away from a stone the sector
gets the score and a quieter bed rather than voices: whispers, murmurs and Betrayal's knocks only reach crew within the stone's own
radius. A turning corpse drops its gear and nothing else, and a gibbed Remade leaves no organs or parts (`WFRemadeGibSystem`). `wf_remade clear` deletes every stone, shard,
Remade, growth, corruption tile, Signal and stone sound and tells every client to stop the bed and score, as the last stone going
does on its own. `wf_remade outbreak [uid]` forces an outbreak.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Remade/Abilities/WFAbility.cs`](Abilities/WFAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFAbilitySystem.Conversion.cs`](Abilities/WFAbilitySystem.Conversion.cs)
- [`Content.Server/_WF/Remade/Abilities/WFAbilitySystem.cs`](Abilities/WFAbilitySystem.cs)
- [`Content.Server/_WF/Remade/Abilities/WFAbilitySystem.Elites.cs`](Abilities/WFAbilitySystem.Elites.cs)
- [`Content.Server/_WF/Remade/Abilities/WFAbilitySystem.Roster.cs`](Abilities/WFAbilitySystem.Roster.cs)
- [`Content.Server/_WF/Remade/Abilities/WFBlinkAbility.cs`](Abilities/WFBlinkAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFChargeAbility.cs`](Abilities/WFChargeAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFDodgeAbility.cs`](Abilities/WFDodgeAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFExplodeAbility.cs`](Abilities/WFExplodeAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFLatchAbility.cs`](Abilities/WFLatchAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFLeapAbility.cs`](Abilities/WFLeapAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFPounceAbility.cs`](Abilities/WFPounceAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFRamAbility.cs`](Abilities/WFRamAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFRemadeAbilitiesComponent.cs`](Abilities/WFRemadeAbilitiesComponent.cs)
- [`Content.Server/_WF/Remade/Abilities/WFShootAbility.cs`](Abilities/WFShootAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFShoutAbility.cs`](Abilities/WFShoutAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFSpineAbility.cs`](Abilities/WFSpineAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFSwingAbility.cs`](Abilities/WFSwingAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFTailStabAbility.cs`](Abilities/WFTailStabAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFTauntAbility.cs`](Abilities/WFTauntAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFTongueAbility.cs`](Abilities/WFTongueAbility.cs)
- [`Content.Server/_WF/Remade/Abilities/WFVomitAbility.cs`](Abilities/WFVomitAbility.cs)
- [`Content.Server/_WF/Remade/AI/WFPickCorpseOperator.cs`](AI/WFPickCorpseOperator.cs)
- [`Content.Server/_WF/Remade/AI/WFReanimateOperator.cs`](AI/WFReanimateOperator.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFBetrayalComponents.cs`](Betrayal/WFBetrayalComponents.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFBetrayalFearSystem.cs`](Betrayal/WFBetrayalFearSystem.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFBetrayalFearSystem.Mind.cs`](Betrayal/WFBetrayalFearSystem.Mind.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFBetrayalScoreSystem.cs`](Betrayal/WFBetrayalScoreSystem.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFBetrayalVoiceSystem.cs`](Betrayal/WFBetrayalVoiceSystem.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFRestlessCorpseSystem.cs`](Betrayal/WFRestlessCorpseSystem.cs)
- [`Content.Server/_WF/Remade/Betrayal/WFSensorGhostSystem.cs`](Betrayal/WFSensorGhostSystem.cs)
- [`Content.Server/_WF/Remade/Closets/WFClosetComponents.cs`](Closets/WFClosetComponents.cs)
- [`Content.Server/_WF/Remade/Closets/WFRemadeDirectorSystem.Closets.cs`](Closets/WFRemadeDirectorSystem.Closets.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeComponent.cs`](Conversion/WFRemadeComponent.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeConversionSystem.Conditions.cs`](Conversion/WFRemadeConversionSystem.Conditions.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeConversionSystem.cs`](Conversion/WFRemadeConversionSystem.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeGibSystem.cs`](Conversion/WFRemadeGibSystem.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeVoiceComponent.cs`](Conversion/WFRemadeVoiceComponent.cs)
- [`Content.Server/_WF/Remade/Conversion/WFRemadeVoiceSystem.cs`](Conversion/WFRemadeVoiceSystem.cs)
- [`Content.Server/_WF/Remade/Conversion/WFTakesBodyComponent.cs`](Conversion/WFTakesBodyComponent.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionComponent.cs`](Corruption/WFCorruptionComponent.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Blood.cs`](Corruption/WFCorruptionSystem.Blood.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.cs`](Corruption/WFCorruptionSystem.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Doors.cs`](Corruption/WFCorruptionSystem.Doors.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Loose.cs`](Corruption/WFCorruptionSystem.Loose.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Nodes.cs`](Corruption/WFCorruptionSystem.Nodes.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Sanctuary.cs`](Corruption/WFCorruptionSystem.Sanctuary.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionSystem.Smoothing.cs`](Corruption/WFCorruptionSystem.Smoothing.cs)
- [`Content.Server/_WF/Remade/Corruption/WFCorruptionWallComponent.cs`](Corruption/WFCorruptionWallComponent.cs)
- [`Content.Server/_WF/Remade/Corruption/WFRemadeSanctuaryComponent.cs`](Corruption/WFRemadeSanctuaryComponent.cs)
- [`Content.Server/_WF/Remade/Corruption/WFRemadeSanctuaryCVars.cs`](Corruption/WFRemadeSanctuaryCVars.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFCounterplayEvents.cs`](Counterplay/WFCounterplayEvents.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFFeralComponent.cs`](Counterplay/WFFeralComponent.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFFeralSystem.cs`](Counterplay/WFFeralSystem.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFNecrotoxinProtectedComponent.cs`](Counterplay/WFNecrotoxinProtectedComponent.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFNecrotoxinSystem.cs`](Counterplay/WFNecrotoxinSystem.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFPrimeComponent.cs`](Counterplay/WFPrimeComponent.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFPrimeSystem.cs`](Counterplay/WFPrimeSystem.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFScorchedGridComponent.cs`](Counterplay/WFScorchedGridComponent.cs)
- [`Content.Server/_WF/Remade/Counterplay/WFScorchSystem.cs`](Counterplay/WFScorchSystem.cs)
- [`Content.Server/_WF/Remade/Critters/WFAbilitySystem.Critters.cs`](Critters/WFAbilitySystem.Critters.cs)
- [`Content.Server/_WF/Remade/Critters/WFCoverSystem.cs`](Critters/WFCoverSystem.cs)
- [`Content.Server/_WF/Remade/Critters/WFCritterComponents.cs`](Critters/WFCritterComponents.cs)
- [`Content.Server/_WF/Remade/Critters/WFCritterOperators.cs`](Critters/WFCritterOperators.cs)
- [`Content.Server/_WF/Remade/Critters/WFRemadeConversionSystem.Animals.cs`](Critters/WFRemadeConversionSystem.Animals.cs)
- [`Content.Server/_WF/Remade/Critters/WFWallClingSystem.cs`](Critters/WFWallClingSystem.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeBehaviourComponents.cs`](Director/WFRemadeBehaviourComponents.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorComponent.cs`](Director/WFRemadeDirectorComponent.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Behaviour.cs`](Director/WFRemadeDirectorSystem.Behaviour.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Build.cs`](Director/WFRemadeDirectorSystem.Build.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.cs`](Director/WFRemadeDirectorSystem.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Dead.cs`](Director/WFRemadeDirectorSystem.Dead.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Dread.cs`](Director/WFRemadeDirectorSystem.Dread.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Hearing.cs`](Director/WFRemadeDirectorSystem.Hearing.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Speech.cs`](Director/WFRemadeDirectorSystem.Speech.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Tension.cs`](Director/WFRemadeDirectorSystem.Tension.cs)
- [`Content.Server/_WF/Remade/Director/WFRemadeDirectorSystem.Vents.cs`](Director/WFRemadeDirectorSystem.Vents.cs)
- [`Content.Server/_WF/Remade/Dread/WFDarknessSystem.cs`](Dread/WFDarknessSystem.cs)
- [`Content.Server/_WF/Remade/Dread/WFDreadAlarmSystem.cs`](Dread/WFDreadAlarmSystem.cs)
- [`Content.Server/_WF/Remade/Dread/WFDreadSystem.cs`](Dread/WFDreadSystem.cs)
- [`Content.Server/_WF/Remade/Echoes/WFRemadeDirectorSystem.Echoes.cs`](Echoes/WFRemadeDirectorSystem.Echoes.cs)
- [`Content.Server/_WF/Remade/Echoes/WFRemadeEchoSystem.cs`](Echoes/WFRemadeEchoSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFBruteArmourComponent.cs`](Elites/WFBruteArmourComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFBruteSystem.cs`](Elites/WFBruteSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFDividerComponent.cs`](Elites/WFDividerComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFDividerSystem.cs`](Elites/WFDividerSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFDodgePoolComponent.cs`](Elites/WFDodgePoolComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFDodgePoolSystem.cs`](Elites/WFDodgePoolSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFEliteSystem.cs`](Elites/WFEliteSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFFlingOnHitComponent.cs`](Elites/WFFlingOnHitComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFReanimateHasteComponent.cs`](Elites/WFReanimateHasteComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFReanimateHasteSystem.cs`](Elites/WFReanimateHasteSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFReclaimComponent.cs`](Elites/WFReclaimComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFReclaimSystem.cs`](Elites/WFReclaimSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFRegenComponent.cs`](Elites/WFRegenComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFRegenSystem.cs`](Elites/WFRegenSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFRemadeLimbsSystem.Regrow.cs`](Elites/WFRemadeLimbsSystem.Regrow.cs)
- [`Content.Server/_WF/Remade/Elites/WFRisesComponent.cs`](Elites/WFRisesComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFRisesSystem.cs`](Elites/WFRisesSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFTauntBuffComponent.cs`](Elites/WFTauntBuffComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFTauntSystem.cs`](Elites/WFTauntSystem.cs)
- [`Content.Server/_WF/Remade/Elites/WFUndyingComponent.cs`](Elites/WFUndyingComponent.cs)
- [`Content.Server/_WF/Remade/Elites/WFUndyingSystem.cs`](Elites/WFUndyingSystem.cs)
- [`Content.Server/_WF/Remade/Environment/WFEnvDecorComponents.cs`](Environment/WFEnvDecorComponents.cs)
- [`Content.Server/_WF/Remade/Environment/WFEnvDecorSystem.cs`](Environment/WFEnvDecorSystem.cs)
- [`Content.Server/_WF/Remade/Garrison/WFGarrisonComponents.cs`](Garrison/WFGarrisonComponents.cs)
- [`Content.Server/_WF/Remade/Garrison/WFGarrisonCVars.cs`](Garrison/WFGarrisonCVars.cs)
- [`Content.Server/_WF/Remade/Garrison/WFGarrisonSystem.cs`](Garrison/WFGarrisonSystem.cs)
- [`Content.Server/_WF/Remade/Garrison/WFRemadeDirectorSystem.Garrison.cs`](Garrison/WFRemadeDirectorSystem.Garrison.cs)
- [`Content.Server/_WF/Remade/Gifts/WFCorruptionSystem.Shards.cs`](Gifts/WFCorruptionSystem.Shards.cs)
- [`Content.Server/_WF/Remade/Gifts/WFFleshComponents.cs`](Gifts/WFFleshComponents.cs)
- [`Content.Server/_WF/Remade/Gifts/WFFleshSystem.cs`](Gifts/WFFleshSystem.cs)
- [`Content.Server/_WF/Remade/Gifts/WFMatterComponents.cs`](Gifts/WFMatterComponents.cs)
- [`Content.Server/_WF/Remade/Gifts/WFMatterSystem.cs`](Gifts/WFMatterSystem.cs)
- [`Content.Server/_WF/Remade/Gifts/WFReachSystem.cs`](Gifts/WFReachSystem.cs)
- [`Content.Server/_WF/Remade/Gifts/WFShardComponent.cs`](Gifts/WFShardComponent.cs)
- [`Content.Server/_WF/Remade/Gifts/WFShardSystem.cs`](Gifts/WFShardSystem.cs)
- [`Content.Server/_WF/Remade/Limbs/WFRemadeLimbsSystem.cs`](Limbs/WFRemadeLimbsSystem.cs)
- [`Content.Server/_WF/Remade/Monolith/WFMonolithComponent.cs`](Monolith/WFMonolithComponent.cs)
- [`Content.Server/_WF/Remade/Monolith/WFMonolithSystem.cs`](Monolith/WFMonolithSystem.cs)
- [`Content.Server/_WF/Remade/Monolith/WFMonolithTapSystem.cs`](Monolith/WFMonolithTapSystem.cs)
- [`Content.Server/_WF/Remade/Morgue/WFMorgueComponents.cs`](Morgue/WFMorgueComponents.cs)
- [`Content.Server/_WF/Remade/Morgue/WFMorgueSystem.cs`](Morgue/WFMorgueSystem.cs)
- [`Content.Server/_WF/Remade/Morgue/WFRemadeDirectorSystem.Fakes.cs`](Morgue/WFRemadeDirectorSystem.Fakes.cs)
- [`Content.Server/_WF/Remade/Morgue/WFRemadeDirectorSystem.Morgue.cs`](Morgue/WFRemadeDirectorSystem.Morgue.cs)
- [`Content.Server/_WF/Remade/Omens/WFRemadeDirectorSystem.Omens.cs`](Omens/WFRemadeDirectorSystem.Omens.cs)
- [`Content.Server/_WF/Remade/Outbreaks/WFRemadeDirectorSystem.Outbreaks.cs`](Outbreaks/WFRemadeDirectorSystem.Outbreaks.cs)
- [`Content.Server/_WF/Remade/Roster/WFDiesWithoutLimbsComponent.cs`](Roster/WFDiesWithoutLimbsComponent.cs)
- [`Content.Server/_WF/Remade/Roster/WFExploderSystem.cs`](Roster/WFExploderSystem.cs)
- [`Content.Server/_WF/Remade/Roster/WFLimbDeathSystem.cs`](Roster/WFLimbDeathSystem.cs)
- [`Content.Server/_WF/Remade/Roster/WFRemadeExploderComponent.cs`](Roster/WFRemadeExploderComponent.cs)
- [`Content.Server/_WF/Remade/Roster/WFRemadeShellComponent.cs`](Roster/WFRemadeShellComponent.cs)
- [`Content.Server/_WF/Remade/Roster/WFRosterSystem.cs`](Roster/WFRosterSystem.cs)
- [`Content.Server/_WF/Remade/Roster/WFShellSystem.cs`](Roster/WFShellSystem.cs)
- [`Content.Server/_WF/Remade/Roster/WFSkirmishOperator.cs`](Roster/WFSkirmishOperator.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeClearSystem.cs`](Sector/WFRemadeClearSystem.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeCommand.cs`](Sector/WFRemadeCommand.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeDiscoveryRuleComponent.cs`](Sector/WFRemadeDiscoveryRuleComponent.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeDiscoveryRuleSystem.cs`](Sector/WFRemadeDiscoveryRuleSystem.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeSignalSystem.cs`](Sector/WFRemadeSignalSystem.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeSiteComponent.cs`](Sector/WFRemadeSiteComponent.cs)
- [`Content.Server/_WF/Remade/Sector/WFRemadeSiteSystem.cs`](Sector/WFRemadeSiteSystem.cs)
- [`Content.Server/_WF/Remade/Sector/WFScionSignalComponent.cs`](Sector/WFScionSignalComponent.cs)
- [`Content.Server/_WF/Remade/Siege/WFSiegeComponents.cs`](Siege/WFSiegeComponents.cs)
- [`Content.Server/_WF/Remade/Siege/WFSiegeCVars.cs`](Siege/WFSiegeCVars.cs)
- [`Content.Server/_WF/Remade/Siege/WFSiegeSystem.cs`](Siege/WFSiegeSystem.cs)
- [`Content.Server/_WF/Remade/Signals/WFAbilitySystem.Signals.cs`](Signals/WFAbilitySystem.Signals.cs)
- [`Content.Server/_WF/Remade/Signals/WFRemadeDirectorSystem.Signals.cs`](Signals/WFRemadeDirectorSystem.Signals.cs)
- [`Content.Server/_WF/Remade/Signals/WFSignalComponents.cs`](Signals/WFSignalComponents.cs)
- [`Content.Server/_WF/Remade/Signals/WFSignalSystem.cs`](Signals/WFSignalSystem.cs)
- [`Content.Server/_WF/Remade/Signals/WFSignalSystem.Leash.cs`](Signals/WFSignalSystem.Leash.cs)
- [`Content.Server/_WF/Remade/Signals/WFSignalSystem.Possession.cs`](Signals/WFSignalSystem.Possession.cs)
- [`Content.Server/_WF/Remade/Signals/WFSignalSystem.Tricks.cs`](Signals/WFSignalSystem.Tricks.cs)
- [`Content.Server/_WF/Remade/Structures/WFCystSystem.cs`](Structures/WFCystSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFEyeSystem.cs`](Structures/WFEyeSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFGrowthComponents.cs`](Structures/WFGrowthComponents.cs)
- [`Content.Server/_WF/Remade/Structures/WFGrowthSystem.cs`](Structures/WFGrowthSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFHarvesterSystem.cs`](Structures/WFHarvesterSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFMawSystem.cs`](Structures/WFMawSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFNestSystem.cs`](Structures/WFNestSystem.cs)
- [`Content.Server/_WF/Remade/Structures/WFSnareSystem.cs`](Structures/WFSnareSystem.cs)
- [`Content.Server/_WF/Remade/Torment/WFTormentedComponent.cs`](Torment/WFTormentedComponent.cs)
- [`Content.Server/_WF/Remade/Torment/WFTormentSystem.cs`](Torment/WFTormentSystem.cs)
- [`Content.Server/_WF/Remade/Variants/WFStuckBreakerComponent.cs`](Variants/WFStuckBreakerComponent.cs)
- [`Content.Server/_WF/Remade/Variants/WFStuckBreakerSystem.cs`](Variants/WFStuckBreakerSystem.cs)
- [`Content.Server/_WF/Remade/Vents/WFAbilitySystem.Ambush.cs`](Vents/WFAbilitySystem.Ambush.cs)
- [`Content.Server/_WF/Remade/Vents/WFRemadeDirectorSystem.Haul.cs`](Vents/WFRemadeDirectorSystem.Haul.cs)
- [`Content.Server/_WF/Remade/Vents/WFRemadeDirectorSystem.VentRun.cs`](Vents/WFRemadeDirectorSystem.VentRun.cs)
- [`Content.Server/_WF/Remade/Vents/WFVentComponents.cs`](Vents/WFVentComponents.cs)
- [`Content.Server/_WF/Remade/Vents/WFVentSystem.cs`](Vents/WFVentSystem.cs)
- [`Content.Server/_WF/Remade/Ward/WFWardComponents.cs`](Ward/WFWardComponents.cs)
- [`Content.Server/_WF/Remade/Ward/WFWardSystem.cs`](Ward/WFWardSystem.cs)

### Shared

- [`Content.Shared/_WF/Remade/Betrayal/WFBetrayalShared.cs`](../../../Content.Shared/_WF/Remade/Betrayal/WFBetrayalShared.cs)
- [`Content.Shared/_WF/Remade/Betrayal/WFShakenAim.cs`](../../../Content.Shared/_WF/Remade/Betrayal/WFShakenAim.cs)
- [`Content.Shared/_WF/Remade/Corruption/WFCorruptionVisuals.cs`](../../../Content.Shared/_WF/Remade/Corruption/WFCorruptionVisuals.cs)
- [`Content.Shared/_WF/Remade/Dread/WFDreadCVars.cs`](../../../Content.Shared/_WF/Remade/Dread/WFDreadCVars.cs)
- [`Content.Shared/_WF/Remade/Dread/WFDreadEvents.cs`](../../../Content.Shared/_WF/Remade/Dread/WFDreadEvents.cs)
- [`Content.Shared/_WF/Remade/Dread/WFDreadMath.cs`](../../../Content.Shared/_WF/Remade/Dread/WFDreadMath.cs)
- [`Content.Shared/_WF/Remade/Limbs/SharedWFRemadeLimbsSystem.cs`](../../../Content.Shared/_WF/Remade/Limbs/SharedWFRemadeLimbsSystem.cs)
- [`Content.Shared/_WF/Remade/Limbs/WFRemadeLimbsComponent.cs`](../../../Content.Shared/_WF/Remade/Limbs/WFRemadeLimbsComponent.cs)
- [`Content.Shared/_WF/Remade/Sector/WFRemadeSectorCVars.cs`](../../../Content.Shared/_WF/Remade/Sector/WFRemadeSectorCVars.cs)
- [`Content.Shared/_WF/Remade/Sector/WFRemadeSitePrototype.cs`](../../../Content.Shared/_WF/Remade/Sector/WFRemadeSitePrototype.cs)
- [`Content.Shared/_WF/Remade/Signals/WFSignalComponent.cs`](../../../Content.Shared/_WF/Remade/Signals/WFSignalComponent.cs)
- [`Content.Shared/_WF/Remade/Signals/WFSignalCVars.cs`](../../../Content.Shared/_WF/Remade/Signals/WFSignalCVars.cs)
- [`Content.Shared/_WF/Remade/Signals/WFSignalEvents.cs`](../../../Content.Shared/_WF/Remade/Signals/WFSignalEvents.cs)
- [`Content.Shared/_WF/Remade/Signals/WFSignalMath.cs`](../../../Content.Shared/_WF/Remade/Signals/WFSignalMath.cs)
- [`Content.Shared/_WF/Remade/Structures/WFGrowthShared.cs`](../../../Content.Shared/_WF/Remade/Structures/WFGrowthShared.cs)
- [`Content.Shared/_WF/Remade/Ward/WFWardFieldComponent.cs`](../../../Content.Shared/_WF/Remade/Ward/WFWardFieldComponent.cs)
- [`Content.Shared/_WF/Remade/WFHeavyPull.cs`](../../../Content.Shared/_WF/Remade/WFHeavyPull.cs)
- [`Content.Shared/_WF/Remade/WFMonolithMath.cs`](../../../Content.Shared/_WF/Remade/WFMonolithMath.cs)
- [`Content.Shared/_WF/Remade/WFMonolithTapComponent.cs`](../../../Content.Shared/_WF/Remade/WFMonolithTapComponent.cs)
- [`Content.Shared/_WF/Remade/WFMonolithTapDropSystem.cs`](../../../Content.Shared/_WF/Remade/WFMonolithTapDropSystem.cs)
- [`Content.Shared/_WF/Remade/WFMonolithUi.cs`](../../../Content.Shared/_WF/Remade/WFMonolithUi.cs)
- [`Content.Shared/_WF/Remade/WFRemadeAppearsDeadComponent.cs`](../../../Content.Shared/_WF/Remade/WFRemadeAppearsDeadComponent.cs)
- [`Content.Shared/_WF/Remade/WFRemadeAppearsDeadSystem.cs`](../../../Content.Shared/_WF/Remade/WFRemadeAppearsDeadSystem.cs)
- [`Content.Shared/_WF/Remade/WFRemadeMobPrototype.cs`](../../../Content.Shared/_WF/Remade/WFRemadeMobPrototype.cs)
- [`Content.Shared/_WF/Remade/WFRemadeShellVisuals.cs`](../../../Content.Shared/_WF/Remade/WFRemadeShellVisuals.cs)

### Client

- [`Content.Client/_WF/Remade/Betrayal/AmbientSoundSystem.Betrayal.cs`](../../../Content.Client/_WF/Remade/Betrayal/AmbientSoundSystem.Betrayal.cs)
- [`Content.Client/_WF/Remade/Betrayal/ContentAudioSystem.Betrayal.cs`](../../../Content.Client/_WF/Remade/Betrayal/ContentAudioSystem.Betrayal.cs)
- [`Content.Client/_WF/Remade/Betrayal/WFBetrayalFlickerSystem.cs`](../../../Content.Client/_WF/Remade/Betrayal/WFBetrayalFlickerSystem.cs)
- [`Content.Client/_WF/Remade/Betrayal/WFBetrayalMusicSystem.cs`](../../../Content.Client/_WF/Remade/Betrayal/WFBetrayalMusicSystem.cs)
- [`Content.Client/_WF/Remade/Betrayal/WFBetrayalSensesSystem.cs`](../../../Content.Client/_WF/Remade/Betrayal/WFBetrayalSensesSystem.cs)
- [`Content.Client/_WF/Remade/Betrayal/WFDreadBedSystem.Betrayal.cs`](../../../Content.Client/_WF/Remade/Betrayal/WFDreadBedSystem.Betrayal.cs)
- [`Content.Client/_WF/Remade/Corruption/WFCorruptionVisualizerSystem.cs`](../../../Content.Client/_WF/Remade/Corruption/WFCorruptionVisualizerSystem.cs)
- [`Content.Client/_WF/Remade/Dread/WFDreadBedSystem.cs`](../../../Content.Client/_WF/Remade/Dread/WFDreadBedSystem.cs)
- [`Content.Client/_WF/Remade/Dread/WFDreadGlimpseSystem.cs`](../../../Content.Client/_WF/Remade/Dread/WFDreadGlimpseSystem.cs)
- [`Content.Client/_WF/Remade/Dread/WFDreadOverlay.cs`](../../../Content.Client/_WF/Remade/Dread/WFDreadOverlay.cs)
- [`Content.Client/_WF/Remade/Dread/WFDreadVisionSystem.cs`](../../../Content.Client/_WF/Remade/Dread/WFDreadVisionSystem.cs)
- [`Content.Client/_WF/Remade/Dread/WFRemadeClearSystem.cs`](../../../Content.Client/_WF/Remade/Dread/WFRemadeClearSystem.cs)
- [`Content.Client/_WF/Remade/Limbs/WFReclaimTentacleOverlay.cs`](../../../Content.Client/_WF/Remade/Limbs/WFReclaimTentacleOverlay.cs)
- [`Content.Client/_WF/Remade/Limbs/WFReclaimTentacleSystem.cs`](../../../Content.Client/_WF/Remade/Limbs/WFReclaimTentacleSystem.cs)
- [`Content.Client/_WF/Remade/Limbs/WFRemadeLimbVisualizerSystem.cs`](../../../Content.Client/_WF/Remade/Limbs/WFRemadeLimbVisualizerSystem.cs)
- [`Content.Client/_WF/Remade/Signals/WFSignalHud.cs`](../../../Content.Client/_WF/Remade/Signals/WFSignalHud.cs)
- [`Content.Client/_WF/Remade/Signals/WFSignalOverlay.cs`](../../../Content.Client/_WF/Remade/Signals/WFSignalOverlay.cs)
- [`Content.Client/_WF/Remade/Signals/WFSignalVisionSystem.cs`](../../../Content.Client/_WF/Remade/Signals/WFSignalVisionSystem.cs)
- [`Content.Client/_WF/Remade/Ward/WFWardGlyphOverlay.cs`](../../../Content.Client/_WF/Remade/Ward/WFWardGlyphOverlay.cs)
- [`Content.Client/_WF/Remade/Ward/WFWardOverlay.cs`](../../../Content.Client/_WF/Remade/Ward/WFWardOverlay.cs)
- [`Content.Client/_WF/Remade/Ward/WFWardOverlaySystem.cs`](../../../Content.Client/_WF/Remade/Ward/WFWardOverlaySystem.cs)
- [`Content.Client/_WF/Remade/WFGlyphButton.cs`](../../../Content.Client/_WF/Remade/WFGlyphButton.cs)
- [`Content.Client/_WF/Remade/WFMonolithTapBoundUserInterface.cs`](../../../Content.Client/_WF/Remade/WFMonolithTapBoundUserInterface.cs)
- [`Content.Client/_WF/Remade/WFMonolithTapWindow.xaml`](../../../Content.Client/_WF/Remade/WFMonolithTapWindow.xaml)
- [`Content.Client/_WF/Remade/WFMonolithTapWindow.xaml.cs`](../../../Content.Client/_WF/Remade/WFMonolithTapWindow.xaml.cs)
- [`Content.Client/_WF/Remade/WFMonolithTheme.cs`](../../../Content.Client/_WF/Remade/WFMonolithTheme.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Remade/AbilitiesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/AbilitiesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/BetrayalTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/BetrayalTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/CorruptionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/CorruptionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/CounterplayTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/CounterplayTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/CrittersTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/CrittersTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/DirectorBehaviourTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/DirectorBehaviourTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/DreadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/DreadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/ElitesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/ElitesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/EnvironmentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/EnvironmentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/GiftsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/GiftsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/LimbsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/LimbsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/MenderTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/MenderTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/OmensTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/OmensTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/OpenItemsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/OpenItemsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/OutbreakTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/OutbreakTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/RosterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/RosterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/SectorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/SectorTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/SiegeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/SiegeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/SignalsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/SignalsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/StructuresTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/StructuresTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/SyntheticConversionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/SyntheticConversionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/TormentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/TormentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/VariantsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/VariantsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/WardTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/WardTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/WFRemadeDirectorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/WFRemadeDirectorTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Remade/WFRemadeSliceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Remade/WFRemadeSliceTest.cs)

### Unit tests

- [`Content.Tests/_WF/Remade/WFDreadMathTest.cs`](../../../Content.Tests/_WF/Remade/WFDreadMathTest.cs)
- [`Content.Tests/_WF/Remade/WFGiftsMathTest.cs`](../../../Content.Tests/_WF/Remade/WFGiftsMathTest.cs)
- [`Content.Tests/_WF/Remade/WFMonolithMathTest.cs`](../../../Content.Tests/_WF/Remade/WFMonolithMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Remade/betrayal.yml`](../../../Resources/Prototypes/_WF/Remade/betrayal.yml)
- [`Resources/Prototypes/_WF/Remade/corruption.yml`](../../../Resources/Prototypes/_WF/Remade/corruption.yml)
- [`Resources/Prototypes/_WF/Remade/counterplay.yml`](../../../Resources/Prototypes/_WF/Remade/counterplay.yml)
- [`Resources/Prototypes/_WF/Remade/critters.yml`](../../../Resources/Prototypes/_WF/Remade/critters.yml)
- [`Resources/Prototypes/_WF/Remade/director.yml`](../../../Resources/Prototypes/_WF/Remade/director.yml)
- [`Resources/Prototypes/_WF/Remade/dread.yml`](../../../Resources/Prototypes/_WF/Remade/dread.yml)
- [`Resources/Prototypes/_WF/Remade/elites.yml`](../../../Resources/Prototypes/_WF/Remade/elites.yml)
- [`Resources/Prototypes/_WF/Remade/environment.yml`](../../../Resources/Prototypes/_WF/Remade/environment.yml)
- [`Resources/Prototypes/_WF/Remade/gifts.yml`](../../../Resources/Prototypes/_WF/Remade/gifts.yml)
- [`Resources/Prototypes/_WF/Remade/mobs.yml`](../../../Resources/Prototypes/_WF/Remade/mobs.yml)
- [`Resources/Prototypes/_WF/Remade/monolith.yml`](../../../Resources/Prototypes/_WF/Remade/monolith.yml)
- [`Resources/Prototypes/_WF/Remade/openitems.yml`](../../../Resources/Prototypes/_WF/Remade/openitems.yml)
- [`Resources/Prototypes/_WF/Remade/roster.yml`](../../../Resources/Prototypes/_WF/Remade/roster.yml)
- [`Resources/Prototypes/_WF/Remade/sector.yml`](../../../Resources/Prototypes/_WF/Remade/sector.yml)
- [`Resources/Prototypes/_WF/Remade/siege.yml`](../../../Resources/Prototypes/_WF/Remade/siege.yml)
- [`Resources/Prototypes/_WF/Remade/signals.yml`](../../../Resources/Prototypes/_WF/Remade/signals.yml)
- [`Resources/Prototypes/_WF/Remade/sounds.yml`](../../../Resources/Prototypes/_WF/Remade/sounds.yml)
- [`Resources/Prototypes/_WF/Remade/splinter.yml`](../../../Resources/Prototypes/_WF/Remade/splinter.yml)
- [`Resources/Prototypes/_WF/Remade/structures.yml`](../../../Resources/Prototypes/_WF/Remade/structures.yml)
- [`Resources/Prototypes/_WF/Remade/variants.yml`](../../../Resources/Prototypes/_WF/Remade/variants.yml)
- [`Resources/Prototypes/_WF/Remade/ward.yml`](../../../Resources/Prototypes/_WF/Remade/ward.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Remade/betrayal.ftl`](../../../Resources/Locale/en-US/_WF/Remade/betrayal.ftl)
- [`Resources/Locale/en-US/_WF/Remade/corruption.ftl`](../../../Resources/Locale/en-US/_WF/Remade/corruption.ftl)
- [`Resources/Locale/en-US/_WF/Remade/counterplay.ftl`](../../../Resources/Locale/en-US/_WF/Remade/counterplay.ftl)
- [`Resources/Locale/en-US/_WF/Remade/director.ftl`](../../../Resources/Locale/en-US/_WF/Remade/director.ftl)
- [`Resources/Locale/en-US/_WF/Remade/dread.ftl`](../../../Resources/Locale/en-US/_WF/Remade/dread.ftl)
- [`Resources/Locale/en-US/_WF/Remade/echoes.ftl`](../../../Resources/Locale/en-US/_WF/Remade/echoes.ftl)
- [`Resources/Locale/en-US/_WF/Remade/elites.ftl`](../../../Resources/Locale/en-US/_WF/Remade/elites.ftl)
- [`Resources/Locale/en-US/_WF/Remade/environment.ftl`](../../../Resources/Locale/en-US/_WF/Remade/environment.ftl)
- [`Resources/Locale/en-US/_WF/Remade/gifts.ftl`](../../../Resources/Locale/en-US/_WF/Remade/gifts.ftl)
- [`Resources/Locale/en-US/_WF/Remade/limbs.ftl`](../../../Resources/Locale/en-US/_WF/Remade/limbs.ftl)
- [`Resources/Locale/en-US/_WF/Remade/openitems.ftl`](../../../Resources/Locale/en-US/_WF/Remade/openitems.ftl)
- [`Resources/Locale/en-US/_WF/Remade/remade.ftl`](../../../Resources/Locale/en-US/_WF/Remade/remade.ftl)
- [`Resources/Locale/en-US/_WF/Remade/roster.ftl`](../../../Resources/Locale/en-US/_WF/Remade/roster.ftl)
- [`Resources/Locale/en-US/_WF/Remade/sector.ftl`](../../../Resources/Locale/en-US/_WF/Remade/sector.ftl)
- [`Resources/Locale/en-US/_WF/Remade/siege.ftl`](../../../Resources/Locale/en-US/_WF/Remade/siege.ftl)
- [`Resources/Locale/en-US/_WF/Remade/signals.ftl`](../../../Resources/Locale/en-US/_WF/Remade/signals.ftl)
- [`Resources/Locale/en-US/_WF/Remade/structures.ftl`](../../../Resources/Locale/en-US/_WF/Remade/structures.ftl)
- [`Resources/Locale/en-US/_WF/Remade/variants.ftl`](../../../Resources/Locale/en-US/_WF/Remade/variants.ftl)

### Textures

- [`Resources/Textures/_WF/Remade/Effects/cutter.rsi/`](../../../Resources/Textures/_WF/Remade/Effects/cutter.rsi/)
- [`Resources/Textures/_WF/Remade/Effects/projectiles.rsi/`](../../../Resources/Textures/_WF/Remade/Effects/projectiles.rsi/)
- [`Resources/Textures/_WF/Remade/Effects/runes.rsi/`](../../../Resources/Textures/_WF/Remade/Effects/runes.rsi/)
- [`Resources/Textures/_WF/Remade/Effects/swinging_limbs.rsi/`](../../../Resources/Textures/_WF/Remade/Effects/swinging_limbs.rsi/)
- [`Resources/Textures/_WF/Remade/Environment/cocoon.rsi/`](../../../Resources/Textures/_WF/Remade/Environment/cocoon.rsi/)
- [`Resources/Textures/_WF/Remade/Environment/decor.rsi/`](../../../Resources/Textures/_WF/Remade/Environment/decor.rsi/)
- [`Resources/Textures/_WF/Remade/Interface/corruption_actions.rsi/`](../../../Resources/Textures/_WF/Remade/Interface/corruption_actions.rsi/)
- [`Resources/Textures/_WF/Remade/Interface/signal_actions.rsi/`](../../../Resources/Textures/_WF/Remade/Interface/signal_actions.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/brute.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/brute.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/brute_flesh.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/brute_flesh.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/divider.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/divider.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/divider_components.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/divider_components.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/divider_components_large.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/divider_components_large.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/exploder.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/exploder.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/exploder_enhanced.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/exploder_enhanced.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/fennec.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/fennec.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/guardian.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/guardian.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/hopper.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/hopper.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/hunter.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/hunter.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/infector.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/infector.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/infector_enhanced.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/infector_enhanced.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/leaper.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/leaper.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/leaper_enhanced.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/leaper_enhanced.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/lurker.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/lurker.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/lurker_malo.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/lurker_malo.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/mender.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/mender.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/puker.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/puker.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/puker_flayed.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/puker_flayed.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/slasher.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/slasher.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/slasher_carrion.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/slasher_carrion.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/slasher_desiccated.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/slasher_desiccated.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/slasher_enhanced.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/slasher_enhanced.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/spitter.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/spitter.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/swarmer.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/swarmer.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/tripod.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/tripod.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/twitcher.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/twitcher.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/twitcher_oracle.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/twitcher_oracle.rsi/)
- [`Resources/Textures/_WF/Remade/Mobs/undying.rsi/`](../../../Resources/Textures/_WF/Remade/Mobs/undying.rsi/)
- [`Resources/Textures/_WF/Remade/Objects/cutter.rsi/`](../../../Resources/Textures/_WF/Remade/Objects/cutter.rsi/)
- [`Resources/Textures/_WF/Remade/Objects/organs.rsi/`](../../../Resources/Textures/_WF/Remade/Objects/organs.rsi/)
- [`Resources/Textures/_WF/Remade/Objects/shard.rsi/`](../../../Resources/Textures/_WF/Remade/Objects/shard.rsi/)
- [`Resources/Textures/_WF/Remade/Shaders/dread.swsl`](../../../Resources/Textures/_WF/Remade/Shaders/dread.swsl)
- [`Resources/Textures/_WF/Remade/Shaders/ward.swsl`](../../../Resources/Textures/_WF/Remade/Shaders/ward.swsl)
- [`Resources/Textures/_WF/Remade/Structures/corruption.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/corruption.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/growth_large.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/growth_large.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/growth_wall.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/growth_wall.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/growths.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/growths.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/harvester.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/harvester.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/nest.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/nest.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/prime.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/prime.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/scion.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/scion.rsi/)
- [`Resources/Textures/_WF/Remade/Structures/wall_growth.rsi/`](../../../Resources/Textures/_WF/Remade/Structures/wall_growth.rsi/)

### Audio

- [`Resources/Audio/_WF/Remade/attacks/big_swoosh_1.ogg`](../../../Resources/Audio/_WF/Remade/attacks/big_swoosh_1.ogg)
- [`Resources/Audio/_WF/Remade/attacks/big_swoosh_2.ogg`](../../../Resources/Audio/_WF/Remade/attacks/big_swoosh_2.ogg)
- [`Resources/Audio/_WF/Remade/attacks/big_swoosh_3.ogg`](../../../Resources/Audio/_WF/Remade/attacks/big_swoosh_3.ogg)
- [`Resources/Audio/_WF/Remade/attributions.yml`](../../../Resources/Audio/_WF/Remade/attributions.yml)
- [`Resources/Audio/_WF/Remade/betrayal/attributions.yml`](../../../Resources/Audio/_WF/Remade/betrayal/attributions.yml)
- [`Resources/Audio/_WF/Remade/betrayal/breath_1.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/breath_1.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/breath_2.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/breath_2.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/breath_3.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/breath_3.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/knock_1.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/knock_1.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/knock_2.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/knock_2.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/knock_3.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/knock_3.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/knock_4.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/knock_4.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_credits_rock.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_credits_rock.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_credits_violin.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_credits_violin.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_danik_speech.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_danik_speech.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_nicole.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_nicole.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_pensive.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_pensive.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/music_unitology.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/music_unitology.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/scrape_1.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/scrape_1.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/scrape_2.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/scrape_2.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/scrape_3.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/scrape_3.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_1.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_1.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_10.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_10.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_2.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_2.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_3.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_3.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_4.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_4.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_5.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_5.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_6.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_6.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_7.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_7.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_8.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_8.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/step_9.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/step_9.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/stinger_1.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/stinger_1.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/stinger_2.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/stinger_2.ogg)
- [`Resources/Audio/_WF/Remade/betrayal/stinger_3.ogg`](../../../Resources/Audio/_WF/Remade/betrayal/stinger_3.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_death.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_death.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_pain_extreme.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_pain_extreme.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/brute/brute_shout_long.ogg`](../../../Resources/Audio/_WF/Remade/brute/brute_shout_long.ogg)
- [`Resources/Audio/_WF/Remade/cutter/plasma_cutter.ogg`](../../../Resources/Audio/_WF/Remade/cutter/plasma_cutter.ogg)
- [`Resources/Audio/_WF/Remade/cyst/cyst_fire_1.ogg`](../../../Resources/Audio/_WF/Remade/cyst/cyst_fire_1.ogg)
- [`Resources/Audio/_WF/Remade/cyst/cyst_fire_2.ogg`](../../../Resources/Audio/_WF/Remade/cyst/cyst_fire_2.ogg)
- [`Resources/Audio/_WF/Remade/cyst/cyst_fire_3.ogg`](../../../Resources/Audio/_WF/Remade/cyst/cyst_fire_3.ogg)
- [`Resources/Audio/_WF/Remade/cyst/cyst_fire_4.ogg`](../../../Resources/Audio/_WF/Remade/cyst/cyst_fire_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/arm_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/arm_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/head_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/head_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/head_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/head_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/head_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/head_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/head_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/head_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/component/leg_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/divider/component/leg_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_death.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_death.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_posession.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_posession.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_shout_long_6.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_shout_long_6.ogg)
- [`Resources/Audio/_WF/Remade/divider/divider_split.ogg`](../../../Resources/Audio/_WF/Remade/divider/divider_split.ogg)
- [`Resources/Audio/_WF/Remade/dread/attributions.yml`](../../../Resources/Audio/_WF/Remade/dread/attributions.yml)
- [`Resources/Audio/_WF/Remade/dread/bed_drone.ogg`](../../../Resources/Audio/_WF/Remade/dread/bed_drone.ogg)
- [`Resources/Audio/_WF/Remade/dread/bed_whisper_a.ogg`](../../../Resources/Audio/_WF/Remade/dread/bed_whisper_a.ogg)
- [`Resources/Audio/_WF/Remade/dread/bed_whisper_b.ogg`](../../../Resources/Audio/_WF/Remade/dread/bed_whisper_b.ogg)
- [`Resources/Audio/_WF/Remade/dread/bed_whisper_c.ogg`](../../../Resources/Audio/_WF/Remade/dread/bed_whisper_c.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_far_noise.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_far_noise.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_growl_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_growl_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_growl_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_growl_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_growl_3.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_growl_3.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_heart_beat.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_heart_beat.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_very_far_noise.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_very_far_noise.ogg)
- [`Resources/Audio/_WF/Remade/dread/menace_wail.ogg`](../../../Resources/Audio/_WF/Remade/dread/menace_wail.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_behind_you_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_behind_you_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_behind_you_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_behind_you_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_look_up_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_look_up_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_look_up_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_look_up_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_over_here_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_over_here_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_over_here_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_over_here_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_over_here_3.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_over_here_3.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_turn_around_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_turn_around_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/voice_turn_around_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/voice_turn_around_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_back_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_back_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_back_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_back_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_back_3.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_back_3.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_back_4.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_back_4.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_hit_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_hit_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_hit_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_hit_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_hit_3.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_hit_3.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_hit_4.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_hit_4.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_low_1.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_low_1.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_low_2.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_low_2.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_low_3.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_low_3.ogg)
- [`Resources/Audio/_WF/Remade/dread/whisper_low_4.ogg`](../../../Resources/Audio/_WF/Remade/dread/whisper_low_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_blast_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_blast_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_blast_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_blast_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_blast_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_blast_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_blast_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_blast_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_blast_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_blast_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_death_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_death_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_death_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_death_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_death_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_death_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_7.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_7.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_5.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_5.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_6.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_6.ogg)
- [`Resources/Audio/_WF/Remade/exploder/exploder_speech_7.ogg`](../../../Resources/Audio/_WF/Remade/exploder/exploder_speech_7.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_5.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_5.ogg)
- [`Resources/Audio/_WF/Remade/footstep/brute_step_6.ogg`](../../../Resources/Audio/_WF/Remade/footstep/brute_step_6.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_6.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_6.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_7.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_7.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/exploder_footstep_multi_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/infector_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/infector_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/infector_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/infector_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/infector_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/infector_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/infector_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/infector_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/leaper_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/leaper_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/leaper_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/leaper_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/leaper_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/leaper_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/leaper_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/leaper_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/leaper_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/footstep/leaper_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_6.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_6.ogg)
- [`Resources/Audio/_WF/Remade/footstep/lurker_footstep_7.ogg`](../../../Resources/Audio/_WF/Remade/footstep/lurker_footstep_7.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/footstep/tripod_footstep_6.ogg`](../../../Resources/Audio/_WF/Remade/footstep/tripod_footstep_6.ogg)
- [`Resources/Audio/_WF/Remade/footstep/twitcher_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/twitcher_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/twitcher_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/twitcher_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/twitcher_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/twitcher_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/twitcher_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/twitcher_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/footstep/undying_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/footstep/undying_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/footstep/undying_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/footstep/undying_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/footstep/undying_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/footstep/undying_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/footstep/undying_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/footstep/undying_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_death_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_death_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_death_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_death_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_death_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_death_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_death_4.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_death_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_flap_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_flap_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_flap_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_flap_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_flap_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_flap_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_flap_4.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_flap_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_flap_5.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_flap_5.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_work_1.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_work_1.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_work_2.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_work_2.ogg)
- [`Resources/Audio/_WF/Remade/infector/infector_work_3.ogg`](../../../Resources/Audio/_WF/Remade/infector/infector_work_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_claw_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_claw_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_claw_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_claw_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_death_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_death_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_death_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_death_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_death_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_death_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_tailswing_1.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_tailswing_1.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_tailswing_2.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_tailswing_2.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_tailswing_3.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_tailswing_3.ogg)
- [`Resources/Audio/_WF/Remade/leaper/leaper_tailswing_4.ogg`](../../../Resources/Audio/_WF/Remade/leaper/leaper_tailswing_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_attack_9.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_attack_9.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_4.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_5.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_5.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_climb_6.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_climb_6.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_death_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_death_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_death_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_death_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_death_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_death_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_7.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_7.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/lurker_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/lurker/lurker_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_fire_1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_fire_1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_fire_2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_fire_2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_fire_3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_fire_3.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_woosh1.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_woosh1.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_woosh2.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_woosh2.ogg)
- [`Resources/Audio/_WF/Remade/lurker/spine_woosh3.ogg`](../../../Resources/Audio/_WF/Remade/lurker/spine_woosh3.ogg)
- [`Resources/Audio/_WF/Remade/monolith/alarm.ogg`](../../../Resources/Audio/_WF/Remade/monolith/alarm.ogg)
- [`Resources/Audio/_WF/Remade/monolith/crescendo_song.ogg`](../../../Resources/Audio/_WF/Remade/monolith/crescendo_song.ogg)
- [`Resources/Audio/_WF/Remade/monolith/stage_growth.ogg`](../../../Resources/Audio/_WF/Remade/monolith/stage_growth.ogg)
- [`Resources/Audio/_WF/Remade/monolith/stage_harvest.ogg`](../../../Resources/Audio/_WF/Remade/monolith/stage_harvest.ogg)
- [`Resources/Audio/_WF/Remade/monolith/stage_hymn.ogg`](../../../Resources/Audio/_WF/Remade/monolith/stage_hymn.ogg)
- [`Resources/Audio/_WF/Remade/monolith/stage_rapture.ogg`](../../../Resources/Audio/_WF/Remade/monolith/stage_rapture.ogg)
- [`Resources/Audio/_WF/Remade/monolith/throb.ogg`](../../../Resources/Audio/_WF/Remade/monolith/throb.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_death_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_death_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_death_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_death_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_death_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_death_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_4.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_4.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_5.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_5.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_6.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_6.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_7.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_7.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_8.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_8.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_footstep_9.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_footstep_9.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/puker/puker_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/puker/puker_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_death_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_death_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_death_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_death_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_death_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_death_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_death_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_death_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher/slasher_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher/slasher_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_death_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_extreme.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_pain_extreme.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_6.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_shout_long_6.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/slasher_enhanced/eslasher_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_9.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_9.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_attack_extreme.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_attack_extreme.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_death_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_death_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_death_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_death_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_death_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_death_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_8.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_8.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_extreme.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_extreme.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_pain_extreme_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_pain_extreme_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/spitter/spitter_speech_5.ogg`](../../../Resources/Audio/_WF/Remade/spitter/spitter_speech_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_attack_9.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_attack_9.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_death_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_death_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_death_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_death_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_death_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_death_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_death_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_death_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_shout_long_6.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_shout_long_6.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_extract_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_3.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_4.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_4.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_5.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongue_lash_5.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_1.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_1.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_2.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_2.ogg)
- [`Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_3.ogg`](../../../Resources/Audio/_WF/Remade/tripod/tripod_tongueforce_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_death_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_death_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_death_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_death_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_death_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_death_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_death_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_death_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_8.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_8.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_9.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_9.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_pain_extreme.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_pain_extreme.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_5.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_shout_long_5.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/twitcher/twitcher_speech_5.ogg`](../../../Resources/Audio/_WF/Remade/twitcher/twitcher_speech_5.ogg)
- [`Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/Ubermorph_spawn_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_5.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_5.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_6.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_6.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_7.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_7.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_attack_8.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_attack_8.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_5.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_5.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_6.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_6.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_7.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_7.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_pain_8.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_pain_8.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_regen_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_regen_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_regen_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_regen_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_5.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_5.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_6.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_6.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_7.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_7.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_8.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_8.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_long_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_long_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_long_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_long_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_long_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_long_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_shout_long_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_shout_long_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_1.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_1.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_2.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_2.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_3.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_3.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_4.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_4.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_5.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_5.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_6.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_6.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_7.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_7.ogg)
- [`Resources/Audio/_WF/Remade/undying/undying_speech_8.ogg`](../../../Resources/Audio/_WF/Remade/undying/undying_speech_8.ogg)

### Guidebook

- [`Resources/ServerInfo/_WF/Remade/Guidebook/Ending.xml`](../../../Resources/ServerInfo/_WF/Remade/Guidebook/Ending.xml)
- [`Resources/ServerInfo/_WF/Remade/Guidebook/Fighting.xml`](../../../Resources/ServerInfo/_WF/Remade/Guidebook/Fighting.xml)
- [`Resources/ServerInfo/_WF/Remade/Guidebook/Monolith.xml`](../../../Resources/ServerInfo/_WF/Remade/Guidebook/Monolith.xml)
- [`Resources/ServerInfo/_WF/Remade/Guidebook/Remade.xml`](../../../Resources/ServerInfo/_WF/Remade/Guidebook/Remade.xml)

### Tools

- [`Tools/_WF/Remade/make_mender.py`](../../../Tools/_WF/Remade/make_mender.py)
- [`Tools/_WF/Remade/make_stage_stings.py`](../../../Tools/_WF/Remade/make_stage_stings.py)
- [`Tools/_WF/Remade/port_assets.py`](../../../Tools/_WF/Remade/port_assets.py)
- [`Tools/_WF/Remade/port_betrayal.py`](../../../Tools/_WF/Remade/port_betrayal.py)
- [`Tools/_WF/Remade/port_counterplay.py`](../../../Tools/_WF/Remade/port_counterplay.py)
- [`Tools/_WF/Remade/port_dread.py`](../../../Tools/_WF/Remade/port_dread.py)
- [`Tools/_WF/Remade/port_environment.py`](../../../Tools/_WF/Remade/port_environment.py)
- [`Tools/_WF/Remade/render_corruption.py`](../../../Tools/_WF/Remade/render_corruption.py)

### Docs

- [`Docs/_WF/Remade/PORT_BRIEF.md`](../../../Docs/_WF/Remade/PORT_BRIEF.md)

## Non-modular edits

- [`Content.Server/Lathe/LatheSystem.cs`](../../Lathe/LatheSystem.cs): lets a print from Monolith-made stock mark its product unsellable
- [`Content.Server/Materials/MaterialStorageSystem.cs`](../../Materials/MaterialStorageSystem.cs): Monolith-made material stays in the silo, or it would sell as sheets
- [`Resources/Fonts/_DS13/`](../../../Resources/Fonts/_DS13/): DS13's Shage Marker-script font (CC-BY 3.0, BlueNexus) for the Monolith Tap's glyphs.
- [`Resources/Prototypes/_NF/Guidebook/nf14.yml`](../../../Resources/Prototypes/_NF/Guidebook/nf14.yml): the Monolith, the Remade and how to end an outbreak
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): the plasma cutter and the monolith tap flatpack

<!-- WOLFGATE-GENERATED END -->
