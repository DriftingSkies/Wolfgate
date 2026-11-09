# Lattice

The sector's boundary. Reality anchors round Caelestinus Central hold off Kyphrus's Unmooring; the further from their
hold, the worse it gets. The field is banded Unison, Detune, Dissonance, Feedback and the Coda, whose name comes out garbled.
Crossing into another band shows a popup and a notification chip, and radars draw the band edges in place of the
drone rings: the band edges are the profile's (20, 34, 50 and 66 km), and the default worldgen's biomes change at the same
distances (`droneRingEdges: false`, so a map's drone rings never move them). Only the six primary anchors set the bands,
so the edges are near-circles that pull in only on the side of a lost primary. Secondary anchors (relays, a station's own)
carry their strength negated and hold only a local well of anchored space (`WFLatticeMath.LocalHold`, about 2 km for a
relay), so they never bend the far bands. The full design and build order are in `Docs/_WF/Lattice/DESIGN.md`.

**How it works.** `WFLatticeSystem` puts `WFLatticeFieldComponent` on every map with world generation and spawns the
profile's six primary anchors on a 3 km hexagon if the map has none. Each is a station loaded from the profile's
`stationMap` (`WFLatticeSystem.Stations.cs`, pushed outward clear of other grids); only if that load fails is a plain
round deck built in code instead. Every station has the anchor core (a glyph obelisk, a different one per anchor:
`WFLatticeAnchorCoreAlpha` to `Zeta`, 20,000 damage, and a 500 kW HV power source: it powers only what HV cable joins
to it, which the mapped station has and the plain deck does not) and a fortress shield
(`WFLatticeAnchorShield`, which needs no power). The shield's field is drawn as a rooted Ward in the profile's
`shieldTint` (purple): the Remade module's membrane and glyphs, tinted through `WFWardFieldComponent.Tint`. Hitting the
core or its shield calls the drones still loyal to the Lattice (`MonoDroneSpawnerT1`, 300 m out towards the attacker,
every 5 minutes at most). A core that is destroyed, or removed any other way mid-round (its station deleted or blown apart), is a breach: it
stops holding, the delta alarm sounds, and a global announcement and chip say reality is coming loose in its direction. The field's `Integrity` (primaries left out of six) goes to clients. Each anchor
(`WFLatticeAnchorComponent`) adds a hold that halves at the profile's `reach`; the bands' thresholds are the summed hold
at each band edge for the intact hexagon, so a lost anchor pulls the edges in, most on its side. Every second each
player's carrier gets a band and builds Drift (`WFDriftComponent`): the grid they are on, themselves in open space, or
on a planet, their ground or grid at the world's place in the sector. Each band has a ceiling (`maxDrift`: 20, 40, 65, then 100 only in the Coda) that Drift climbs to over about ten minutes and eases back down to from above. Ghosts are skipped, and Drift only changes while
someone is aboard. The field component carries the anchors and thresholds to clients. `WFLatticeMath` holds the field,
band, depth and garble sums.

**Drift in the head.** Apart from a carrier's Drift, a person holds their own (`WFDriftComponent.Mind`,
`WFDriftMindSystem`). A Monolith's dread feeds it (red on the gauge), and so does any entity with `WFDriftSource`: by
vicinity, falling off over its `range`, more when carried, and a lump on touch or pick-up (exotic matter has a weak one).
It fades at 0.4 a second once nothing feeds it. The Unmooring's hallucinations play off the greater of the two, and off
a map with no Lattice they play off the head alone with the `WFDefault` profile. The Choir and the Remade hold none.

**What a person feels.** The Lattice works out what each person aboard feels (`WFDriftComponent.Felt`, `FeltLevel`,
`WFLatticeSystem.Felt.cs`), which drives their hallucinations, gauge, screen warp and mutations: their ship's Drift, less
their anchor charm, less half while grounded by lodestar (`WFLodestar`: haloperidol, silver and iron; `WFGround`), less a
fifth asleep or half asleep on a sheltered ship, less a tenth for each sane crewmate within 8 m (at most 30%), and 15%
worse with nobody near. Sleep also clears Drift from the head three times faster. A ship in the Coda soaks Drift into
its hull (`residueRate` a minute up to `residueMax`), and its Drift never falls below that until a drydock's Clean Drift
(`cleanDriftPrice` a point, credit consoles only) scrubs it.

**Gauge.** `WFDriftMindSystem` shows the `WFDrift` alert, a dial among the status icons with a step per Drift level, filled to the greater of the two and red when a Monolith's
Drift is the greater;
it shows only while out of anchored space or holding Drift, and in a garbling band it shows an unreadable dial.

**Walls and spires.** A raised anchor shield is a wall like a rooted Monolith's Ward (`WFLatticeSystem.Barrier`, using
`WFWardSystem.HoldOut`): no ship but the station's docked ones, and nobody loose in space, gets inside it, so it has to be
brought down first. Inside, the old alien spires are `WFLatticeSpireTurret`s that snap round and fire flechette volleys at anyone aboard almost at once.
The station's hardpoints and lances lose their `Anchorable` and can't be unwrenched.
A core knocked loose (its deck blown out) is a breach like one destroyed or deleted. The shield generators can't be
unbolted or taken apart, only destroyed.

