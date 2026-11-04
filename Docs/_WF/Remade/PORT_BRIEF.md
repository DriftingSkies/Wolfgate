# Remade port brief

Shared brief for the lanes finishing the Remade module. Read this and `Content.Server/_WF/Remade/README.md` first, then
the slice code, which is the pattern to follow: `WFMonolithSystem`, `WFCorruptionSystem`, `WFRemadeConversionSystem`,
`WFRemadeDirectorSystem`, `Resources/Prototypes/_WF/Remade/mobs.yml`.

## The design

An NPC-driven horror threat. The owner's end goal: "a super scary NPC driven necromorph gameloop that has a lot of
functionality". Players never play the Remade; there is no ghost takeover.

- A **Monolith Scion** gives free power, and more the harder it is drawn on. Draw climbs **Resonance** 0-100, which
  speeds itself up: Quiet, Hymn 15, Growth 35, Harvest 55, Rapture 80, Crescendo 100. Effects and gifts both grow with it.
- **Gifts**: Power (done). Matter fills the fabrication silo with materials nobody can resell. Flesh heals and revives
  nearby, but anyone revived rises as Remade on the spot when they die again. Reach gives faster jumps and wider sensors,
  and each jump adds Resonance.
- **Shards** chip off as small portable gifts. Chipping adds Resonance, and a shard grows corruption once its parent is
  at Growth, so selling shards spreads the outbreak.
- Feeding a corpse to the Tap is the only way Resonance falls.
- At Crescendo the Scion roots as the **Prime Monolith**. Destroying it kills its corruption, starves its Remade and
  pays a sector bounty.
- Main stations are safe: no corruption, no drawing. Everything must end and clean up.

## Names

Never use Dead Space's names in anything a player sees, or in new IDs and folders.

| DS13 | Here |
|---|---|
| Marker | Monolith (Scion, Prime). "The Monolith" is already the anomalous zone in Mono's lore; artefacts are Scion and Prime |
| Necromorph | the Remade |
| Unitologist | the Choir, Chorister |
| Convergence | the Crescendo |
| Signal, whispers | the Hymn |
| "Make us whole" | "Join the song" |
| Ubermorph | the Undying |

Slasher, Brute, Leaper, Exploder, Puker, Lurker, Spitter, Twitcher, Hunter, Divider, Tripod and Infector stay. Player
text is plain and blunt. No Ishimura, CEC, EarthGov or Aegis.

## Sources

Both repositories are read-only references. BYOND code doesn't translate: take the rules and numbers, and write SS14.

- `C:\Users\jzo12\Documents\GitHub\DS13-2.0\deadspace\code`: `necromorph/necromorphs/subtypes/` (stats per mob),
  `necromorph/abilities/`, `necromorph/bodyparts/`, `marker/abilities/` (psychic tricks), `corruption/structures/`.
- `C:\Users\jzo12\Documents\GitHub\DS13-1.0\code`: `modules/mob/living/carbon/human/species/necromorph/` (has infector,
  lurker, divider, tripod, which 2.0 lacks), `modules/necromorph/`, `modules/mob/dead/observer/freelook/marker/`,
  `game/gamemodes/marker/marker_shard.dm`, `modules/reagents/Chemistry-Reagents/necrotoxin.dm`.

Art and audio are already converted by `Tools/_WF/Remade/port_assets.py`: `Resources/Textures/_WF/Remade/` and
`Resources/Audio/_WF/Remade/`, with `soundCollection`s in `Resources/Prototypes/_WF/Remade/sounds.yml`. Mob RSIs hold each
limb as a 4-direction state (`<prefix>_chest`, `_head`, `_l_arm`...), `_lying` forms, and composited `full` and `dead`. Check
the meta.json for real state names; they vary. There are no walk frames. A 48px sprite takes `offset: 0,0.25`, like the
slasher; work out the offset for other sizes the same way.

## Adding a mob

A mob is an entity with parent `WFRemadeBase` plus one `wfRemadeMob` prototype giving its cost, stage, spawn weight and
conversion weight. The Director and conversion read those, so no shared table needs editing. Take conversion weights from
DS13-1.0's `conversion.dm`.

