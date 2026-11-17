# Wolfgate Guidelines

Wolfgate is a fork of Monolith. We merge from upstream a lot, so the whole point of these rules is to keep our stuff separate from theirs. Follow them and merges stay easy.

## 1. Put your stuff in `_WG`

Every feature is a module with a PascalCase name (`Traders`, `ShipPa`, `Tether`). The module name is the folder name everywhere:

| What | Where |
|---|---|
| C# | `Content.Client/_WG/<Module>`, `Content.Server/_WG/<Module>`, `Content.Shared/_WG/<Module>` |
| Tests | `Content.IntegrationTests/Tests/_WG/<Module>` |
| Prototypes | `Resources/Prototypes/_WG/<Module>` |
| Locale | `Resources/Locale/en-US/_WG/<Module>` |
| Sprites, audio | `Resources/Textures/_WG/<Module>`, `Resources/Audio/_WG/<Module>` |
| Ship grids | `Resources/SharedMaps/_WG/<Module>` |
| Other maps | `Resources/Maps/_WG/<Module>` |
| Guidebook | `Resources/ServerInfo/_WG/<Module>` |

- New feature, new module. No loose files sitting directly in a `_WG` folder.
- Namespaces follow the folder (`Content.Server._WG.Traders`).
- Stuff ported from another fork can keep that fork's folder (`_HL`, `_Floof`, ...).

## 2. Mark anything you touch outside `_WG`

If you have to edit an upstream file, mark it with the module it's for.

One line:

```cs
using Content.Shared._WG.Traders; // WOLFGATE(Traders)
DoTheThing(); // WOLFGATE(Traders): traders need this to fire early
```

A block:

```cs
// WOLFGATE(Traders) START: skip the price check for trader vendors
...
// WOLFGATE END
```

- Small edit that belongs to no module: `// WOLFGATE: reason`. The reason is required here.
- YAML, Fluent, Python use `#`. XAML and XML use `<!-- WOLFGATE(Traders): reason -->`.
- In Fluent files put the marker on its own line, never at the end of one.
- Files that can't hold a comment (JSON, sprites, audio, maps) get listed under `unmarked` in
  `Tools/_WG/Ci/modules.yml` instead.

Keep upstream edits as small as you can. Try these in order:

1. A new `_WG` system that subscribes to existing events
2. A partial class in `_WG`
3. A marked edit

Don't reformat, reorder or delete upstream code. Comment it out or branch around it inside a marked block.

## 3. Give your module a README

Each module has a `README.md` in `Content.Server/_WG/<Module>/` (or Shared, Client, or `Docs/_WG/<Module>/` if
there's no server code). Write a few lines by hand: what it does and how players or admins use it.

This is all you need before running the script:

```md
# TractorBeam

Ship-mounted tractor beams. A gunner aims the emitter from the gunnery console and holds fire to pull loose
objects or small grids towards the ship. Needs power and overheats if held too long.

Entry points: `TractorBeamSystem` handles the pull, `WGTractorBeamEmitter` is the emitter you map onto ships.
```

The title is the module name, then a short overview and the main entry points. Longer design notes can go
under that if you want. If you skip the README completely the script makes one with a `TODO` overview, and CI
fails until you replace it.

### The autogenerator

You don't write the rest. The script adds its part to the bottom of your README. The autogenerator reads the module folders and your markers and fills in:

- The file lists in each module README (between the `WOLFGATE-GENERATED` comments): every file in the module
  and every upstream file it edits, with the reasons from your markers.
- `Docs/_WG/NONMODULAR.md`: every standalone `// WOLFGATE: reason` edit.

Run it after adding, moving or removing files or markers, and commit the result:

```bash
python Tools/_WG/Ci/modules.py --write
```

Never edit the generated parts by hand. If they're wrong, fix the marker or the folder and run it again.

To see what CI will complain about before you push:

```bash
python Tools/_WG/Ci/modules.py --check
```

CI fails if the docs are stale or an edit outside `_WG` has no marker, and it prints the marker to add.

## 4. Check it doesn't already exist

Before building something, grep for it. Upstream SS14 or one of the fork layers (`_NF`, `_Mono`, `_DV`, `_CE`, `_Goobstation`) might already have it. If you're porting from another fork, check the systems it depends on exist here too. HardLight especially has renames that will bite you.

## 5. Code style

- No license headers in `_WG` files.
- Short comments. A one line `/// <summary>` on types and public stuff, and a `//` only where it isn't obvious.
- `[Dependency] private X _x = default!;` with no `readonly`.
- No hard-coded player text. Everything goes through Fluent in `Resources/Locale/en-US/_WG`.
- Prototype IDs start with `WG` (`WGTractorBeamEmitter`), unless you're overriding an upstream ID or porting.
- Renaming an entity ID needs a `Resources/migration.yml` entry.
- `Content.Server/_WG/SafetyDepositBox` is a good example to copy from.

## 6. Things that will bite you

- Never edit `RobustToolbox`. Engine changes get talked about first.
- Client and Shared code is sandboxed and the compiler won't warn you. No `BinaryWriter`, no `System.Diagnostics.Process`, no `List<T> x = [...]`, no `string += char`.
- Only one system can subscribe to a given component + event. A duplicate compiles fine and then crashes the server on startup, so grep first.
- In a `DefaultWindow`, don't name controls `CloseButton`, `ContentsContainer`, `TitleLabel` or `WindowHeader`.
- Ship grids go in `Resources/SharedMaps`, since the client doesn't get `Resources/Maps`.
- Keep each file's existing line endings.

## 7. Test before you PR

- Build it.
- Prototype changes: start a server and check the log for `[ERRO]`, then run `dotnet run --project Content.YAMLLinter -c Release`.
- Logic: add a test under `Content.IntegrationTests/Tests/_WG`. Never use `Destructive = true` in `PoolSettings`, CI runs out of memory.
- Client or Shared changes: start a real client and check its log for `Sandbox violation`.
- Don't say it works if you didn't run it.

## 8. PRs

- Never commit to `main`. Make a branch and PR against `VanguardControl/Wolfgate`.
- Fill in the PR template, with a `:cl:` changelog if players will notice the change.
- If you used AI, read the [AI-generated content policy](Docs/_WG/Contributing/AI_POLICY.md) first. AI-generated sprites and audio must be substantially reworked by hand.
- Trust the PR's CI checks over local runs. Some lints and map tests lie on Windows.
