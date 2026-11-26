# Coda sites design

Eight places in the Coda. Each one is a small generated grid, placed at round start in the last
band, with one focused mechanic. Three give the Coda mercy and temptation (the Lighthouse, the Last Anchor, the
Hospice). The rest give it places to fear (the Orchard, the Graveyard Field, the Waiting Room, the Broadcast Tower). They share one set of stone, glass and light, one way of keeping a grid standing (self-power, air that
never drains, a hull that heals), the Lattice's shelter and Drift, and a record of this round's dead. (A Nursery
was designed and dropped.)

## Placement

A `wgCodaSite` prototype per site gives its map, its distance band from the sector's centre, its Lattice band and
how clear of other grids it must be. `WGCodaSiteSystem` places every site once per Lattice map at round start, on a
random bearing, clear of everything else. Each site's grid carries `CleanupImmune`, and those meant to
be safe from the Coda's ship hazards carry `WGCodaHazardImmune`. Admins: `wgcodasite place|where`.

## The sites

### The Lighthouse (mercy)

A round lighthouse tower with a keeper's room and a dock, broadcasting "safe harbour".
- **Shelter:** it is the only real shelter in the Coda. A Lattice station anchor holds a local well of anchored
  space round it. The grid carries `WGLatticeShelter`, which keeps most Drift off and stops the taking, and
  `WGCodaHazardImmune`, so no hunters board.
- **The beacon:** every ten minutes it calls on the common channel: "safe harbour" and its bearing.
- **The keeper:** polite, and wrong in small ways. When players are near he says a line every minute or so. Some are
  ordinary, some are off: he mentions keeping the light for seventy years, or asks after someone by name. The name is
  a dead player's, from this round's record of the dead.
- **Sleeping there:** a player who wakes aboard finds the keeper standing over them, smiling.
- He never turns hostile and can't be hurt.

### The Last Anchor (mercy)

A small platform round a dying anchor nobody built.
- **The cycle:** it holds for three minutes, then fails for one, over and over, its lights going out with it. While
  it holds, it is a Lattice station anchor with a local well.
- **While it fails:** every player within two kilometres takes Drift each few seconds, the Coda pressing in harder
  than before.

### The Choir's Hospice (temptation)

A small, clean infirmary run by Choir attendants who never lift a hand against anyone.
- **The cots:** heal anyone who lies on one, fully and for free, after ten seconds.
- **The price:** each treatment adds Drift that clears slowly.
- **The third visit:** the Choir offers the player the song outright, without the usual 80 Drift.

### The Orchard (fear)

An asteroid mass webbed with flesh vines, with bodies hanging from them.
- **Exotic matter:** its rock drops exotic matter like any Coda rock, and there is a lot of it.
- **The vines:** they creep toward anyone within twelve tiles, growing a tile every twenty seconds over open rock floor.
- **The hanged:** cutting a vine drops the bodies hanging from it, and they rise as Remade.

### The Graveyard Field (fear)

A field of rocks, one carved with the name of each character who has died this round.
- **The names:** they come from this round's record of the dead and are read on examine. Rocks without a name yet read blank.
- **Mining a named rock:** the nearest player takes a spike of Drift, the dead one's weight.

### The Waiting Room (fear)

A small derelict with a sealed back room of chairs, a ticket dispenser and a board reading NOW SERVING.
- **The board:** every three minutes it calls the next number. Each living player holds a number in a fixed order,
  and the player whose number is called takes a spike of Drift wherever they are, with a message telling them so.
- **Breaking the board:** destroying it stops the calling for the rest of the round.

### The Broadcast Tower (fear)

A relay mast with a small control room.
- **The replays:** it records the round's radio traffic and rebroadcasts each message about twenty minutes later on
  the common channel, altered: a name swapped for another speaker's, the numbers changed.
- **Stopping it:** while the transmitter stands it keeps leaking onto common. Breaking it stops it.

### The Echo Station (fear)

Caelestinus Central itself, loaded a second time far out in the Coda and decayed: the place everyone knows, wrong.
- **The station:** Central's own map (`/Maps/_Mono/Outpost/caelestinus_central.yml`), loaded whole into a scratch map,
  initialised, then moved into the Coda. It changes whenever Central does, with no copy to keep up.
- **The decay, applied when it is placed:**
  - Every console, vending machine, contract board and other machine with a window is stripped out, so the Echo can't
    be used to shop, sell or broadcast.
  - Three bulbs in four are dead, and the rest flicker.
  - Flesh knots take root in its halls and spread corruption.
  - Windows are cracked; its docks stand open to vacuum, so the arms seal themselves off.
  - Scrap, glass, cobwebs and bones lie about.
- **The echoes:** pale figures stand where the consoles were, going through the motions of a shift years gone. When
  players are near they say stale lines. Now and then one uses a name from this round, which it shouldn't know.
- **The archive:** noticeboards carry this round's dead as news from long ago.
- **The dead anchor:** a dead copy of Central's anchor stands where Central's own does, and twitches whenever a real
  anchor falls, pressing Drift on everyone aboard.
- **Nothing leaves:** anything taken from the Echo crumbles to dust 30 seconds after it leaves the Echo's grid.
- **Familiar rooms:** Drift builds faster aboard, in rooms players know by heart.
- **Ambience:** a hook for its own sound; the user is gathering it.

## Building the maps

`its generator (sites.py, kept outside the repo)` describes each site as rooms, doors, docks and props, with `mapformat.py`
for tiles, air and the map format. It writes `Resources/Maps/_WG/CodaSites/<site>.yml`. Renders come
from the game's map renderer.

## Decisions taken without the user

- The keeper and the Hospice attendants are passive NPCs with no AI: they stand where they are mapped. The keeper
  speaks, and moves only when a sleeper wakes.
- The Last Anchor's failure adds Drift directly, rather than scaling every Drift gain in range. That keeps it inside
  this module.
- The Graveyard Field's Drift goes to the player nearest a rock when it breaks, since a rock doesn't know who broke it.
- The Broadcast Tower's replays go on common for everyone. That is the design's "leak", and it is always on while the
  transmitter stands.
