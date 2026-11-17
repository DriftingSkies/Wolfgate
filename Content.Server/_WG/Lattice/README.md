# Lattice

The sector's boundary. Reality anchors round Caelestinus Central hold off Kyphrus's Unmooring; the further from their
hold, the worse it gets. The field is banded Unison, Detune, Dissonance, Feedback and the Coda, whose name comes out garbled.
Crossing into another band shows a popup and a notification chip, and radars draw the band edges in place of the
drone rings: the band edges are the profile's (20, 34, 50 and 66 km), and the default worldgen's biomes change at the same
distances (`droneRingEdges: false`, so a map's drone rings never move them). Only the six primary anchors set the bands,
so the edges are near-circles that pull in only on the side of a lost primary. Secondary anchors (relays, a station's own)
carry their strength negated and hold only a local well of anchored space (`WGLatticeMath.LocalHold`, about 2 km for a
relay), so they never bend the far bands. The full design and build order are in `Docs/_WG/Lattice/DESIGN.md`.

**How it works.** `WGLatticeSystem` puts `WGLatticeFieldComponent` on every map with world generation and spawns the
profile's six primary anchors on a 3 km hexagon if the map has none. Each is a station loaded from the profile's
`stationMap` (`WGLatticeSystem.Stations.cs`, pushed outward clear of other grids); only if that load fails is a plain
round deck built in code instead. Every station has the anchor core (a glyph obelisk, a different one per anchor:
`WGLatticeAnchorCoreAlpha` to `Zeta`, 20,000 damage, and a 500 kW HV power source: it powers only what HV cable joins
to it, which the mapped station has and the plain deck does not) and a fortress shield
(`WGLatticeAnchorShield`, which needs no power). The shield's field is drawn as a rooted Ward in the profile's
`shieldTint` (purple): the Remade module's membrane and glyphs, tinted through `WGWardFieldComponent.Tint`. Hitting the
core or its shield calls the drones still loyal to the Lattice (`MonoDroneSpawnerT1`, 300 m out towards the attacker,
every 5 minutes at most). A core that is destroyed, or removed any other way mid-round (its station deleted or blown apart), is a breach: it
stops holding, the delta alarm sounds, and a global announcement and chip say reality is coming loose in its direction. The field's `Integrity` (primaries left out of six) goes to clients. Each anchor
(`WGLatticeAnchorComponent`) adds a hold that halves at the profile's `reach`; the bands' thresholds are the summed hold
at each band edge for the intact hexagon, so a lost anchor pulls the edges in, most on its side. Every second each
player's carrier gets a band and builds Drift (`WGDriftComponent`): the grid they are on, themselves in open space, or
on a planet, their ground or grid at the world's place in the sector. A vehicle's crew count as wherever the vehicle is. Each band has a ceiling (`maxDrift`: 20, 40, 65, then 100 only in the Coda) that Drift climbs to over about ten minutes and eases back down to from above. Ghosts are skipped, and Drift only changes while
someone is aboard. The field component carries the anchors and thresholds to clients. `WGLatticeMath` holds the field,
band, depth and garble sums.

**Drift in the head.** Apart from a carrier's Drift, a person holds their own (`WGDriftComponent.Mind`,
`WGDriftMindSystem`). A Monolith's dread feeds it (red on the gauge), and so does any entity with `WGDriftSource`: by
vicinity, falling off over its `range`, more when carried, and a lump on touch or pick-up (exotic matter has a weak one).
It fades at 0.4 a second once nothing feeds it. The Unmooring's hallucinations play off the greater of the two, and off
a map with no Lattice they play off the head alone with the `WGDefault` profile. The Choir and the Remade hold none.

**What a person feels.** The Lattice works out what each person aboard feels (`WGDriftComponent.Felt`, `FeltLevel`,
`WGLatticeSystem.Felt.cs`), which drives their hallucinations, gauge, screen warp and mutations: their ship's Drift, less
their anchor charm, less half while grounded by lodestar (`WGLodestar`: haloperidol, silver and iron; `WGGround`), less a
fifth asleep or half asleep on a sheltered ship, less a tenth for each sane crewmate within 8 m (at most 30%), and 15%
worse with nobody near. Sleep also clears Drift from the head three times faster. A ship in the Coda soaks Drift into
its hull (`residueRate` a minute up to `residueMax`), and its Drift never falls below that until a drydock's Clean Drift
(`cleanDriftPrice` a point, credit consoles only) scrubs it.

**Gauge.** `WGDriftMindSystem` shows the `WGDrift` alert, a dial among the status icons with a step per Drift level, filled to the greater of the two and red when a Monolith's
Drift is the greater;
it shows only while out of anchored space or holding Drift, and in a garbling band it shows an unreadable dial.

**Walls and spires.** A raised anchor shield is a wall like a rooted Monolith's Ward (`WGLatticeSystem.Barrier`, using
`WGWardSystem.HoldOut`): no ship but the station's docked ones, and nobody loose in space, gets inside it, so it has to be
brought down first. Inside, the old alien spires are `WGLatticeSpireTurret`s that snap round and fire flechette volleys at anyone aboard almost at once.
The station's hardpoints and lances lose their `Anchorable` and can't be unwrenched.
A core knocked loose (its deck blown out) is a breach like one destroyed or deleted. The shield generators can't be
unbolted or taken apart, only destroyed.