## Ownership, so lanes merge cleanly

- Put your prototypes in `Resources/Prototypes/_WF/Remade/<lane>.yml`, your strings in
  `Resources/Locale/en-US/_WF/Remade/<lane>.ftl`, your tests in `Content.IntegrationTests/Tests/_WF/Remade/<Lane>Test.cs`,
  and your code in a subfolder of `Content.Server/_WF/Remade/` or the Shared and Client equivalents.
- Your prompt names any existing file you may edit. Keep edits elsewhere minimal, and say so in your report.
- Don't edit `Content.Server/_WF/Remade/README.md`, and don't commit `modules.py --write` output. Put your README paragraph
  in your report, and I'll merge the docs. `modules.py --check` will complain about stale docs in your lane; that's expected.
- Edits outside `_WF` follow AGENTS.md: a `WOLFGATE(Remade)` marker with a reason, the smallest hook that works.

## Setting up your lane

You start in an isolated worktree cut from `main`, with an empty `RobustToolbox`. In PowerShell:

```powershell
git checkout -f -B lane/<name> <sha from your prompt>
robocopy E:\GitHub\Wolfgate\.claude\worktrees\necromorph-infection-port-1d13e7\RobustToolbox .\RobustToolbox /E /XD bin obj /NFL /NDL /NJH /NJS /NP
```

robocopy exiting with 1 is success. Never run `git worktree remove` or delete `RobustToolbox`.

## Building and testing

At most two lanes build at once on the owner's PC, so stay inside these limits. Run one dotnet command at a time.

```powershell
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt -m:3 -nr:false -v q -clp:ErrorsOnly -p:WarningsAsErrors=nullable
Tools/_WF/Testing/test.ps1 -NoBuild -Workers 2 -Where "test =~ 'Tests[.]_WF[.]Remade[.]'"
```

A cold build takes about 5 minutes. The second command takes about a minute and writes logs to `test_results/`. Don't
run the Release YAML linter, the whole test suite or a real client; I run those after merging. Do start a headless server
if you add prototypes: `cd bin/Content.Server; ./Content.Server.exe --cvar net.port=<1300 + any> --cvar game.auto_pause_empty=false`,
stop it after about a minute, and grep its output for `[ERRO]` or `[FATL]`.

## Test traps found on the slice

- The test map is a vacuum with no gravity. A human goes critical there, even in godmode, and with no living player near
  NPCs fall asleep. Attach the player to `MobCarp`, as `WFRemadeDirectorTest.SpawnCrewman` does.
- `CreateTestMap` is one tile. Lay a deck with `LayDeck`. Keep bodies apart: overlapping mobs push each other, which breaks
  do-afters.
- The pathfinder answers in real time, so poll with `RunUntil` (it has a `Task.Delay`) and allow generous game time.
- NPCs only run with `CCVars.NPCEnabled` on; set it, use `Dirty = true`, and set it back at the end.
- Assert on real behaviour with a failure message saying what went wrong. Never use `Destructive = true`.

## Engine traps

- Only one system may subscribe a given (component, event) pair. `WFRemadeVoiceSystem` holds `DamageChangedEvent` and
  `MobStateChangedEvent` for `WFRemadeVoiceComponent`; use your own component.
- Client and Shared code is sandboxed: see AGENTS.md. Prototype kinds used by both sides go in Shared.
- `[Dependency] private X _x = default!;`, without `readonly`. Short comments, one-line summaries, no licence headers.
- Every player string goes through Fluent. Entity names go in `ent-<ID>` Fluent entries, not YAML.
- Prefer upstream and fork systems where they fit: grep before writing anything new, and say what you reused.

## Finishing

Commit to your lane branch with short lowercase subjects. Don't push or open a PR. Report: branch and head SHA, what works,
what you tested and how, what you cut or left unverified, every file you edited outside your lane, and a README paragraph.
State plainly anything that isn't working. Numbers are starting values: pick sensible ones and list them.
