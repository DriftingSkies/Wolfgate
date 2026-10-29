# Tether

Ropes between two attach points, and the content built on them: anchor eyes, the tether installer that bolts an eye to
a tile at range, the manned harpoon turret that leaves a tow cable in a ship it hits, and power cords that join two
hulls' power nets.

Players pay rope out from one attach point with a coil and use the coil on a second point to tie it off; attach points
have verbs to untie and to take in or pay out slack, and a cutting tool cuts a rope. Entry points: `RopeSystem`
(server; the API for creating, lengthening and breaking ropes), `RopeComponent` and `RopeAttachPointComponent`,
`TetherInstallerSystem`, `SharedShipHarpoonTurretSystem` with `ShipHarpoonTurretSystem`, `PowerCordSystem`, and on the
client `RopeVisualizerSystem` and `RopeOverlay`. The notes below describe the rope core; `Docs/_WF/Tether/design.md`
is the full design.

## Ropes

The rope core that harpoons, tow cables and power cords are built on. Code lives in
`Content.{Shared,Server,Client}/_WF/Tether/`.

### How a rope works

A rope is its own lightweight entity carrying `RopeComponent`, with a global PVS
override so both hulls can draw it. Its ends are two entities with
`RopeAttachPointComponent`.

The physical body a rope pulls on is the attach point's **grid** while the point is
anchored (or has no dynamic body of its own), otherwise the point's own body, so
loose crates can be towed. Both ends on the same body means no joint and no forces:
the rope is visual only. A towed hull's cargo weight (CargoLoad) is not added to the towing ship's: a loaded barge
on a rope is a tug job, and a loose crate on a rope pulls with its real mass.

Rope is one sided. Below its rest length it does nothing at all. Past it, the server
applies a spring-damper as impulses at both anchor points (plus the torque each
anchor arm generates). The engine has no rope joint, so the inextensible limit is a
`DistanceJoint` with `Stiffness = 0`, `MinLength = 0` and
`MaxLength = Length * (1 + maxStretch)`; with stiffness zero and distinct limits the
engine skips its two-sided soft spring entirely and solves only the upper bound,
which is exactly what a rope needs. Damping acts on separating motion only, and the
spring impulse is clamped so it can never reverse the relative motion past the rest
length — a 1 kg body on a 5 MN/m rope stays stable.

A rope severs when an end is deleted, when the ends end up on different maps (joints
cannot span maps, so FTL cuts ropes), or when the tension rule fires. When an end's
body changes underneath it — unanchored, re-anchored, embedded — the joint is quietly
re-created against the new bodies rather than snapping.

### Tuning: `ropeType` prototype

| Field | Meaning |
| --- | --- |
| `stiffness` | N/m **per metre of rope**. The spring constant is `stiffness / length`, so a long rope is softer. |
| `dampingRatio` | Fraction of critical damping, applied to separating motion only. |
| `maxStretch` | Fraction past the rest length where the hard limit sits. Also the denominator of `Strain`. |
| `breakForce` | Tension in newtons that parts the rope. Above it for 0.25 s, or instantly above twice it. `0` is unbreakable. |
| `maxLength` | Longest rest length the rope may be set to. |
| `loadBearing` | `false` (power cords): no joint and no force, but the rope still snaps once stretched past the limit. |
| `color` / `tautColor` | Drawn colour, lerped by strain. |
| `width` | Drawn width in metres. Thins as the rope goes taut. |
| `segmentsPerMetre` | Verlet chain resolution; the point count is clamped to [8, 96]. |
| `stackType` | Stack refunded when the rope is untied. |
| `breakSound` | Played when it parts. |

`RopeCoilComponent` (on a `Stack` item) carries `ropeType`, `metresPerUnit` (1) and
`slack` (1.15, the rope laid per metre of separation).
`RopeAttachPointComponent` carries `maxRopes` (1) and `localOffset`.

### Public API (`RopeSystem`, server)

```csharp
bool TryCreateRope(EntityUid a, EntityUid b, ProtoId<RopeTypePrototype> type, float length, out EntityUid? rope);
bool SetLength(EntityUid rope, float length);            // clamps to [0.5 m, type.MaxLength]
void BreakRope(EntityUid rope, bool refund = false, EntityUid? user = null);
float GetTension(EntityUid rope);                        // newtons, 0 while slack
Vector2 GetAnchorPosition(EntityUid point);
bool TryGetBody(EntityUid point, out EntityUid body, out Vector2 localAnchor);
```