**Lost ships.** Every ten minutes or so there is a small chance (`lostShipChance`, more per lost anchor) that a wreck in the
Deep with no player within 600 m fills with Remade, is renamed "Lost ship" and shows its label on radar
(`WGLatticeSystem.LostShips`). Each lost primary anchor also makes the Coda's take of small animals half again as likely
(`takeLostScale`).

**Beams.** Clients draw a flowing violet beam between each pair of neighbouring primary anchors (`WGLatticeBeamOverlay`, shader `WGLatticeBeam`: braided strands, streaming filaments and comets),
starting at the shield edge (`beamOffset`). A lost anchor breaks the two beams that ran to it.

**FTL.** Bands with `ftl: false` (Feedback and the Coda) kill FTL drives (`WGLatticeSystem.Ftl.cs`): a ship there
can't jump (`ConsoleFTLAttemptEvent`, and the helm's drive status reads `Unmoored` off the ship's networked
`WGDriftComponent.NoFtl`), and the helm refuses a jump whose destination is in one. Ships have to fly in and out.

**The Coda's hazards.** For crewed ships in `hazardBand` (`WGCodaHazardSystem`): after `hunterGrace` (90 s) a roll
each minute, climbing over `hunterRamp` (8 min) to `hunterMaxChance`, puts a pack of 2-4 `hunterMobs` aboard on open floor at
least 6 m from the crew (5 min cooldown, at most 8 alive per ship); every 20 s 2-6 bolted-down structures take
`rotDamage` structural damage unless a stabiliser runs; and a `failureChance` roll every `failureInterval` (30 s at the
edge, down to about 10 s deep in and after five minutes) makes something give out for 8-20 s: power flickering on and
off ship-wide (`WGCodaFlutterComponent` on each APC), doors slamming, opening or bolting, half the thrusters, half the
gyroscopes, or every APC breaker. Flickers and doors are the commonest. On the client every scope, helm and mass scanner alike, goes blind in two
wandering wedges and fills with static (`ShuttleNavControl.Lattice.cs`): crawling specks and a rolling interference band,
denser deeper in, and bursts of snow that white the scope out for a moment, more often far out. Reduced motion slows the
static and drops the bursts. Relays and stabilisers don't stop hunters or failures.

**Twisted flesh.** In `mutationBand` at `mutationMinDrift` (80) or more and unsheltered, a person's arm can twist
into a Remade blade (`WGCodaMutationSystem`, `mutationChance` a minute, doubling at full Drift): `WGCodaArmBlade` in
one hand, unremovable. It withers away after `mutationWither` (10 min) out of the Coda.

**The leviathan** (`WGCodaHazardSystem.Leviathan.cs`, client `WGLeviathanSystem`). About once in 40 minutes (`leviathanChance`,
more often deeper; 20 min `leviathanCooldown` per ship) something vast finds a crewed ship in the Coda. It circles as
garbled contacts at the edge of the radar for 25 s, slides past beneath the hull (a dark shape drawn under the world,
violet-rimmed, two pale eyes) while the power flickers and screens shake, and half the time brushes the hull: a shove,
6 s of dead engines and up to `leviathanTear` (3) outer tiles torn away, dead-end spurs first, never under anyone or a helm
and never splitting the ship. Then it sinks away and leaves 8 Drift in every head aboard. It can't be fought.

**Flesh** (`WGCodaFleshSystem`). On an unsheltered crewed ship in the Coda, a flesh knot (`WGCodaFleshNode`) takes root
now and then on open floor away from the crew (`fleshChance`, at most `fleshMaxNodes` per ship). Every 6 s it grows a
tile or two of Remade corruption (`WGCorruptionSystem.GrowLoose`: the Monolith's floor and wall growth, but spreading
only from its knot) within 6 tiles, up to 40. The knot feeds Drift to anyone near. Kill it and its flesh dies over a
minute; out of the Coda it withers within 5-10 minutes; Clean Drift at a drydock strips it at once.

**Drones.** Only the low tiers spawn: T0 in the inner Wilds, T1 from Detune out, rarest in the Coda. The T2 spawners are
gone from every biome.

**The rot** (`WGCodaRotSystem`). A living player out in the Coda's open space, off any hull, is warned at once ("your skin
prickles"), again at half the grace, and when the grace (20 s) runs out: then they take 2 Cellular a second that no suit
stops (an anchor charm halves it), are reminded every 15 s, and at 45 s an arm twists into a blade. Death out there
rises Remade as usual. Back aboard, it stops and fades three times as fast. The Choir are exempt. `WGCodaRotCVars`
(`wg.lattice.coda_rot*`) tune it.