**Lost ships.** Every ten minutes or so there is a small chance (`lostShipChance`, more per lost anchor) that a wreck in the
Deep with no player within 600 m fills with Remade, is renamed "Lost ship" and shows its label on radar
(`WFLatticeSystem.LostShips`). Each lost primary anchor also makes the Coda's take of small animals half again as likely
(`takeLostScale`).

**Beams.** Clients draw a flowing violet beam between each pair of neighbouring primary anchors (`WFLatticeBeamOverlay`, shader `WFLatticeBeam`: braided strands, streaming filaments and comets),
starting at the shield edge (`beamOffset`). A lost anchor breaks the two beams that ran to it.

**FTL.** Bands with `ftl: false` (Feedback and the Coda) kill FTL drives (`WFLatticeSystem.Ftl.cs`): a ship there
can't jump (`ConsoleFTLAttemptEvent`, and the helm's drive status reads `Unmoored` off the ship's networked
`WFDriftComponent.NoFtl`), and the helm refuses a jump whose destination is in one. Ships have to fly in and out.

**The Coda's hazards.** For crewed ships in `hazardBand` (`WFCodaHazardSystem`): after `hunterGrace` (90 s) a roll
each minute, climbing over `hunterRamp` (8 min) to `hunterMaxChance`, puts a pack of 2-4 `hunterMobs` aboard on open floor at
least 6 m from the crew (5 min cooldown, at most 8 alive per ship); every 20 s 2-6 bolted-down structures take
`rotDamage` structural damage unless a stabiliser runs; and a `failureChance` roll every `failureInterval` (30 s at the
edge, down to about 10 s deep in and after five minutes) makes something give out for 8-20 s: power flickering on and
off ship-wide (`WFCodaFlutterComponent` on each APC), doors slamming, opening or bolting, half the thrusters, half the
gyroscopes, or every APC breaker. Flickers and doors are the commonest. On the client every scope, helm and mass scanner alike, goes blind in two
wandering wedges and fills with static (`ShuttleNavControl.Lattice.cs`): crawling specks and a rolling interference band,
denser deeper in, and bursts of snow that white the scope out for a moment, more often far out. Reduced motion slows the
static and drops the bursts. Relays and stabilisers don't stop hunters or failures.

**Twisted flesh.** In `mutationBand` at `mutationMinDrift` (80) or more and unsheltered, a person's arm can twist
into a Remade blade (`WFCodaMutationSystem`, `mutationChance` a minute, doubling at full Drift): `WFCodaArmBlade` in
one hand, unremovable. It withers away after `mutationWither` (10 min) out of the Coda.

**The leviathan** (`WFCodaHazardSystem.Leviathan.cs`, client `WFLeviathanSystem`). About once in 40 minutes (`leviathanChance`,
more often deeper; 20 min `leviathanCooldown` per ship) something vast finds a crewed ship in the Coda. It circles as
garbled contacts at the edge of the radar for 25 s, slides past beneath the hull (a dark shape drawn under the world,
violet-rimmed, two pale eyes) while the power flickers and screens shake, and half the time brushes the hull: a shove,
6 s of dead engines and up to `leviathanTear` (3) outer tiles torn away, dead-end spurs first, never under anyone or a helm
and never splitting the ship. Then it sinks away and leaves 8 Drift in every head aboard. It can't be fought.

**Flesh** (`WFCodaFleshSystem`). On an unsheltered crewed ship in the Coda, a flesh knot (`WFCodaFleshNode`) takes root
now and then on open floor away from the crew (`fleshChance`, at most `fleshMaxNodes` per ship). Every 6 s it grows a
tile or two of Remade corruption (`WFCorruptionSystem.GrowLoose`: the Monolith's floor and wall growth, but spreading
only from its knot) within 6 tiles, up to 40. The knot feeds Drift to anyone near. Kill it and its flesh dies over a
minute; out of the Coda it withers within 5-10 minutes; Clean Drift at a drydock strips it at once.

**Drones.** Only the low tiers spawn: T0 in the inner Wilds, T1 from Detune out, rarest in the Coda. The T2 spawners are
gone from every biome.