Events (all `[ByRefEvent]`, in `Content.Shared._WF.Tether`):

- `RopeAttachedEvent(Rope, Other, RopeType)` — on both attach points when tied.
- `RopeDetachedEvent(Rope, Other, RopeType)` — on both attach points when untied, for any reason.
- `RopeBrokenEvent(Rope, EndA, EndB, RopeType, Refunded)` — on both ends and broadcast, before deletion.
- `RopeCoilTargetAttemptEvent(User, Coil, Target, RopeType)` with `AttachPoint` and `Handled` —
  raised on an entity a coil was used on that is not itself an attach point. The power cord
  clamps set `AttachPoint` here instead of copying the coil flow.

### Hand interaction

Use a coil on an attach point to take the loose end off it (`RopeCarrierComponent` on
the player, visual-only rope). Dropping or stowing the coil, using it in hand, or
walking past the coil's reach drops the end. Use the coil on a second point to tie
off: the length is `max(distance * slack, 1)` rounded up to whole units, and must fit
both the coil and `maxLength`.

Verbs on an attach point, per rope: **Untie** (2 s doafter, refunds exactly the units
paid out), and alt-verbs **Take in slack** / **Pay out** (one metre; paying out spends
a unit from a held coil of the same type, taking in returns one, and is refused above
0.9 strain). Any tool with the `Cutting` quality cuts every rope on the point with no
refund. Examining a point lists its ropes, their lengths and a coarse strain word.

### Client

`RopeVisualizerSystem` keeps a client-only verlet chain per rope entity and steps it
in `FrameUpdate` with capped substeps; `RopeOverlay` draws it as a mitred quad strip,
a darker outline pass under a lighter core. There is no gravity, so slack comes from
the pinned ends' own motion plus a small per-point drift — slack rope curls, trails
and whips, taut rope straightens, tints, thins and buzzes. A pinned end jumping more
than 10 m re-seeds the chain. Ropes off screen keep their chain but are neither
stepped nor drawn.

### Debug content

`debug.yml` and `rope_types.yml` in `Resources/Prototypes/_WF/Tether/` keep the
`WFRopeDebug` rope type, the `WFRopeAttachPointDebug` eye and the `WFRopeCoilDebug` coil
next to the real content, so the core can be tried in game on its own.

### Tests

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~Tether" --logger "console;verbosity=normal"
```

`RopeMathTest` covers the pure spring and verlet maths with no server pair.
`RopeTest` ties two dynamic grids together and checks the hard limit, towing, the
absence of force while slack, breaking, severing on deletion and the coil's unit
accounting. `TetherContentTest` spawns the real content, fires the installer and ties
each real rope type between two anchor eyes. `HarpoonTest` covers manning the turret,
the shot-well rule, the tow cable a good shot leaves, loose and loaded harpoons staying
plain items and the winch stopping at hull contact. `PowerCordTest` checks that a
cord joins two hulls' power nets until it parts, and refuses the wrong voltage.
Rerun any Skipped test on its own: pair tests can be skipped by unrelated
`db.ef` sqlite warnings on some machines.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Tether/Harpoon/ShipHarpoonTurretSystem.cs`](Harpoon/ShipHarpoonTurretSystem.cs)
- [`Content.Server/_WF/Tether/PowerCord/PowerCordNode.cs`](PowerCord/PowerCordNode.cs)
- [`Content.Server/_WF/Tether/PowerCord/PowerCordSystem.cs`](PowerCord/PowerCordSystem.cs)
- [`Content.Server/_WF/Tether/RopeSystem.cs`](RopeSystem.cs)
- [`Content.Server/_WF/Tether/RopeSystem.Interaction.cs`](RopeSystem.Interaction.cs)
- [`Content.Server/_WF/Tether/TetherInstallerSystem.cs`](TetherInstallerSystem.cs)

### Shared