**Speaking of it.** A living speaker who says "Coda" stumbles on the word and feels it catch (`WGCodaTabooSystem`);
ghosts and the Choir say it plainly.

**The dead rise.** In the Coda (`riseBand`) a body that lies dead for `riseDelay` seconds (60) starts to turn Remade
(`WGDeepRisingSystem`, through `WGRemadeConversionSystem`), unless its ship or the body itself is sheltered. If it was
a player's, they are asked whether to rise with it and play the Remade; a no leaves it to the NPC AI.

**Dead anchors.** A lost anchor's station is renamed `DEAD ANCHOR <name>` and its IFF turns grey, so radar shows which
anchors are gone.

**Sky.** Clients regrade the space background by how far out the view is (`WGLatticeSkyOverlay`, shader
`WGLatticeSky`), easing between bands: Detune drains its colour, Dissonance turns it teal and swimming, Feedback rust-red
and dark, and the Coda nearly black, with crawling veins, a heartbeat at the edges and eyes that open and watch the ship.
It needs parallax on and holds its look through FTL until the ship arrives somewhere.

**Garbling.** In a garbling band (the Coda) text garbles more the deeper it is, and flickers on clients
(`WGLatticeViewSystem`): radar contact names by the deeper of the viewer and the contact, the console's own position and
the radar's cursor and scanner readouts by the viewer, and the Scion hint whispers' coordinates by where the hulk is
(`WGLatticeSystem.GarbleAt`).

**Drift effects.** `WGDriftEffectsSystem` rolls each player's hallucination every 20 s or so, more often the higher
their Drift, from the profile's `effects` list: whispers (popups), voices and false hails on the common channel, radar
contacts that aren't there, footsteps and knocks, glimpses, and from 70 Drift a Remade standing in the dark that goes
when approached. Each goes to that one player only: chat and sound by a single-player filter, figures and contacts as a
`WGLatticeHallucinationEvent` that `WGLatticeHallucinationSystem` shows client-side. The rolls close from every
`effectInterval` (20 s) towards `effectIntervalAtMax` (4 s) as Drift climbs, and from 80 Drift two come at once,
three from 95. Higher up: blood round them (40), a crewmate saying something they never said (50), a crewmate seen as a
Remade for a few seconds (60), a ring of Remade that vanish when approached (80), and themselves seen as a Remade (90).
Past 90 the screen warp climbs steeply. Some lie about the game itself (`WGDriftEffectsSystem.Lies.cs`): their own
ship twice on radar (45), echoes, where sounds round them play again from the other side (50), a door or person that isn't
there for a few seconds (55), a false oxygen, pressure or heat warning (`WGFalse*` alerts, 60), and a silence where every
sound stops and then everything missed plays at once (85; `WGDriftSoundSystem` on the client). Some tell the truth: a
third of radar blips are a real lost ship or Monolith within 4 km, and a knowing (50) names a place out there that is real
60% of the time. Blood, glimpses, phantoms and swarms are shared with anyone else aboard also past 70.

**Band signatures** (`WGBandSignatureSystem`, client `WGDriftSoundSystem`): in Detune sound goes dull, in Dissonance a
door a player opens waits a beat first, and in Feedback a player's words sometimes come back to them on the radio, a
little wrong.

**Loggers, tissue and couriers.** The drift logger (autolathe) counts readings while carried, 1, 3, 8 and 20 a minute from
Detune to the Coda, up to 1,500, and sells for 4 cr a reading. A twisted arm can be cut away in surgery
(`WGSurgeryCutTwistedArm`), leaving Remade tissue (2,500 cr). A ship moving a Monolith faster than 1 m/s puts Drift in
every head aboard and can't hide its name on radar (`WGDriftLoggerSystem`). Rarely, from 40 Drift, a pulse hits:
vision closes to a throbbing tunnel ringed with red glyphs for about three seconds over a sub-bass heartbeat
(`WGDriftPulseOverlay`, `drift_pulse.ogg`); it holds still with reduced motion. Admins can show any of them with
`wghallucinate <kind> [username]`. From 30 Drift the screen warps
(`WGDriftOverlay`, the drunk shader), off the carrier's networked Drift `Level`. In the Coda (`takeBand`) a crewed ship
can lose a small animal (under 30 kg) about once in 25 minutes: it dies and turns, through the Remade conversion. A
`WGLatticeShelterComponent` on the ship (or a person) keeps a share of the Drift off and stops the taking.

**Rocks.** Each band out has fewer rocks than the last (`WGDensityDetune` to `WGDensityCoda` in `worldgen.yml`), and
the inner zone's are thinned too, so the rich rocks are a long trip out.