**The rot** (`WFCodaRotSystem`). A living player out in the Coda's open space, off any hull, is warned at once ("your skin
prickles"), again at half the grace, and when the grace (20 s) runs out: then they take 2 Cellular a second that no suit
stops (an anchor charm halves it), are reminded every 15 s, and at 45 s an arm twists into a blade. Death out there
rises Remade as usual. Back aboard, it stops and fades three times as fast. The Choir are exempt. `WFCodaRotCVars`
(`wf.lattice.coda_rot*`) tune it.

**Speaking of it.** A living speaker who says "Coda" stumbles on the word and feels it catch (`WFCodaTabooSystem`);
ghosts and the Choir say it plainly.

**The dead rise.** In the Coda (`riseBand`) a body that lies dead for `riseDelay` seconds (60) starts to turn Remade
(`WFDeepRisingSystem`, through `WFRemadeConversionSystem`), unless its ship or the body itself is sheltered. If it was
a player's, they are asked whether to rise with it and play the Remade; a no leaves it to the NPC AI.

**Dead anchors.** A lost anchor's station is renamed `DEAD ANCHOR <name>` and its IFF turns grey, so radar shows which
anchors are gone.

**Sky.** Clients regrade the space background by how far out the view is (`WFLatticeSkyOverlay`, shader
`WFLatticeSky`), easing between bands: Detune drains its colour, Dissonance turns it teal and swimming, Feedback rust-red
and dark, and the Coda nearly black, with crawling veins, a heartbeat at the edges and eyes that open and watch the ship.
It needs parallax on and holds its look through FTL until the ship arrives somewhere.

**Garbling.** In a garbling band (the Coda) text garbles more the deeper it is, and flickers on clients
(`WFLatticeViewSystem`): radar contact names by the deeper of the viewer and the contact, the console's own position and
the radar's cursor and scanner readouts by the viewer, and the Scion hint whispers' coordinates by where the hulk is
(`WFLatticeSystem.GarbleAt`).

**Drift effects.** `WFDriftEffectsSystem` rolls each player's hallucination every 20 s or so, more often the higher
their Drift, from the profile's `effects` list: whispers (popups), voices and false hails on the common channel, radar
contacts that aren't there, footsteps and knocks, glimpses, and from 70 Drift a Remade standing in the dark that goes
when approached. Each goes to that one player only: chat and sound by a single-player filter, figures and contacts as a
`WFLatticeHallucinationEvent` that `WFLatticeHallucinationSystem` shows client-side. The rolls close from every
`effectInterval` (20 s) towards `effectIntervalAtMax` (4 s) as Drift climbs, and from 80 Drift two come at once,
three from 95. Higher up: blood round them (40), a crewmate saying something they never said (50), a crewmate seen as a
Remade for a few seconds (60), a ring of Remade that vanish when approached (80), and themselves seen as a Remade (90).
Past 90 the screen warp climbs steeply. Some lie about the game itself (`WFDriftEffectsSystem.Lies.cs`): their own
ship twice on radar (45), echoes, where sounds round them play again from the other side (50), a door or person that isn't
there for a few seconds (55), a false oxygen, pressure or heat warning (`WFFalse*` alerts, 60), and a silence where every
sound stops and then everything missed plays at once (85; `WFDriftSoundSystem` on the client). Some tell the truth: a
third of radar blips are a real lost ship or Monolith within 4 km, and a knowing (50) names a place out there that is real
60% of the time. Blood, glimpses, phantoms and swarms are shared with anyone else aboard also past 70.

**Band signatures** (`WFBandSignatureSystem`, client `WFDriftSoundSystem`): in Detune sound goes dull, in Dissonance a
door a player opens waits a beat first, and in Feedback a player's words sometimes come back to them on the radio, a
little wrong.

**Loggers, tissue and couriers.** The drift logger (autolathe) counts readings while carried, 1, 3, 8 and 20 a minute from
Detune to the Coda, up to 1,500, and sells for 4 cr a reading. A twisted arm can be cut away in surgery
(`WFSurgeryCutTwistedArm`), leaving Remade tissue (2,500 cr). A ship moving a Monolith faster than 1 m/s puts Drift in
every head aboard and can't hide its name on radar (`WFDriftLoggerSystem`). Rarely, from 40 Drift, a pulse hits:
vision closes to a throbbing tunnel ringed with red glyphs for about three seconds over a sub-bass heartbeat
(`WFDriftPulseOverlay`, `drift_pulse.ogg`); it holds still with reduced motion. Admins can show any of them with
`wfhallucinate <kind> [username]`. From 30 Drift the screen warps
(`WFDriftOverlay`, the drunk shader), off the carrier's networked Drift `Level`. In the Coda (`takeBand`) a crewed ship
can lose a small animal (under 30 kg) about once in 25 minutes: it dies and turns, through the Remade conversion. A
`WFLatticeShelterComponent` on the ship (or a person) keeps a share of the Drift off and stops the taking.

