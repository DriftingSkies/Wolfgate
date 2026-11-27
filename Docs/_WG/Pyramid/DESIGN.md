# The Pyramid

The Coda's rarest enemy. It isn't a ship, but it behaves like one: it flies, hunts ships, shows on their scanners and
trades fire with them. Reality warps around it, and it sends waves of warped space out 400 m.

## Sources

| Source | What we took |
|---|---|
| The user's reference video, "The Pyramid" | The look: chrome faces split by a bright front ridge, violet-slate on the left and teal on the right. A pale banded base, a dark mosaic upper half, an eye near the apex (black ring, teal iris, red pupil), two red lamps low on the faces, and a cloud of shed flakes spraying widest at the base. |
| Caves of Qud's chrome pyramid (wiki) | A high-level robot artillery piece with a force bubble that keeps things off it, a cosmetic space-time vortex where the screen ripples round it, and a missile swarm fired in a shotgun spread. It shrugs off heat and cold, is weak to electricity, is EMP-sensitive and regenerates. Its descriptions talk of space and time coming off their hinges and of sound arriving only in waves. |
| Paragon-Moon's `chrome_pyramid.dm` | Telegraphed barrages: patches painted round the target and one under it, a charge sound, and a fire 6 s later. Robotic protocol lines while it marks. A distortion pulse every 5 s, ambience every 15 to 25 s, and a collapse on death: a 10 s shudder with a collapse sound, then loot. We use its five sounds as they are, converted to mono. We leave out its description's angel and God text ([[no-god-content-from-paragon]]). |

## How it plays

Its numbers are in the README and on the prototype. This is the shape of a fight:

1. **Arrival.** A crew lingering deep in the Coda gets "Something on the scanners isn't a ship" and a red triangle
   650 m out. The first wave reaches them a few seconds later: the screen shears, colours split, and Drift ticks up.
2. **The circle.** It closes to 90 m and circles. At 16 m/s it's slower than most ships, so running is a real choice,
   but it follows across the Coda for ten minutes before giving up.
3. **Barrages.** Every 12 s, five pale-blue reticles appear on the hull, one under someone's feet, with a protocol line
   and the charge whine. Six seconds later lasers flash in from the Pyramid and each patch blows. Crews learn to step
   out of the marks and patch hull between volleys. Once 30 lasers have landed on a ship, about six full volleys, it
   breaks off from that ship for good, and leaves if no other is in reach. Before that a summoned one never lets go.
4. **Fighting back.** Ship guns hurt it, shock weapons most. An EMP hangs it in place for 12 s with its marks cleared.
   Ramming doesn't work, because its force bubble shoves ships off at 35 m.
5. **The collapse.** At 60,000 damage it groans, everything within 120 m shakes for 10 s, a last triple-strength wave
   rolls out, and it folds away. It leaves three chrome shards worth 12,000 each, which draw the Coda's hunters.

## Effects (v0.2)

The user asked for the animation to follow the video more closely ("waves of fragments going upwards"), for reality
to warp at every turn, and for the sound to move with the actions.

- **Fragments** are simulated on the client: a steady stream peels off the faces and rises; a surge is flung up and
  out each time a wave leaves; laser hits throw chips; and in a collapse they are dragged back in. The sprite has its
  own rising flakes, a glint sweeping up the faces, and an aura that flares with each wave.
- **The field**: within 48 m, space swirls, ripples, tears in bands and splits into its colours, strongest just as a
  wave leaves and climbing through a collapse.
- **Barrages are in time with their sound.** The reticle animation and the tethers both run exactly six seconds, the
  length of the charge sound; the fire sound plays as the lasers land.
- **Impacts** are their own explosion type, shattered chrome and broken pixels with no fire, and each hit sends out a
  shockwave, a flash and a moment of pixelated reality.
- **Sound comes in waves.** The ambience no longer plays on its own timer; each wave carries it, and each player
  hears it as the front reaches them.
- Reduced motion turns off the swirl, tearing, pixelation and shaking, and thins the fragments.

## Rarity

Once a minute, each crewed ship in the Coda's hazard bands rolls 0.3 %, doubled at full depth. Rolls start 45 minutes
into the round; at most one Pyramid comes per round, and never while one is out. A ship sitting deep in the Coda
draws it about once every two to four hours of exposure, so most rounds won't see it. For comparison, the Leviathan
rolls 2.5 % a minute and players still rarely meet it.

## Decisions taken without the user

- **An entity, not a grid.** A grid would need tiles and would show as a ship, while a physics body with a radar blip
  can be shot, shoved and tracked. The cost is that it has no interior to board, which suits it.
- **Its own module**, `Pyramid`, built in the sites worktree. It touches no CodaSites code, so it can ship with the
  sites PR or on its own.
- **Lasers are visual; the damage is explosions** at each mark, on par with ship weapons (the prototype's
  `WGPyramidShatter`: intensity 150, slope 3, at most 30 per tile). A full volley can wreck a ship. Shots are scheduled
  about 0.15 s apart down the volley so the blasts overlap less, and overdue shots fire one a tick.
- **The waves don't touch ships**, only the people they pass through (warp, camera jolt, Drift), so a wave every 10 s
  is atmosphere rather than attrition.
- **The protocol lines are our own**, in the Paragon style, with no religious content.
- **Lensing uses the singularity's distortion component at a fixed strength.** Its strength can't be changed outside
  the singularity system, so the pulses come from our own wave and warp overlays instead.

## Open questions

- Is 60,000 health right? 12,000 went down in moments to high-tier ship guns, so it was raised fivefold; it needs
  another live fight to confirm.
- Should it be able to follow a ship out of the Coda? Currently it stops at the hazard bands' edge.
- Should the shards do more than sell and lure, for example as a crafting input?
- Should the reticles be shot or broken to cancel a patch? Not done.