**Rewards and shelter.** Asteroid rocks broken out in the bands drop more ore (each band's `yield`, up to 1.8 in the
Coda, through `WGLatticeSystem.YieldAt` in `WGAsteroidSystem`) and, in the Coda only, exotic matter
(`WGMaterialExoticMatter`, 3,000 a shard, `exotic` shards per vein). The autolathe makes two things from it: the Drift
stabiliser flatpack (5 shards; 15 kW while running), which shelters the ship it is anchored on from 80% of Drift gain
and stops the taking, and the anchor charm (1 shard), worn round the neck, which keeps half the Drift off its wearer.
`WGLatticeShelterSystem` keeps the `WGLatticeShelterComponent` on ships and wearers.
`WGDriftGainEvent` is raised on everyone aboard a carrier that is gaining Drift and the largest multiplier asked for
scales the gain; the WolfChem exotic sense (Chorus) uses it.

**Anchor stations and their defence.** Each station is loaded from `Resources/Maps/_WG/Lattice/anchor_station.yml`
(the profile's `stationMap`; a plain deck is built in code if it fails), placed so its core sits on the hexagon's
corner, and its core is swapped for that anchor's own. The core is also a station anchor (`StationAnchor`), so the
station can't be moved. Its eight hardpoints carry `WGLatticeWardenLance`s, which fire only from an anchor station.
`WGLatticeSystem.Defence.cs` is slow to anger. Shield and core hits are counted per ship (forgiven after 60 s); ten
(`strikeLimit`) from one ship put out a global "under fire" alert, and nothing more while the shield holds. Once the
station's shield is down it alerts again, calls its drones, and turns every lance on the nearest ship that has fired on
it in the last 20 s and still has a living player or NPC aboard, within 3 km; it stops when the shield comes back or the
ship is dead. Shield hits are slow to anger, but damage to anything on the station itself (`WGLatticeStationPartComponent`,
marked every 30 s) or to the core is answered at once (`Provoke`): the alert goes out, the drones come, and the lances
fire on the attacker for 20 s whether the shield holds or not. The attacker is the person who fired a hand weapon,
wherever they stand, or the ship a mounted gun fired from, so nobody else aboard is caught up in it. Other anchor stations never count as attackers. The station also hums: `WGLatticeHumSystem` plays the
Goonstation flock reactor loop to a player within 450 m of an anchor, louder the closer they are.

**Relay anchors.** The autolathe makes a relay anchor flatpack (20 shards of exotic matter). Bolted down with 30 kW,
on a grid moving under 1 m/s for a minute, a relay holds 0.4 of a primary's strength and pushes anchored space out
round it; it lets go when the grid moves or the power fails. It also runs on exotic matter: it starts empty, takes
shards by hand (up to 6), and each shard buys 10 minutes of holding (`WGLatticeSystem.RelayFuel.cs`). Mapped station
anchors (`WGLatticeStationAnchor`) don't need fuel.

**Kyphrus.** The black hole shader takes `unmooring` (0 to 1) from `WGLatticeViewSystem.Unmooring`: the share of
primary anchors lost, or at least 0.6 (rising with depth) while the viewer is in the Coda. It strengthens the lensing,
makes the disk's turning skip, leans its colour from orange to violet and adds a counter-turning ghost image of the disk.
From the first anchor lost it also twists space into a spiral, splits the bent light red from blue and fires precessing
jets from the poles, growing with each loss. Past half lost, crimson veins pump in the haze to a heartbeat, the disk
tears into streamers, the shadow swells and reaches out in tendrils, the photon ring stutters and an eye opens inside
the shadow and watches the viewer (`black_hole.swsl`, set in `StarOverlay.BlackHole.cs`).

**The Choir.** A player at 80 Drift or more is offered the song once a round (`WGChoirSystem`): a popup, a line in chat
and an "Answer the song" action for five minutes. Using it is the only way in: two confirmation dialogs spell out the
coin toss, then half join and half die where they stand and rise as Remade, with the player in the Remade
(`WGDeepRisingSystem.Raise`). Members are `Unrevivable`; one lying dead 30 s turns Remade and takes the player with it.
Members show a white C on red (`WGChoirFaction`) to ghosts, admins and each other, carry the `WGMindRoleChoir`
antagonist role, and their PDA examines with red glyphs (`WGChoirMarkComponent`). Surgeons can cut the song out
(`WGSurgeryExciseSong`, `WGSongExcisionSystem`): saw the skull, then excise with a shard of exotic matter (used up),
15 s, 30 brain damage. 70% leave the Choir and can be revived again; 30% die on the table and rise Remade with the
player. Not in the Coda. A Choir player takes the `WGChoir`
faction (the Choir's NPC ships and the Remade leave them be), is untouched by Drift, its gauge, screen sway and garbled readouts (the Monolith's dread still reaches them, though it adds no Drift), and is briefed to break anchors
and carry Scions inward as an antagonist. Admins get an alert for each offer and join, and joins are admin-logged.
Removing `WGChoirMemberComponent` takes them out again. `wg.lattice.choir`, `wg.lattice.choir_min_players` (15),
`wg.lattice.choir_max` (3) and `wg.lattice.choir_min_drift` (80) gate it. Each Choir player adds 2 to the DEFCON
score, and each lost primary anchor 25 (`breachDefcon`) and holds DEFCON a step worse per anchor lost (one lost is at
least 4, four is 1).

**Coda-only encounters.** An encounter with `latticeBand` above zero is placed at its distances from the sector's centre
and only where the band is at least that; with no Lattice it falls back to distances from a player. The Monolith hulk,
the guarded hulk and the adrift freighter use band 4 at 68-76 km.

**Lore and guide.** The lore book keeps The Unmooring, The Lattice and The Choir in its Forbidden chapter
(`Resources/ServerInfo/_WG/LoreBook`); The Sector Today, The Collapse and The Monolith link to them. The guidebook page
The Lattice and Drift (`Resources/ServerInfo/_WG/Lattice/Guidebook`) covers the bands, Drift, gear and anchors. Anchors,
relays, exotic matter, the stabiliser and the charm have a Lore button on examine (`guidebook.yml`).

**Tuning.** `Resources/Prototypes/_WG/Lattice/lattice.yml`: the `WGDefault` profile's anchor layout, reach, Drift cap
and per-band edge, Drift rate, growth with depth, chip style and garbling.

**The hex field** (`WGLatticeSystem.Hex.cs`, `WGLatticeHex`). The beams between neighbouring primary anchors make a
ring; a beam stands while both its anchors do, and the hex is sealed while every beam stands. No Monolith crosses a
standing beam inwards: whatever carries it (its ship and everything docked to it, or whoever holds it in open space) is set
back outside and its inward speed is thrown back. A broken beam lets it through. A ship carrying a Scion can't jump inside a
sealed hex, and one that arrives inside it by any jump is put back out. Each beam has a slice of the hex, from the beam
in to Central's circle (its grid plus 60 m). A slice whose beam stands is a sanctuary: a Monolith can't be drawn on, grow
corruption or reach anyone in it from outside. A broken beam opens its slice, and while any beam is broken Central's
circle is open too, so the Monolith reaches Caelestinus Central; with a Lattice the main station has no sanctuary of its own.

**Wards.** Each anchor keeps corruption off grids within its `ward` (primaries 600 m, relays and station anchors
150 m). Caelestinus Central gets its own anchor (`WGLatticeCentralAnchor`, 20,000 damage, an 800 m ward) railed in at
tile (1, 17) and a Lattice status console at (-19, -6) facing east, unless an anchor is mapped there. Damaging it names
the attacker to the sector; destroying it is announced. Players aboard an anchor station are named as trespassers, all who came aboard in one announcement, as a silent banner at most every
ten minutes a station, and each again only after five minutes. A station alerts the sector at most every five minutes (`alertCooldown`); only a direct hit sounds the alarm.

**Ghosts** can warp to every anchor, Central's included, from the ghost menu.

**Radar.** Every radar and mass scanner draws the hex (`ShuttleNavControl.Hex.cs`): violet beams, dashed red where
broken, a faint tint inside while it is sealed, and a ring round each anchor's ward.

**Status app.** The autolathe prints a Lattice status cartridge (PDA app) and a status console flatpack. Both show
`WGLatticeStatusPanel` (client): the hex drawn as glyph nodes with comets running along standing beams and torn red ones
where broken, Central's anchor at the heart, flesh ships circling as red motes, and readouts for state, integrity, the
hex, Central's anchor, flesh ships, the viewer's band (garbled where the band garbles) and each anchor by name. It reads
only the networked field (`Hex`, `HexUp`, `HexAlive`, `HexNames`, `CentralStanding`, `FleshShips`, `Unstrung`, `Tacet`).

**Fading.** A lost anchor's hold doesn't vanish: it fades where it stood over `wg.lattice.anchor_fade` (1,000 s, about 17 minutes), so
the bands creep inward instead of jumping.

**The end** (`WGLatticeSystem.Endgame.cs`, `WGLatticeSystem.Implosion.cs`). When the last primary anchor falls, the
Unstringing: every station anchor lets go too, all fading the same way, so over about 17 minutes the Coda, its hazards,
its rot and its dead FTL close in from the edge. Mobile relays (player-built, fuelled) keep holding as the last havens,
but their power draw doubles every `wg.lattice.haven_load_doubling` (10 minutes) for as long as they stand, and bulbs
aboard them don't fail. Tacet comes 15 s after the Coda reaches the centre (`tacet_centre_delay`), or 15 to 30
minutes after the Coda has the whole sector if a haven keeps it off the centre, timed by
`WGLatticeImplosion` so clients draw what the server does: Kyphrus swells to four times its size for 18 s while
"Spleen" plays, falls in to a point, the whole view goes black for 6 s while the charge builds, then it bursts in a
blinding flash. Four shockwaves leave the centre 6 s apart at 600 m/s and run out past the farthest grid: each one bursts
bulbs it passes (`tacet_wave_bulb_chance`, 55%), throws ships outward and spins them (`tacet_wave_shove`,
`tacet_wave_spin`), knocks their crews flat and throws anyone adrift. After the last wave the bulbs keep failing (a
quarter every 30 s), the sky goes nearly black with only a few faint stars (`WGLatticeSkyOverlay`), and the leviathan
comes three times as often (`tacet_leviathan_scale`). Client: `WGTacetOverlay` (blackout and flash),
`WGTacetWaveOverlay` (`tacet_shockwave.swsl`). `WGLatticeEndgameCVars` (`wg.lattice.tacet*`) tune it. Admins use
`wglattice siege|unstring|tacet`.

**Admins.** Spawn `WGLatticeAnchorRelayMarker` (0.4 strength) to push the field out, or delete a primary anchor to
open a breach. A carrier's band and Drift are on its `WGDriftComponent` in VV.


<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Lattice/ShuttleConsoleSystem.Lattice.cs`](ShuttleConsoleSystem.Lattice.cs)
- [`Content.Server/_WG/Lattice/WGBandSignatureSystem.cs`](WGBandSignatureSystem.cs)
- [`Content.Server/_WG/Lattice/WGChoirRoleComponent.cs`](WGChoirRoleComponent.cs)
- [`Content.Server/_WG/Lattice/WGChoirSystem.cs`](WGChoirSystem.cs)
- [`Content.Server/_WG/Lattice/WGCodaFleshSystem.cs`](WGCodaFleshSystem.cs)
- [`Content.Server/_WG/Lattice/WGCodaFlutterComponent.cs`](WGCodaFlutterComponent.cs)
- [`Content.Server/_WG/Lattice/WGCodaHazardSystem.cs`](WGCodaHazardSystem.cs)
- [`Content.Server/_WG/Lattice/WGCodaHazardSystem.Leviathan.cs`](WGCodaHazardSystem.Leviathan.cs)
- [`Content.Server/_WG/Lattice/WGCodaMutationSystem.cs`](WGCodaMutationSystem.cs)
- [`Content.Server/_WG/Lattice/WGCodaRotSystem.cs`](WGCodaRotSystem.cs)
- [`Content.Server/_WG/Lattice/WGCodaTabooSystem.cs`](WGCodaTabooSystem.cs)
- [`Content.Server/_WG/Lattice/WGDeepRisingSystem.cs`](WGDeepRisingSystem.cs)
- [`Content.Server/_WG/Lattice/WGDriftEffectsSystem.cs`](WGDriftEffectsSystem.cs)
- [`Content.Server/_WG/Lattice/WGDriftEffectsSystem.Lies.cs`](WGDriftEffectsSystem.Lies.cs)
- [`Content.Server/_WG/Lattice/WGDriftLoggerSystem.cs`](WGDriftLoggerSystem.cs)
- [`Content.Server/_WG/Lattice/WGDriftMindSystem.cs`](WGDriftMindSystem.cs)
- [`Content.Server/_WG/Lattice/WGHallucinateCommand.cs`](WGHallucinateCommand.cs)
- [`Content.Server/_WG/Lattice/WGLatticeEndCommand.cs`](WGLatticeEndCommand.cs)
- [`Content.Server/_WG/Lattice/WGLatticeShelterSystem.cs`](WGLatticeShelterSystem.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Barrier.cs`](WGLatticeSystem.Barrier.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.cs`](WGLatticeSystem.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Defence.cs`](WGLatticeSystem.Defence.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Endgame.cs`](WGLatticeSystem.Endgame.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Felt.cs`](WGLatticeSystem.Felt.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Ftl.cs`](WGLatticeSystem.Ftl.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Gain.cs`](WGLatticeSystem.Gain.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Hex.cs`](WGLatticeSystem.Hex.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Implosion.cs`](WGLatticeSystem.Implosion.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.LostShips.cs`](WGLatticeSystem.LostShips.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.RelayFuel.cs`](WGLatticeSystem.RelayFuel.cs)
- [`Content.Server/_WG/Lattice/WGLatticeSystem.Stations.cs`](WGLatticeSystem.Stations.cs)
- [`Content.Server/_WG/Lattice/WGSongExcisionSystem.cs`](WGSongExcisionSystem.cs)