**Rocks.** Each band out has fewer rocks than the last (`WFDensityDetune` to `WFDensityCoda` in `worldgen.yml`), and
the inner zone's are thinned too, so the rich rocks are a long trip out.

**Rewards and shelter.** Asteroid rocks broken out in the bands drop more ore (each band's `yield`, up to 1.8 in the
Coda, through `WFLatticeSystem.YieldAt` in `WFAsteroidSystem`) and, in the Coda only, exotic matter
(`WFMaterialExoticMatter`, 3,000 a shard, `exotic` shards per vein). The autolathe makes two things from it: the Drift
stabiliser flatpack (5 shards; 15 kW while running), which shelters the ship it is anchored on from 80% of Drift gain
and stops the taking, and the anchor charm (1 shard), worn round the neck, which keeps half the Drift off its wearer.
`WFLatticeShelterSystem` keeps the `WFLatticeShelterComponent` on ships and wearers.

**Anchor stations and their defence.** Each station is loaded from `Resources/Maps/_WF/Lattice/anchor_station.yml`
(the profile's `stationMap`; a plain deck is built in code if it fails), placed so its core sits on the hexagon's
corner, and its core is swapped for that anchor's own. The core is also a station anchor (`StationAnchor`), so the
station can't be moved. Its eight hardpoints carry `WFLatticeWardenLance`s, which fire only from an anchor station.
`WFLatticeSystem.Defence.cs` is slow to anger. Shield and core hits are counted per ship (forgiven after 60 s); ten
(`strikeLimit`) from one ship put out a global "under fire" alert, and nothing more while the shield holds. Once the
station's shield is down it alerts again, calls its drones, and turns every lance on the nearest ship that has fired on
it in the last 20 s and still has a living player or NPC aboard, within 3 km; it stops when the shield comes back or the
ship is dead. Shield hits are slow to anger, but damage to anything on the station itself (`WFLatticeStationPartComponent`,
marked every 30 s) or to the core is answered at once (`Provoke`): the alert goes out, the drones come, and the lances
fire on the attacker for 20 s whether the shield holds or not. The attacker is the person who fired a hand weapon,
wherever they stand, or the ship a mounted gun fired from, so nobody else aboard is caught up in it. Other anchor stations never count as attackers. The station also hums: `WFLatticeHumSystem` plays the
Goonstation flock reactor loop to a player within 450 m of an anchor, louder the closer they are.

**Relay anchors.** The autolathe makes a relay anchor flatpack (20 shards of exotic matter). Bolted down with 30 kW,
on a grid moving under 1 m/s for a minute, a relay holds 0.4 of a primary's strength and pushes anchored space out
round it; it lets go when the grid moves or the power fails. It also runs on exotic matter: it starts empty, takes
shards by hand (up to 6), and each shard buys 10 minutes of holding (`WFLatticeSystem.RelayFuel.cs`). Mapped station
anchors (`WFLatticeStationAnchor`) don't need fuel.

**Kyphrus.** The black hole shader takes `unmooring` (0 to 1) from `WFLatticeViewSystem.Unmooring`: the share of
primary anchors lost, or at least 0.6 (rising with depth) while the viewer is in the Coda. It strengthens the lensing,
makes the disk's turning skip, leans its colour from orange to violet, adds a counter-turning ghost image of the disk
and tears the edge of the shadow (`black_hole.swsl`, set in `StarOverlay.BlackHole.cs`).

**The Choir.** A player at 80 Drift or more is offered the song once a round (`WFChoirSystem`): a popup, a line in chat
and an "Answer the song" action for five minutes. Using it is the only way in: two confirmation dialogs spell out the
coin toss, then half join and half die where they stand and rise as Remade, with the player in the Remade
(`WFDeepRisingSystem.Raise`). Members are `Unrevivable`; one lying dead 30 s turns Remade and takes the player with it.
Members show a white C on red (`WFChoirFaction`) to ghosts, admins and each other, carry the `WFMindRoleChoir`
antagonist role, and their PDA examines with red glyphs (`WFChoirMarkComponent`). Surgeons can cut the song out
(`WFSurgeryExciseSong`, `WFSongExcisionSystem`): saw the skull, then excise with a shard of exotic matter (used up),
15 s, 30 brain damage. 70% leave the Choir and can be revived again; 30% die on the table and rise Remade with the
player. Not in the Coda. A Choir player takes the `WFChoir`
faction (the Choir's NPC ships and the Remade leave them be), is untouched by Drift, its gauge, screen sway and garbled readouts (the Monolith's dread still reaches them, though it adds no Drift), and is briefed to break anchors
and carry Scions inward as an antagonist. Admins get an alert for each offer and join, and joins are admin-logged.
Removing `WFChoirMemberComponent` takes them out again. `wf.lattice.choir`, `wf.lattice.choir_min_players` (15),
`wf.lattice.choir_max` (3) and `wf.lattice.choir_min_drift` (80) gate it. Each Choir player adds 2 to the DEFCON
score, and each lost primary anchor 25 (`breachDefcon`) and holds DEFCON a step worse per anchor lost (one lost is at
least 4, four is 1).

**Coda-only encounters.** An encounter with `latticeBand` above zero is placed at its distances from the sector's centre
and only where the band is at least that; with no Lattice it falls back to distances from a player. The Monolith hulk,
the guarded hulk and the adrift freighter use band 4 at 68-76 km.

**Lore and guide.** The lore book keeps The Unmooring, The Lattice and The Choir in its Forbidden chapter
(`Resources/ServerInfo/_WF/LoreBook`); The Sector Today, The Collapse and The Monolith link to them. The guidebook page
The Lattice and Drift (`Resources/ServerInfo/_WF/Lattice/Guidebook`) covers the bands, Drift, gear and anchors. Anchors,
relays, exotic matter, the stabiliser and the charm have a Lore button on examine (`guidebook.yml`).

**Tuning.** `Resources/Prototypes/_WF/Lattice/lattice.yml`: the `WFDefault` profile's anchor layout, reach, Drift cap
and per-band edge, Drift rate, growth with depth, chip style and garbling.

**The hex field** (`WFLatticeSystem.Hex.cs`, `WFLatticeHex`). The beams between neighbouring primary anchors make a
ring; a beam stands while both its anchors do, and the hex is sealed while every beam stands. No Monolith crosses a
standing beam inwards: whatever carries it (its ship and everything docked to it, or whoever holds it in open space) is set
back outside and its inward speed is thrown back. A broken beam lets it through. A ship carrying a Scion can't jump inside a
sealed hex, and one that arrives inside it by any jump is put back out. Each beam has a slice of the hex, from the beam
in to Central's circle (its grid plus 60 m). A slice whose beam stands is a sanctuary: a Monolith can't be drawn on, grow
corruption or reach anyone in it from outside. A broken beam opens its slice, and while any beam is broken Central's
circle is open too, so the Monolith reaches Caelestinus Central; with a Lattice the main station has no sanctuary of its own.

**Wards.** Each anchor keeps corruption off grids within its `ward` (primaries 600 m, relays and station anchors
150 m). Caelestinus Central gets its own anchor (`WFLatticeCentralAnchor`, 20,000 damage, an 800 m ward) railed in at
tile (1, 17) and a Lattice status console at (-19, -6) facing east, unless an anchor is mapped there. Damaging it names
the attacker to the sector; destroying it is announced. Players aboard an anchor station are named as trespassers, all who came aboard in one announcement, as a silent banner at most every
ten minutes a station, and each again only after five minutes. A station alerts the sector at most every five minutes (`alertCooldown`); only a direct hit sounds the alarm.

**Ghosts** can warp to every anchor, Central's included, from the ghost menu.

**Radar.** Every radar and mass scanner draws the hex (`ShuttleNavControl.Hex.cs`): violet beams, dashed red where
broken, a faint tint inside while it is sealed, and a ring round each anchor's ward.

**Status app.** The autolathe prints a Lattice status cartridge (PDA app) and a status console flatpack. Both show
`WFLatticeStatusPanel` (client): the hex drawn as glyph nodes with comets running along standing beams and torn red ones
where broken, Central's anchor at the heart, flesh ships circling as red motes, and readouts for state, integrity, the
hex, Central's anchor, flesh ships, the viewer's band (garbled where the band garbles) and each anchor by name. It reads
only the networked field (`Hex`, `HexUp`, `HexAlive`, `HexNames`, `CentralStanding`, `FleshShips`, `Unstrung`, `Tacet`).

**Fading.** A lost anchor's hold doesn't vanish: it fades where it stood over `wf.lattice.anchor_fade` (20 minutes), so
the bands creep inward instead of jumping.

**The end** (`WFLatticeSystem.Endgame.cs`). When the last primary anchor falls, the Unstringing: every relay and station
anchor lets go too, all fading the same way, so over 20 minutes the Coda, its hazards, its rot and its dead FTL engulf the
whole sector. Some 15 to 30 minutes after it has fully set in comes Tacet: Kyphrus implodes, every bulb in the sector bursts and new ones keep failing (a quarter every 30 s),
and clients stop drawing the black hole. `WFLatticeEndgameCVars` (`wf.lattice.tacet*`) tune it. Admins use
`wflattice siege|unstring|tacet`.

**Admins.** Spawn `WFLatticeAnchorRelayMarker` (0.4 strength) to push the field out, or delete a primary anchor to
open a breach. A carrier's band and Drift are on its `WFDriftComponent` in VV.


<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Lattice/ShuttleConsoleSystem.Lattice.cs`](ShuttleConsoleSystem.Lattice.cs)
- [`Content.Server/_WF/Lattice/WFBandSignatureSystem.cs`](WFBandSignatureSystem.cs)
- [`Content.Server/_WF/Lattice/WFChoirRoleComponent.cs`](WFChoirRoleComponent.cs)
- [`Content.Server/_WF/Lattice/WFChoirSystem.cs`](WFChoirSystem.cs)
- [`Content.Server/_WF/Lattice/WFCodaFleshSystem.cs`](WFCodaFleshSystem.cs)
- [`Content.Server/_WF/Lattice/WFCodaFlutterComponent.cs`](WFCodaFlutterComponent.cs)
- [`Content.Server/_WF/Lattice/WFCodaHazardSystem.cs`](WFCodaHazardSystem.cs)
- [`Content.Server/_WF/Lattice/WFCodaHazardSystem.Leviathan.cs`](WFCodaHazardSystem.Leviathan.cs)
- [`Content.Server/_WF/Lattice/WFCodaMutationSystem.cs`](WFCodaMutationSystem.cs)
- [`Content.Server/_WF/Lattice/WFCodaRotSystem.cs`](WFCodaRotSystem.cs)
- [`Content.Server/_WF/Lattice/WFCodaTabooSystem.cs`](WFCodaTabooSystem.cs)
- [`Content.Server/_WF/Lattice/WFDeepRisingSystem.cs`](WFDeepRisingSystem.cs)
- [`Content.Server/_WF/Lattice/WFDriftEffectsSystem.cs`](WFDriftEffectsSystem.cs)
- [`Content.Server/_WF/Lattice/WFDriftEffectsSystem.Lies.cs`](WFDriftEffectsSystem.Lies.cs)
- [`Content.Server/_WF/Lattice/WFDriftLoggerSystem.cs`](WFDriftLoggerSystem.cs)
- [`Content.Server/_WF/Lattice/WFDriftMindSystem.cs`](WFDriftMindSystem.cs)
- [`Content.Server/_WF/Lattice/WFHallucinateCommand.cs`](WFHallucinateCommand.cs)
- [`Content.Server/_WF/Lattice/WFLatticeEndCommand.cs`](WFLatticeEndCommand.cs)
- [`Content.Server/_WF/Lattice/WFLatticeShelterSystem.cs`](WFLatticeShelterSystem.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Barrier.cs`](WFLatticeSystem.Barrier.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.cs`](WFLatticeSystem.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Defence.cs`](WFLatticeSystem.Defence.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Endgame.cs`](WFLatticeSystem.Endgame.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Felt.cs`](WFLatticeSystem.Felt.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Ftl.cs`](WFLatticeSystem.Ftl.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Hex.cs`](WFLatticeSystem.Hex.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.LostShips.cs`](WFLatticeSystem.LostShips.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.RelayFuel.cs`](WFLatticeSystem.RelayFuel.cs)
- [`Content.Server/_WF/Lattice/WFLatticeSystem.Stations.cs`](WFLatticeSystem.Stations.cs)
- [`Content.Server/_WF/Lattice/WFSongExcisionSystem.cs`](WFSongExcisionSystem.cs)