- [`Content.Shared/_WF/Tether/Harpoon/HarpoonEvents.cs`](../../../Content.Shared/_WF/Tether/Harpoon/HarpoonEvents.cs)
- [`Content.Shared/_WF/Tether/Harpoon/MannedTurretOperatorComponent.cs`](../../../Content.Shared/_WF/Tether/Harpoon/MannedTurretOperatorComponent.cs)
- [`Content.Shared/_WF/Tether/Harpoon/SharedShipHarpoonTurretSystem.cs`](../../../Content.Shared/_WF/Tether/Harpoon/SharedShipHarpoonTurretSystem.cs)
- [`Content.Shared/_WF/Tether/Harpoon/ShipHarpoonComponent.cs`](../../../Content.Shared/_WF/Tether/Harpoon/ShipHarpoonComponent.cs)
- [`Content.Shared/_WF/Tether/Harpoon/ShipHarpoonTurretComponent.cs`](../../../Content.Shared/_WF/Tether/Harpoon/ShipHarpoonTurretComponent.cs)
- [`Content.Shared/_WF/Tether/PowerCord/PowerCordClampComponent.cs`](../../../Content.Shared/_WF/Tether/PowerCord/PowerCordClampComponent.cs)
- [`Content.Shared/_WF/Tether/RopeAttachPointComponent.cs`](../../../Content.Shared/_WF/Tether/RopeAttachPointComponent.cs)
- [`Content.Shared/_WF/Tether/RopeCarrierComponent.cs`](../../../Content.Shared/_WF/Tether/RopeCarrierComponent.cs)
- [`Content.Shared/_WF/Tether/RopeCoilComponent.cs`](../../../Content.Shared/_WF/Tether/RopeCoilComponent.cs)
- [`Content.Shared/_WF/Tether/RopeComponent.cs`](../../../Content.Shared/_WF/Tether/RopeComponent.cs)
- [`Content.Shared/_WF/Tether/RopeEvents.cs`](../../../Content.Shared/_WF/Tether/RopeEvents.cs)
- [`Content.Shared/_WF/Tether/RopeMath.cs`](../../../Content.Shared/_WF/Tether/RopeMath.cs)
- [`Content.Shared/_WF/Tether/RopeTypePrototype.cs`](../../../Content.Shared/_WF/Tether/RopeTypePrototype.cs)
- [`Content.Shared/_WF/Tether/TetherInstallerComponent.cs`](../../../Content.Shared/_WF/Tether/TetherInstallerComponent.cs)

### Client

- [`Content.Client/_WF/Tether/Harpoon/ShipHarpoonTurretSystem.cs`](../../../Content.Client/_WF/Tether/Harpoon/ShipHarpoonTurretSystem.cs)
- [`Content.Client/_WF/Tether/RopeChain.cs`](../../../Content.Client/_WF/Tether/RopeChain.cs)
- [`Content.Client/_WF/Tether/RopeOverlay.cs`](../../../Content.Client/_WF/Tether/RopeOverlay.cs)
- [`Content.Client/_WF/Tether/RopeVerlet.cs`](../../../Content.Client/_WF/Tether/RopeVerlet.cs)
- [`Content.Client/_WF/Tether/RopeVisualizerSystem.cs`](../../../Content.Client/_WF/Tether/RopeVisualizerSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Tether/HarpoonTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Tether/HarpoonTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Tether/PowerCordTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Tether/PowerCordTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Tether/RopeMathTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Tether/RopeMathTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Tether/RopeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Tether/RopeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Tether/TetherContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Tether/TetherContentTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Tether/anchor_eye.yml`](../../../Resources/Prototypes/_WF/Tether/anchor_eye.yml)
- [`Resources/Prototypes/_WF/Tether/debug.yml`](../../../Resources/Prototypes/_WF/Tether/debug.yml)
- [`Resources/Prototypes/_WF/Tether/harpoon.yml`](../../../Resources/Prototypes/_WF/Tether/harpoon.yml)
- [`Resources/Prototypes/_WF/Tether/installer.yml`](../../../Resources/Prototypes/_WF/Tether/installer.yml)
- [`Resources/Prototypes/_WF/Tether/power_cord.yml`](../../../Resources/Prototypes/_WF/Tether/power_cord.yml)
- [`Resources/Prototypes/_WF/Tether/Recipes/anchor_eye_graph.yml`](../../../Resources/Prototypes/_WF/Tether/Recipes/anchor_eye_graph.yml)
- [`Resources/Prototypes/_WF/Tether/Recipes/anchor_eye_recipe.yml`](../../../Resources/Prototypes/_WF/Tether/Recipes/anchor_eye_recipe.yml)
- [`Resources/Prototypes/_WF/Tether/Recipes/lathe_pack.yml`](../../../Resources/Prototypes/_WF/Tether/Recipes/lathe_pack.yml)
- [`Resources/Prototypes/_WF/Tether/Recipes/lathe_recipes.yml`](../../../Resources/Prototypes/_WF/Tether/Recipes/lathe_recipes.yml)
- [`Resources/Prototypes/_WF/Tether/Recipes/power_cord.yml`](../../../Resources/Prototypes/_WF/Tether/Recipes/power_cord.yml)
- [`Resources/Prototypes/_WF/Tether/rope_types.yml`](../../../Resources/Prototypes/_WF/Tether/rope_types.yml)
- [`Resources/Prototypes/_WF/Tether/ropes.yml`](../../../Resources/Prototypes/_WF/Tether/ropes.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Tether/content.ftl`](../../../Resources/Locale/en-US/_WF/Tether/content.ftl)
- [`Resources/Locale/en-US/_WF/Tether/harpoon.ftl`](../../../Resources/Locale/en-US/_WF/Tether/harpoon.ftl)
- [`Resources/Locale/en-US/_WF/Tether/power-cord.ftl`](../../../Resources/Locale/en-US/_WF/Tether/power-cord.ftl)
- [`Resources/Locale/en-US/_WF/Tether/rope.ftl`](../../../Resources/Locale/en-US/_WF/Tether/rope.ftl)