### Shared

- [`Content.Shared/_WG/Lattice/WGChoir.cs`](../../../Content.Shared/_WG/Lattice/WGChoir.cs)
- [`Content.Shared/_WG/Lattice/WGCodaMutation.cs`](../../../Content.Shared/_WG/Lattice/WGCodaMutation.cs)
- [`Content.Shared/_WG/Lattice/WGDriftComponent.cs`](../../../Content.Shared/_WG/Lattice/WGDriftComponent.cs)
- [`Content.Shared/_WG/Lattice/WGDriftLoggerComponent.cs`](../../../Content.Shared/_WG/Lattice/WGDriftLoggerComponent.cs)
- [`Content.Shared/_WG/Lattice/WGDriftSourceComponent.cs`](../../../Content.Shared/_WG/Lattice/WGDriftSourceComponent.cs)
- [`Content.Shared/_WG/Lattice/WGGround.cs`](../../../Content.Shared/_WG/Lattice/WGGround.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeAnchorComponent.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeAnchorComponent.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeEndgameCVars.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeEndgameCVars.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeFieldComponent.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeFieldComponent.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeHallucination.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeHallucination.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeHex.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeHex.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeImplosion.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeImplosion.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeMath.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeMath.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeProfilePrototype.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeProfilePrototype.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeShelterComponent.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeShelterComponent.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeShelterSources.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeShelterSources.cs)
- [`Content.Shared/_WG/Lattice/WGLatticeStatus.cs`](../../../Content.Shared/_WG/Lattice/WGLatticeStatus.cs)
- [`Content.Shared/_WG/Lattice/WGLeviathan.cs`](../../../Content.Shared/_WG/Lattice/WGLeviathan.cs)
- [`Content.Shared/_WG/Lattice/WGSongExcision.cs`](../../../Content.Shared/_WG/Lattice/WGSongExcision.cs)