### Shared

- [`Content.Shared/_WF/Lattice/WFChoir.cs`](../../../Content.Shared/_WF/Lattice/WFChoir.cs)
- [`Content.Shared/_WF/Lattice/WFCodaMutation.cs`](../../../Content.Shared/_WF/Lattice/WFCodaMutation.cs)
- [`Content.Shared/_WF/Lattice/WFDriftComponent.cs`](../../../Content.Shared/_WF/Lattice/WFDriftComponent.cs)
- [`Content.Shared/_WF/Lattice/WFDriftLoggerComponent.cs`](../../../Content.Shared/_WF/Lattice/WFDriftLoggerComponent.cs)
- [`Content.Shared/_WF/Lattice/WFDriftSourceComponent.cs`](../../../Content.Shared/_WF/Lattice/WFDriftSourceComponent.cs)
- [`Content.Shared/_WF/Lattice/WFGround.cs`](../../../Content.Shared/_WF/Lattice/WFGround.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeAnchorComponent.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeAnchorComponent.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeEndgameCVars.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeEndgameCVars.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeFieldComponent.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeFieldComponent.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeHallucination.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeHallucination.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeHex.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeHex.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeMath.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeMath.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeProfilePrototype.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeProfilePrototype.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeShelterComponent.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeShelterComponent.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeShelterSources.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeShelterSources.cs)
- [`Content.Shared/_WF/Lattice/WFLatticeStatus.cs`](../../../Content.Shared/_WF/Lattice/WFLatticeStatus.cs)
- [`Content.Shared/_WF/Lattice/WFLeviathan.cs`](../../../Content.Shared/_WF/Lattice/WFLeviathan.cs)
- [`Content.Shared/_WF/Lattice/WFSongExcision.cs`](../../../Content.Shared/_WF/Lattice/WFSongExcision.cs)

