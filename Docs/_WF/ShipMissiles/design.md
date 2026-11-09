# Ship missile guidance

How `WFMissileFlightSystem` flies a missile, why it is shaped that way, and how to retune it.

## The model

Ported from the reference game `space-combat-bullshit`. Per tick:

1. **Diverters.** `λ̇ = cross(r, ṙ)/|r|²`, `V_c = −dot(r, ṙ)/|r|`, `a = clamp(N · V_c · λ̇, ±authority)`, applied
   across the line of sight. `authority = lateralAccel · (0.4 + 0.6 · div/div₀)`, so a missile low on diverters is
   also a lazy one.
2. **No free energy.** If the motor is off, the speed is renormalised back to what it was before the lateral
   command. Diverters turn; only the motor accelerates. Without this, a weaving target makes the missile faster
   and faster, and since turn radius is `v²/a` it becomes a lawn dart that passes wide.
3. **Divert budget.** `div` is drained by `min(|a|, divertCap) · dt`, at 15% price in midcourse and full price in
   the terminal phase. At zero the lateral command is zero. If the missile is also *opening* for `dudOpeningTime`,
   it is deleted: a clean miss rather than a stray hit on a third party.
4. **Motor.** `boostAccel` for `boostTime`, then coast, capped at `maxSpeed`.
5. **Seeker.** Live data while tracking; a notch (`|V_c| < notchBand` while moving) accumulated past
   `notchMemory` puts it on memory for `memoryTime`, flying `knownPosition + knownVelocity · t`; the geometry
   reopening re-locks it, running out of memory loses it for good.

## Why a carrier plus a flight system

Upstream `TargetSeekingSystem` flies missiles by pointing the nose at a predicted intercept and thrusting along it.
It also owns three things this module must not break:

| Consumer | What it reads |
|---|---|
| `TargetSeekerAlertSystem` | `EntityStartedBeingSeekedTargetEvent`, whose payload is `Entity<TargetSeekingComponent, TransformComponent>` |
| `WFCombatConsoleSystem.Flares.cs` | `TargetSeekingComponent`'s `CurrentTarget`, `DetectionRange`, `ScanArc`, `TrackDelay` |
| `RadarBlipSystem` | `TargetSeekingComponent.ArcLines` |

So the missile keeps the component with its flight terms zeroed and lets upstream do acquisition and lock events
only. Concretely the carrier is set to `acceleration: 0`, `launchSpeed: 0`, `turnRate: 0`,
`trackingAlgorithm: Direct`, with `detectionRange`, `scanArc`, `trackDelay` and `maxSpeed` left at their real
values. `Direct` matters: the predictive laws divide by closure rate or acceleration, and `acceleration: 0` would
put a NaN into the transform. `turnRate: 0` makes `RotateToFaceSystem.TryRotateTo` write the current rotation
back, which is a no-op.

Flares, alerts and radar keep working for free. The one other reader of the flight terms is the NPC gunner
(`ShipTargetingSystem`), which leads a seeker by its `acceleration`; it now reads `boostAccel` from
`WFMissileFlight` instead, since the carrier's is zero. The cost is that
acquisition is upstream's: instant, nearest blip in the cone, no seeker acquisition timer. `trackDelay` already
covers the launch window, so nothing is lost for gameplay.

## Release

A missile is soft-launched rather than dropped down whatever line the gun happened to point at. At release it takes a
bearing — the console's aim point if `FireControlSystem` passed one, else the hull it has just locked — clamped to
`launchOffBoresight` so it is never handed a turn it cannot fly, and blended from the nose by `snapFraction`: the
ASM-50 snaps its whole velocity over, everything else swings. Re-pointing also removes the crab left by inheriting the
launcher's velocity.

For `launchTransient` seconds afterwards the missile stabilises for free: `launchTurnMultiplier` extra authority, a
command damped from `launchDamping` up to full, and no divert impulse spent. Upstream's `scanArc` is 7.5 to 17.5
degrees of half-cone, far too narrow to hand a missile a target at release, so on the release tick the missile looks
once inside its own wider `launchAcquireCone` and range. A missile with no diverters (the unguided ASM-150) skips this,
so it never raises a lock it cannot fly. Once it ends, upstream's rules — and the flare decoys that read `scanArc` — are
untouched, which is why the soft launch cannot be used to make flares stronger.

The console's aim point is stored on a fired missile by a short loop inside the existing `AmmoShotEvent` handler,
ahead of its `TargetGuided` loop (none of the flown missiles is `TargetGuided`). It has to live there: a second subscription to `GunComponent, AmmoShotEvent`
would crash the server at startup.

## Notch and memory

The notch is not a special case bolted on: for a target crossing square to the line of sight the closure rate is
zero, so proportional navigation commands nothing and the missile flies on. The notch test only decides when to
stop *believing* the seeker, and the memory flight is what lets a beamed missile drift past instead of snapping
back onto the target it can no longer see.

## Tuning

A hull's lateral demand is about `v · ω`. `ShuttleComponent` caps hulls at `BaseMaxLinearVelocity = 30` m/s and
`MaxAngularVelocity = 4` rad/s, so:

| Hull | Speed | Turn | Lateral demand |
|---|---|---|---|
| Light escort | 30 m/s | 1.5 rad/s | ~45 m/s² |
| Heavy | 30 m/s | 0.4 rad/s | ~12 m/s² |

`lateralAccel` should be two to three times the demand of the target class the missile is meant to threaten, and
`divertBudget` about one to two seconds of full deflection. Before this change the ASM-150 pulled 250 m/s²
forever — five to twenty times a hull's authority, which is why it could out-turn anything and still miss: the
pursuit law aimed it at the wrong place.

The fuse is not modelled here. `ShipMissileBase` already carries a 4 m `hit` fixture and `ProjectileGridPhase`
exempts the shooter's grid, so proximity is physical contact with that radius; widen the fixture on a prototype
that needs a fatter fuse.