### Client

- [`Content.Client/_WG/Lattice/ShuttleNavControl.Hex.cs`](../../../Content.Client/_WG/Lattice/ShuttleNavControl.Hex.cs)
- [`Content.Client/_WG/Lattice/ShuttleNavControl.Lattice.cs`](../../../Content.Client/_WG/Lattice/ShuttleNavControl.Lattice.cs)
- [`Content.Client/_WG/Lattice/WGChoirIconSystem.cs`](../../../Content.Client/_WG/Lattice/WGChoirIconSystem.cs)
- [`Content.Client/_WG/Lattice/WGDriftOverlay.cs`](../../../Content.Client/_WG/Lattice/WGDriftOverlay.cs)
- [`Content.Client/_WG/Lattice/WGDriftPulseOverlay.cs`](../../../Content.Client/_WG/Lattice/WGDriftPulseOverlay.cs)
- [`Content.Client/_WG/Lattice/WGDriftSoundSystem.cs`](../../../Content.Client/_WG/Lattice/WGDriftSoundSystem.cs)
- [`Content.Client/_WG/Lattice/WGLatticeBeamOverlay.cs`](../../../Content.Client/_WG/Lattice/WGLatticeBeamOverlay.cs)
- [`Content.Client/_WG/Lattice/WGLatticeHallucinationSystem.cs`](../../../Content.Client/_WG/Lattice/WGLatticeHallucinationSystem.cs)
- [`Content.Client/_WG/Lattice/WGLatticeHumSystem.cs`](../../../Content.Client/_WG/Lattice/WGLatticeHumSystem.cs)
- [`Content.Client/_WG/Lattice/WGLatticeRings.cs`](../../../Content.Client/_WG/Lattice/WGLatticeRings.cs)
- [`Content.Client/_WG/Lattice/WGLatticeSkyOverlay.cs`](../../../Content.Client/_WG/Lattice/WGLatticeSkyOverlay.cs)
- [`Content.Client/_WG/Lattice/WGLatticeStatusPanel.cs`](../../../Content.Client/_WG/Lattice/WGLatticeStatusPanel.cs)
- [`Content.Client/_WG/Lattice/WGLatticeStatusUi.cs`](../../../Content.Client/_WG/Lattice/WGLatticeStatusUi.cs)
- [`Content.Client/_WG/Lattice/WGLatticeViewSystem.cs`](../../../Content.Client/_WG/Lattice/WGLatticeViewSystem.cs)
- [`Content.Client/_WG/Lattice/WGLeviathanSystem.cs`](../../../Content.Client/_WG/Lattice/WGLeviathanSystem.cs)
- [`Content.Client/_WG/Lattice/WGTacetOverlay.cs`](../../../Content.Client/_WG/Lattice/WGTacetOverlay.cs)
- [`Content.Client/_WG/Lattice/WGTacetWaveOverlay.cs`](../../../Content.Client/_WG/Lattice/WGTacetWaveOverlay.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Lattice/WGChoirTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGChoirTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGCodaHazardTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGCodaHazardTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGCodaRotTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGCodaRotTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGDeepRisingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGDeepRisingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGDriftDepthTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGDriftDepthTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGDriftEffectsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGDriftEffectsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGDriftMindTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGDriftMindTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGDriftRidersTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGDriftRidersTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeFtlTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeFtlTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeHexTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeHexTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeShelterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeShelterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeStationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeStationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeTrespassTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGLatticeTrespassTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Lattice/WGSongExcisionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Lattice/WGSongExcisionTest.cs)