### Client

- [`Content.Client/_WF/Lattice/ShuttleNavControl.Hex.cs`](../../../Content.Client/_WF/Lattice/ShuttleNavControl.Hex.cs)
- [`Content.Client/_WF/Lattice/ShuttleNavControl.Lattice.cs`](../../../Content.Client/_WF/Lattice/ShuttleNavControl.Lattice.cs)
- [`Content.Client/_WF/Lattice/WFChoirIconSystem.cs`](../../../Content.Client/_WF/Lattice/WFChoirIconSystem.cs)
- [`Content.Client/_WF/Lattice/WFDriftOverlay.cs`](../../../Content.Client/_WF/Lattice/WFDriftOverlay.cs)
- [`Content.Client/_WF/Lattice/WFDriftPulseOverlay.cs`](../../../Content.Client/_WF/Lattice/WFDriftPulseOverlay.cs)
- [`Content.Client/_WF/Lattice/WFDriftSoundSystem.cs`](../../../Content.Client/_WF/Lattice/WFDriftSoundSystem.cs)
- [`Content.Client/_WF/Lattice/WFLatticeBeamOverlay.cs`](../../../Content.Client/_WF/Lattice/WFLatticeBeamOverlay.cs)
- [`Content.Client/_WF/Lattice/WFLatticeHallucinationSystem.cs`](../../../Content.Client/_WF/Lattice/WFLatticeHallucinationSystem.cs)
- [`Content.Client/_WF/Lattice/WFLatticeHumSystem.cs`](../../../Content.Client/_WF/Lattice/WFLatticeHumSystem.cs)
- [`Content.Client/_WF/Lattice/WFLatticeRings.cs`](../../../Content.Client/_WF/Lattice/WFLatticeRings.cs)
- [`Content.Client/_WF/Lattice/WFLatticeSkyOverlay.cs`](../../../Content.Client/_WF/Lattice/WFLatticeSkyOverlay.cs)
- [`Content.Client/_WF/Lattice/WFLatticeStatusPanel.cs`](../../../Content.Client/_WF/Lattice/WFLatticeStatusPanel.cs)
- [`Content.Client/_WF/Lattice/WFLatticeStatusUi.cs`](../../../Content.Client/_WF/Lattice/WFLatticeStatusUi.cs)
- [`Content.Client/_WF/Lattice/WFLatticeViewSystem.cs`](../../../Content.Client/_WF/Lattice/WFLatticeViewSystem.cs)
- [`Content.Client/_WF/Lattice/WFLeviathanSystem.cs`](../../../Content.Client/_WF/Lattice/WFLeviathanSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Lattice/WFChoirTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFChoirTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFCodaHazardTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFCodaHazardTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFCodaRotTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFCodaRotTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFDeepRisingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFDeepRisingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFDriftDepthTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFDriftDepthTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFDriftEffectsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFDriftEffectsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFDriftMindTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFDriftMindTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeFtlTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeFtlTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeHexTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeHexTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeShelterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeShelterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeStationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeStationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeTrespassTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFLatticeTrespassTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Lattice/WFSongExcisionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Lattice/WFSongExcisionTest.cs)