### Textures

- [`Resources/Textures/_WF/Tether/anchor_eye.rsi/`](../../../Resources/Textures/_WF/Tether/anchor_eye.rsi/)
- [`Resources/Textures/_WF/Tether/harpoon_projectile.rsi/`](../../../Resources/Textures/_WF/Tether/harpoon_projectile.rsi/)
- [`Resources/Textures/_WF/Tether/harpoon_turret.rsi/`](../../../Resources/Textures/_WF/Tether/harpoon_turret.rsi/)
- [`Resources/Textures/_WF/Tether/installer.rsi/`](../../../Resources/Textures/_WF/Tether/installer.rsi/)
- [`Resources/Textures/_WF/Tether/power_cord_clamps.rsi/`](../../../Resources/Textures/_WF/Tether/power_cord_clamps.rsi/)
- [`Resources/Textures/_WF/Tether/power_cord_coils.rsi/`](../../../Resources/Textures/_WF/Tether/power_cord_coils.rsi/)
- [`Resources/Textures/_WF/Tether/rope_bungee.rsi/`](../../../Resources/Textures/_WF/Tether/rope_bungee.rsi/)
- [`Resources/Textures/_WF/Tether/rope_hemp.rsi/`](../../../Resources/Textures/_WF/Tether/rope_hemp.rsi/)
- [`Resources/Textures/_WF/Tether/rope_steel_cable.rsi/`](../../../Resources/Textures/_WF/Tether/rope_steel_cable.rsi/)
- [`Resources/Textures/_WF/Tether/rope_synthetic.rsi/`](../../../Resources/Textures/_WF/Tether/rope_synthetic.rsi/)
- [`Resources/Textures/_WF/Tether/rope_tow_cable.rsi/`](../../../Resources/Textures/_WF/Tether/rope_tow_cable.rsi/)

### Tools

- [`Tools/_WF/Tether/brown_rope.png`](../../../Tools/_WF/Tether/brown_rope.png)
- [`Tools/_WF/Tether/bungee_rope.png`](../../../Tools/_WF/Tether/bungee_rope.png)
- [`Tools/_WF/Tether/cable_rope.png`](../../../Tools/_WF/Tether/cable_rope.png)
- [`Tools/_WF/Tether/harpoon_sprites.py`](../../../Tools/_WF/Tether/harpoon_sprites.py)
- [`Tools/_WF/Tether/metal_rope.png`](../../../Tools/_WF/Tether/metal_rope.png)
- [`Tools/_WF/Tether/nylon_rope.png`](../../../Tools/_WF/Tether/nylon_rope.png)
- [`Tools/_WF/Tether/power_cord_sprites.py`](../../../Tools/_WF/Tether/power_cord_sprites.py)
- [`Tools/_WF/Tether/tether_sprites.py`](../../../Tools/_WF/Tether/tether_sprites.py)
- [`Tools/_WF/Tether/tow_eye_metal.png`](../../../Tools/_WF/Tether/tow_eye_metal.png)

### Docs

- [`Docs/_WF/Tether/design.md`](../../../Docs/_WF/Tether/design.md)

## Non-modular edits

- [`Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs`](../../../Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs)
  - manned turret gun relay
  - a mounted weapon can stand in for whatever the entity is holding, e.g. a manned harpoon turret
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml)
- [`Resources/Prototypes/Recipes/Lathes/Packs/shared.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/shared.yml): power cord coils

<!-- WOLFGATE-GENERATED END -->