### Unit tests

- [`Content.Tests/_WG/Lattice/WGLatticeImplosionTest.cs`](../../../Content.Tests/_WG/Lattice/WGLatticeImplosionTest.cs)
- [`Content.Tests/_WG/Lattice/WGLatticeMathTest.cs`](../../../Content.Tests/_WG/Lattice/WGLatticeMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Lattice/alerts.yml`](../../../Resources/Prototypes/_WG/Lattice/alerts.yml)
- [`Resources/Prototypes/_WG/Lattice/anchors.yml`](../../../Resources/Prototypes/_WG/Lattice/anchors.yml)
- [`Resources/Prototypes/_WG/Lattice/choir.yml`](../../../Resources/Prototypes/_WG/Lattice/choir.yml)
- [`Resources/Prototypes/_WG/Lattice/drift.yml`](../../../Resources/Prototypes/_WG/Lattice/drift.yml)
- [`Resources/Prototypes/_WG/Lattice/exotic.yml`](../../../Resources/Prototypes/_WG/Lattice/exotic.yml)
- [`Resources/Prototypes/_WG/Lattice/guidebook.yml`](../../../Resources/Prototypes/_WG/Lattice/guidebook.yml)
- [`Resources/Prototypes/_WG/Lattice/lattice.yml`](../../../Resources/Prototypes/_WG/Lattice/lattice.yml)
- [`Resources/Prototypes/_WG/Lattice/reagents.yml`](../../../Resources/Prototypes/_WG/Lattice/reagents.yml)
- [`Resources/Prototypes/_WG/Lattice/shaders.yml`](../../../Resources/Prototypes/_WG/Lattice/shaders.yml)
- [`Resources/Prototypes/_WG/Lattice/status.yml`](../../../Resources/Prototypes/_WG/Lattice/status.yml)
- [`Resources/Prototypes/_WG/Lattice/weapons.yml`](../../../Resources/Prototypes/_WG/Lattice/weapons.yml)
- [`Resources/Prototypes/_WG/Lattice/worldgen.yml`](../../../Resources/Prototypes/_WG/Lattice/worldgen.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Lattice/lattice.ftl`](../../../Resources/Locale/en-US/_WG/Lattice/lattice.ftl)