### Unit tests

- [`Content.Tests/_WF/Lattice/WFLatticeMathTest.cs`](../../../Content.Tests/_WF/Lattice/WFLatticeMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Lattice/alerts.yml`](../../../Resources/Prototypes/_WF/Lattice/alerts.yml)
- [`Resources/Prototypes/_WF/Lattice/anchors.yml`](../../../Resources/Prototypes/_WF/Lattice/anchors.yml)
- [`Resources/Prototypes/_WF/Lattice/choir.yml`](../../../Resources/Prototypes/_WF/Lattice/choir.yml)
- [`Resources/Prototypes/_WF/Lattice/drift.yml`](../../../Resources/Prototypes/_WF/Lattice/drift.yml)
- [`Resources/Prototypes/_WF/Lattice/exotic.yml`](../../../Resources/Prototypes/_WF/Lattice/exotic.yml)
- [`Resources/Prototypes/_WF/Lattice/guidebook.yml`](../../../Resources/Prototypes/_WF/Lattice/guidebook.yml)
- [`Resources/Prototypes/_WF/Lattice/lattice.yml`](../../../Resources/Prototypes/_WF/Lattice/lattice.yml)
- [`Resources/Prototypes/_WF/Lattice/reagents.yml`](../../../Resources/Prototypes/_WF/Lattice/reagents.yml)
- [`Resources/Prototypes/_WF/Lattice/shaders.yml`](../../../Resources/Prototypes/_WF/Lattice/shaders.yml)
- [`Resources/Prototypes/_WF/Lattice/status.yml`](../../../Resources/Prototypes/_WF/Lattice/status.yml)
- [`Resources/Prototypes/_WF/Lattice/weapons.yml`](../../../Resources/Prototypes/_WF/Lattice/weapons.yml)
- [`Resources/Prototypes/_WF/Lattice/worldgen.yml`](../../../Resources/Prototypes/_WF/Lattice/worldgen.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Lattice/lattice.ftl`](../../../Resources/Locale/en-US/_WF/Lattice/lattice.ftl)