### Textures

- [`Resources/Textures/_WG/Lattice/choir_icon.rsi/`](../../../Resources/Textures/_WG/Lattice/choir_icon.rsi/)
- [`Resources/Textures/_WG/Lattice/drift_alert.rsi/`](../../../Resources/Textures/_WG/Lattice/drift_alert.rsi/)
- [`Resources/Textures/_WG/Lattice/Shaders/drift_pulse.swsl`](../../../Resources/Textures/_WG/Lattice/Shaders/drift_pulse.swsl)
- [`Resources/Textures/_WG/Lattice/Shaders/lattice_beam.swsl`](../../../Resources/Textures/_WG/Lattice/Shaders/lattice_beam.swsl)
- [`Resources/Textures/_WG/Lattice/Shaders/lattice_sky.swsl`](../../../Resources/Textures/_WG/Lattice/Shaders/lattice_sky.swsl)
- [`Resources/Textures/_WG/Lattice/Shaders/tacet_shockwave.swsl`](../../../Resources/Textures/_WG/Lattice/Shaders/tacet_shockwave.swsl)

### Audio

- [`Resources/Audio/_WG/Lattice/attributions.yml`](../../../Resources/Audio/_WG/Lattice/attributions.yml)
- [`Resources/Audio/_WG/Lattice/drift_pulse.ogg`](../../../Resources/Audio/_WG/Lattice/drift_pulse.ogg)
- [`Resources/Audio/_WG/Lattice/tacet_burst.ogg`](../../../Resources/Audio/_WG/Lattice/tacet_burst.ogg)
- [`Resources/Audio/_WG/Lattice/tacet_spleen.ogg`](../../../Resources/Audio/_WG/Lattice/tacet_spleen.ogg)

### Maps

- [`Resources/Maps/_WG/Lattice/anchor_station.yml`](../../../Resources/Maps/_WG/Lattice/anchor_station.yml)

### Guidebook

- [`Resources/ServerInfo/_WG/Lattice/Guidebook/Lattice.xml`](../../../Resources/ServerInfo/_WG/Lattice/Guidebook/Lattice.xml)

### Tools

- [`Tools/_WG/Lattice/gen_drift_alert.py`](../../../Tools/_WG/Lattice/gen_drift_alert.py)

### Docs

- [`Docs/_WG/Lattice/DESIGN.md`](../../../Docs/_WG/Lattice/DESIGN.md)

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
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/ore_magnet.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/ore_magnet.yml): exotic matter mined from asteroids
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