### Textures

- [`Resources/Textures/_WF/Lattice/choir_icon.rsi/`](../../../Resources/Textures/_WF/Lattice/choir_icon.rsi/)
- [`Resources/Textures/_WF/Lattice/drift_alert.rsi/`](../../../Resources/Textures/_WF/Lattice/drift_alert.rsi/)
- [`Resources/Textures/_WF/Lattice/Shaders/drift_pulse.swsl`](../../../Resources/Textures/_WF/Lattice/Shaders/drift_pulse.swsl)
- [`Resources/Textures/_WF/Lattice/Shaders/lattice_beam.swsl`](../../../Resources/Textures/_WF/Lattice/Shaders/lattice_beam.swsl)
- [`Resources/Textures/_WF/Lattice/Shaders/lattice_sky.swsl`](../../../Resources/Textures/_WF/Lattice/Shaders/lattice_sky.swsl)

### Audio

- [`Resources/Audio/_WF/Lattice/attributions.yml`](../../../Resources/Audio/_WF/Lattice/attributions.yml)
- [`Resources/Audio/_WF/Lattice/drift_pulse.ogg`](../../../Resources/Audio/_WF/Lattice/drift_pulse.ogg)

### Maps

- [`Resources/Maps/_WF/Lattice/anchor_station.yml`](../../../Resources/Maps/_WF/Lattice/anchor_station.yml)

### Guidebook

- [`Resources/ServerInfo/_WF/Lattice/Guidebook/Lattice.xml`](../../../Resources/ServerInfo/_WF/Lattice/Guidebook/Lattice.xml)

### Tools

- [`Tools/_WF/Lattice/gen_drift_alert.py`](../../../Tools/_WF/Lattice/gen_drift_alert.py)

### Docs

- [`Docs/_WF/Lattice/DESIGN.md`](../../../Docs/_WF/Lattice/DESIGN.md)

## Non-modular edits

- [`Content.Client/Shuttles/UI/NavScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/NavScreen.xaml.cs): the ship's own position garbles too.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - radar contacts only this player sees.
  - the hex field's beams and the anchors' wards.
  - the Coda shows only as static on every scanner.
  - contact names garble out past the Lattice's hold.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.FTL.cs)
  - drives don't work past the Lattice's hold
  - no jumping into a band where drives die
- [`Resources/Maps/_Mono/POI/pdvhelios.yml`](../../../Resources/Maps/_Mono/POI/pdvhelios.yml): Mapped a station anchor beside an APC so Helios stays anchored space past the Lattice's hold.
- [`Resources/Maps/_Mono/POI/underground.yml`](../../../Resources/Maps/_Mono/POI/underground.yml): Mapped a station anchor beside an APC so the Syndicate FOB stays anchored space past the Lattice's hold.
- [`Resources/Prototypes/_Mono/Entities/World/Biome/basic.yml`](../../../Resources/Prototypes/_Mono/Entities/World/Biome/basic.yml)
  - fewer rocks near home, so the good ones are a trip out
  - no high-tier drones anywhere
  - anchored space reaches 20 km
  - wider Lattice bands, Detune 20-34 km
  - each band thins its own rocks
  - wider Lattice bands, Dissonance 34-50 km
  - only the low-tier drones out past the hold
  - wider Lattice bands, Feedback 50-66 km
  - the Coda has the fewest rocks of all
  - only the low-tier drones out past the hold, rarest in the Coda
- [`Resources/Prototypes/_NF/Guidebook/nf14.yml`](../../../Resources/Prototypes/_NF/Guidebook/nf14.yml): the bands, Drift, exotic matter and the anchors
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): Drift stabiliser, anchor charm and relay anchor recipes

<!-- WOLFGATE-GENERATED END -->
