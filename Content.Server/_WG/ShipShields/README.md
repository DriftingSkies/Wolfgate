# Ship shields

The gunnery console carries a simplified shield control on its right side: the bearing dial, raise/lower, arc, shunt
power and heat, with none of the helm page's detail. It only shows while the ship has a shield generator. A
gunner at a powered, anchored console they can access may work the shields of the ship it sits on
(`WGShipShieldShuntSystem`); the console is sent the same shield state as the helm.

Ship shields use a symmetric oval fitted around the occupied hull, with a five-tile margin instead of the previous eight-tile hull-tracing field. Long ships get an oval and square ships get a circle; detached sections on one grid share the same envelope. A regular ship-local hexagonal lattice fades inward from the edge. Traveling impact ripples and repeated-hit heat remain local, while the overall colour shows remaining emitter capacity. Hull changes refresh the collision perimeter and visuals together without disabling interception.

The upstream ship shield emitter keeps its power, damage, recharge and projectile rules. Hitscan beams intersect the same perimeter and shunt sectors, charge emitter capacity, and trigger impact effects. Ship-fired projectiles sweep their full travel path each tick, including slow mining pulses. Collision-triggered ship warheads are absorbed before their explosion trigger runs, regardless of projectile collision subscriber order. Absorption also clears pending timers and proximity triggers before deletion. Ordinary hull contacts still detonate normally. Absorbed EMP warheads retain their area EMP pulse and capped EMP capacity cost without activating explosive or deployment payloads. Flak fragments, mine-laying shells and deployed naval mines are intercepted; deployed payloads inherit their launcher's grid so they pass through their own shields. Confirmed shield ray hits absorb the round immediately instead of waiting for a later thin-edge physics contact; the nearest obstacle and existing collision filters still apply. Shots from the protected ship pass outward. Emitter ownership and deleted-field handles are reconciled before each update; actual field transitions are logged for diagnosis. `ShipShieldsSystem.Wolfgate.cs` supplies hull fixtures and visual updates; `WGShipShieldGeometry` builds the shared contours. Collision edges retain a small physical radius to catch slower projectiles between physics steps.

Confirmed damaging projectile and hitscan absorption raises `WGShipShieldAttackedEvent` with the protected grid,
attacking grid, shooter and weapon. NPC escort crews use it to retaliate even when shields prevent all hull damage.
Attribution prefers the launch grid, then the weapon and shooter grids. Collision probes, harmless shots, shield
gaps and shots leaving the protected ship do not report attacks; a projectile cannot report the same absorption twice.
`WGShipShieldInterceptAttemptEvent` allows crew friendly-fire rules to pass protected projectiles and beams
through before clipping or absorption, preserving capacity and suppressing impact effects. Launched crew rounds
retain their protection when the cannon changes hands. Explicit Attack orders and retaliation still penetrate
normal faction protection after leaving an escort formation.

FTL spool-up immediately lowers shields on the departing ship and docked ships travelling with it. Deployment remains locked through travel, arrival and the full jump cooldown, including manual and admin deployment attempts. Normal power, recharge and manual-switch rules resume when FTL ends; the lockout does not reset shield damage or recharge progress. The helm and generator Shields panels show the FTL lockout while it applies.

Hull collisions spend the impacted ship's remaining shield capacity before damaging its hull. Absorption uses the existing collision energy-to-damage conversion and current local shunt strength; unpowered sectors and exhausted or offline fields let impact damage through. Each ship pays for its own protection, and excess energy reaches the hull when capacity runs out. The shield never becomes a solid barrier: normal hull contact, slowdown and occupant inertia remain. Absorbed collisions trigger the same localized, damage-scaled shield shimmer and rate-limited impact sound as weapons. Collision depletion immediately drops the field; a ship generator then waits out its overload like any other collapse, an outpost generator recovers by the upstream recharge/overload rules.

Healthy shields use the generator's configured shield colour for their faint edge and readable idle hexes, including radar outlines. Damage blends that colour through amber to red; local impact heat reddens both the surface and hex lattice while untouched areas retain the overall health colour. The helm shows the same condition as a heat gauge that fills and reddens as the generator heats. The field becomes more visible as health drops, and smooth traveling shimmer crests brighten it briefly. Hits reveal the surface and nearby hexes with a bright white flash confined to the impact zone. Traveling ripples brighten the shield in its health colour, followed by a broader red wake. Impact brightness and size scale with absorbed damage relative to full generator capacity, including explosive/EMP damage, hitscan modifiers and shunting. Small hits create compact dim patches and shorter ripples; hits consuming at least 5% of capacity reach the full visual response. The lower of the overload and power limits determines capacity, so stronger generators react less to the same round. Full-strength ripples travel for four seconds at 22 tiles per second; sustained fire leaves a brighter red patch that cools gradually over a sixteen-second lifetime. Untouched healthy surfaces remain faint without hiding the idle hex lattice. Surface and hex opacity are sampled separately without rebuilding meshes. Each vertex blends four cached samples to prevent square impact-lighting patches.

Shield startup uses a coloured forming sweep before settling to normal visibility. Shutdown removes protection immediately and leaves a short visual-only copy: combat/power collapse flares and dissolves in smooth uneven patches, while manual lowering fades evenly. Copies retain the generator colour, hull pose and allocation, expire within a second, and are replaced rather than stacked when shields restart. Radar and helm status continue to show actual protection.

The client caches hull meshes and hex lines and animates shimmer in a lightweight shader. Below 16 pixels per tile, oval shields use at most 96 segments (1,728 vertices and 384 direct impact samples), regardless of station size. Distant flashes cover the gaps between sparse samples; normal views retain the detailed hex lattice and interpolated lighting. Legacy non-oval contours retain their original outline. Health-only network deltas preserve the cached outline; impacts retain coarser feedback when zoomed out. Impact sounds randomly choose from three synthesized impact designs (soft bubble, warbling field and heavy shield), each with a soft echo tail. Impacts play at +4 dB with a 0.75-1.1 second per-ship cooldown that survives shield collapse. At most two echo tails overlap; another impact retires the oldest tail. Global audio visibility and a hull-sized full-volume radius let occupants hear impacts throughout large stations.

At the helm (when a generator is installed), or directly at an anchored shield generator, open **Shields** to choose a ship-relative bearing (0° forward, 180° rear), an arc width from 30° to 360°, and concentration from 0% to 100%. Changes send live without an Apply step; the white marker and outline show the requested target while the coloured ring animates actual protection. Concentration moves power from the remaining perimeter into the chosen arc; at 100%, the rest of the perimeter disappears and lets projectiles through. The arc's strength follows a cube root of how much narrower it is than the full ring (`WGShipShieldShuntMath.PeakStrength`), so a full 30° shunt reaches about 2.3x and 90° about 1.6x; shunting always costs some total protection. Regeneration is the same whatever the arc and concentration. Reset requests even coverage. Direction and width turn at up to 30 degrees per second, and power moves at up to 20 percentage points per second, easing into the target, so a faster ship can work around a concentrated arc. Server allocation advances at most ten times per second; partial allocations reuse full-perimeter fixtures, and only fully open sectors require clipped fixture updates. Raise/lower controls switch deployment without discarding allocation or bypassing recharge. The allocation ring and thicker heat gauge share health colours and pulse red above 90% heat. Ring segments use wider pixel-sized gaps and fewer segments at smaller display sizes to keep low-resolution edges readable. When offline, the shared panel shows an estimated return countdown from the emitter recharge and overload rules, including delayed overload reset and the next update tick. Missing power, manual lowering, and disabled grids show the blocking condition instead. Allocation persists on the grid through collapse/recharge and is shared by every helm. Open helms poll authoritative emitter capacity and field availability every 0.2 seconds, sending shield-only snapshots when the visible percentage, recovery second, or settings change without rebuilding navigation, map or docking state. Field removal immediately reports offline, including entities queued for deletion. Power loss can still collapse a field before capacity reaches zero under the existing emitter rules. Stronger arcs absorb more incoming damage per unit of emitter capacity while weaker arcs consume more.

A pilot can also work the field without opening the panel. The shield key (F, **Raise or lower ship shields**) sends `WGShipShieldToggleEvent`, and `TryToggleFromHelm` flips the flown ship's field under the helm's raise/lower checks; Descend moves from F to G. The vent key (V, **Vent ship heat**) sends `WGShipVentKeyEvent`, and `TryVentFromHelm` vents under the helm's vent checks, on a bare hull too. While a ship's generator waits out an overload or a collapse has its guns locked, the hull carries `WGShipOverloadedComponent` (`ShipShieldsSystem.Radar.cs`, refreshed every 0.25 s), and every radar that detects it draws a broken, flickering cyan ring with sparks and an **OVERLOADED** caption above the hull (`DrawWolfgateOverloads`). The field entity only reaches clients within about 100 m of the hull, so the same update copies a raised field's outline, colour and health (in 5% steps) onto the grid as `WGShipShieldRadarComponent`; radars draw nearby fields from the field itself and any other fully detected ship's from that copy, as far as the radar sees it.

WGShipShieldShuntMath defines the allocation and clipped sectors; WGShipShieldShuntSystem validates helm requests; the client shunting screen and shield overlay show the same allocation.

Navigation and fire-control radar views draw the hull contours through `ShuttleNavControl.ShipShields.cs`. Cached map outlines show health colours and allocation strength, leave fully unpowered sectors open, and retain radar detection and FTL visibility rules. Radar outlines use the protected grid transform, matching the vessel drawing even when the separate shield entity receives a delayed rotation update.

A raised field is not free. `WGShipShieldCostComponent` on a generator sets what its field costs the ship while it is up (`ShipShieldsSystem.Costs.cs`): a share of linear thrust and of turning thrust (`MoverController.ShipShields.cs`; braking is cut too, in a straight line and in a turn), a steady thermal signature that widens the range the ship is detected at, and a burst of signature heat for every point of damage the field absorbs. When damage beats the field down, by weapons or by a hull collision, every fire-controlled gun on the ship refuses to fire until the generator's overload wait ends, or for `gunLockout` seconds on a generator that sets one; each mounted gun is wreathed in cyan sparks (`WGEffectShieldGunLock`) for as long as the lock lasts, the shield panels show the countdown and open gunnery consoles get a popup. Losing power, FTL and lowering the field by hand do not lock the guns, so dropping shields early is the way out. The MS-100, MS-250, MS-500 and LM-212 carry the component with heavier costs on the larger generators; the MS-500 is the cheapest to lose, with a 14 second overload wait and a 3 second gun lock behind the heaviest thrust cost, and its collapse sheds the ship's heat down to `collapseHeat` (80%), under the mark where hot guns resume, so the lock is all that silences them; the Parasol has the MS-100's 16 second wait, lighter thrust and turning costs (17% and 15% against 25% and 20%) and half its signature, and outpost generators have none. A generator without the component costs nothing. The costs show on examine and in **Generator stats**.

Shield health is the ship's heat (`ShipShieldsSystem.Heat.cs`), the flux of other ship games under another name. Heat lives on the hull, in `WGShipHeatComponent` on the grid; a shieldless ship has the same pool. A ship generator's field turns `hitHeat` (0.8, `WGShipShieldEffects.HitHeat`) of the damage it absorbs into heat, so it soaks a quarter more than the ship's heat capacity in damage before it breaks; an outpost's field and a hull collision are charged point for point. A generator does not own heat. A ship generator adds its capacity (its overload limit or its power cap, whichever is lower) to what the hull holds by its size, and adds its heal rate to what the hull sheds by its own size (`WolfgateHullCooling`); an outpost generator's capacity replaces the hull's. A ship generator without power cools nothing of its own (`WolfgateGeneratorCooling`): only the hull's own rate is left, a vent included, though the capacity stays, so a dead generator is no radiator. The upstream emitter rules still run on the generator's `Damage`, which is kept at the hull's heat scaled to the generator's own range (`WolfgateGeneratorShare`, the generator's capacity over the ship's), so the generator reaches its limits when the whole ship is full. Hits charge the generator that same share, so the heat a hit adds does not depend on how the capacity is split, while anything that sets `Damage` outright (siege, ward, admin tools) sets the same fraction of the ship's capacity: each tick whatever raised or set it (absorbed hits, siege, ward and admin tools) is carried to the hull, the cooling is done on the hull itself (`CoolWolfgateHull`), and every generator on the hull reads the same heat back. Only one generator cools a hull, the one holding the field and after it drops the same one, so a spare generator is neither a second health bar nor an extra heat sink. A ship takes one ship generator in any case (`WGShipShieldLimitSystem`): another is refused at the wrench and comes loose if a flatpack, a build or a rebuild bolts it down, before the hull bond can take it. A hull loaded from a save never map-inits, so one saved with two keeps both. A generator taken off its hull leaves the heat behind; one installed on a hot hull takes it on. Heat stops at the ship's capacity, as Starsector's flux does (`CapWolfgateShipHeat`): a hit that would carry a raised field past it collapses the field however big it is, and with the field already down the excess is lost, so one huge warhead costs one overload and no longer a minutes-long cool. One hit a ship generator's field absorbs, a projectile, a beam or a round a deflector slows, adds at most `WGShipShieldEffects.HitHeatCap` (a fifth) of the ship's capacity (`WolfgateHitCharge`), so no single warhead breaks a field with more than a fifth of its room left and a cool one takes five of the biggest; hull collisions are not capped. Small rounds charge less: a hit under `FullHitDamage` (200) charges its share of it, down to `MinHitWeight` (a quarter), judged on the hit before shunting (`WGShipShieldEffects.HitWeight`), so a Phalanx or L85 presses a field without stunlocking it and slow, heavy guns are what push a hot ship into overload; Mono's anti-shield rounds hit big and are charged in full. Heat past the limit, or a generator reaching its power cap, collapses the field, with the gun lock above. A ship generator's field comes back after its overload wait (`damageOverloadTimePunishment`: 14 seconds on the MS-500, 16 on the MS-100 and Parasol, 18 on the LM-212, 20 on the MS-250, times the ship's `WGShipShieldEffects.SizeScale` (`OverloadWait`): the square root of its heat capacity over 40000, from 0.75 to 1.5, so a capital is out about half again as long as a frigate; `WolfgateOverloadWait`) with whatever heat the ship still holds, as Starsector does; it never waits for the hull to cool to zero (`ShipShieldsSystem.Recovery.cs`). As in Starsector, the ship can't vent during the wait, so it can't trade the wait for an empty field. Losing power holds the field down only until power returns. Heat comes in two kinds on a ship generator (one with `WGShipShieldCostComponent`). Hard heat is what came through the field; soft heat is the rest, such as the ship's own gun fire. Under a raised field soft heat cools at that rate and hard heat stays, until the raised generator's `hardHeatHold` seconds have passed without a hit, so a ship under fire cannot wait its shield back up. The hold is 30 seconds by default (`WGShipShieldEffects.HardHeatHold`), 15 on `ShieldGeneratorTSFCapital`, which fights on under its field, and 45 on `ShieldGeneratorMedium`, which soaks one long fight and then has to leave. With the field down, whether lowered by hand, collapsed or locked out by FTL, everything cools at `WGShipShieldEffects.FieldDownCooling` (2) times that rate, soft heat first, so standing down is a real option between fighting on and venting. Every ship, with a generator or without, cools by this one rule: its plain rate (the hull's own, plus a powered generator's heal rate) times the multiplier for its state, 1 with the field raised or overloaded, 2 with it down otherwise or with no field, 6 venting within the vent cap below (`WGShipShieldEffects.CoolingRate`, `WolfgateCoolingRate`). An overload cools at the plain rate, as in Starsector, so the wait is no free refill and the field usually comes back hot. Outpost generators keep the upstream rule: no hard heat, and fast cooling only in recharge mode. Every point of heat a hull holds adds `heatSignature` (25) to its thermal signature, so a hot ship is detected from farther off, field up or down.

A vent (`ShipShieldsSystem.Vent.cs`, `StartWolfgateVent`) is the fast way: **Vent heat** on the helm, generator and gunnery shield panels drops the field, silences every fire-controlled gun and cools the ship at `WGShipShieldEffects.VentCooling` (6) times its plain rate, but never faster than a full load in `FullVentSeconds` (12) times its `SizeScale` (`WGShipShieldEffects.VentRate`), so a capital vents longer than a frigate. It cannot be called off before the heat is gone, and lasts at least `MinVentSeconds` (3) however little heat it held. A venting hull adds `ventSignature` to its thermal signature on top of its heat's, plays a hiss across the hull and throws plumes of heat (`WGEffectShipHeatVent`) off its edge tiles. It also carries `WGShipVentingComponent`, which every navigation, gunnery and cockpit radar that detects the ship draws as pulsing orange rings round the hull with a **VENTING** caption (`DrawWolfgateVents`). The panels show the hard part of the gauge brighter, **VENTING · GUNS OFFLINE** with the seconds left, and the cockpit lamp lights amber. An NPC flying a ship (`ShipSteererComponent` or `ShipTargetingComponent` aboard, no player at the helm) vents by `WantsWolfgateNpcVent`: with nothing to fight at 15% heat; in a fight with the field up at 50% hard heat once 6 seconds pass without a hit; with the field already down at 15% heat after 4 seconds. Under a raised field its guns hold fire from 70% heat (`NpcHoldFireHeat`, `WantsWolfgateNpcHoldFire`), so its own fire leaves the field room for hits; once hard heat alone is past that mark, which holding fire can't cure, it vents without waiting for quiet. The first time it is asked after an overload ends it vents if the ship still holds 50% (`NpcOverloadVentHeat`), hit or not, instead of fighting on under a field that is nearly full. A hull with no generator vents by the same rule; its helm tab, gunnery column and cockpit panel read **HEAT** and keep only the status line, the gauge and **Vent heat** (`WGShipShieldShuntState.Hull`). Guns heat every ship, shield or none, field up or down: each shot of a fire-controlled gun adds the hull's `fireHeat` (25%) of its damage as soft heat (`OnWolfgateShipGunShot`). A gun with `WGShipGunHeatComponent` sets its own: a fixed `heatPerShot`, 3 on the Phalanx, which fires fast at missiles, 140 on the AK820 and AK800, 1500 on the Hades, whose damage share would fill a corvette in seconds, and 62.5 on the ASF Flutter so it does not inherit the Phalanx's; or a `damageShare` in place of the hull's, 15% on the RAC-30 and AC-35 rapid-fire cannons. A fixed heat wins over a share; for that a shell's blast counts at its peak tile (`maxIntensity`, `WGShipShieldEffects.BlastDamage`), not summed over its spread, so big HE shells don't cook the ship that fires them. A field that absorbs or deflects a shell is charged its whole blast, every tile it would reach, as upstream does (`WGShipShieldEffects.AbsorbedBlastDamage`), so a missile costs the shield what it would have cost the hull. Missiles and rockets are the exception: a projectile carrying `WGShipMissileComponent` (everything under `ShipMissileBase`, the ASM-19 and the Mk 248 rocket) adds no heat when fired; it still heats any shield it hits. This heat stops at 95% of capacity, where every fire-controlled gun holds fire until the ship cools below 85%, so a ship's own fire never collapses its field but leaves it one hit from collapse; lowering the field does not free the guns, cooling or a vent does. A collapse leaves the ship at full heat unless the generator sets `collapseHeat`, so once the gun lock runs out with the overload the hold keeps its guns silent until it cools below 85% or vents. A Parasol's ship heats like any other. Every hull holds `baseCapacity` (8000) plus `capacityPerTile` (15) for each of its first tiles, each further tile adding less as it tapers toward `maxCapacity` (30000) (`WGShipShieldEffects.HullCapacity`, `SizeWolfgateHull`, measured again when the hull gains or loses tiles), plus a built-in sink of `sinkCapacity` (20000) that a ship generator takes the place of: the generator adds its own capacity instead, 30000 for the MS-100 and the Parasol, 40000 for the MS-500, 50000 for the MS-250 and the LM-212, and its own cooling instead of the sink's. A shielded ship's totals do not depend on the sink; it only gives a bare hull a pool and cooling near a small generator's, so a ship without a shield can fight on its guns without watching its heat. A hull's own plain rate sheds a full load in `coolSeconds` (80), the sink included; a bare hull has no field, so it always cools at the field-down rate, a full load in 40 seconds, faster for its size than any generator cools its own capacity. An installed generator's cooling applies even while its field is down; an NPC crew on one fights on until its guns hold and vents then. Only a hull ruled by an outpost generator is left out: its guns add no heat and never hold. `AddWolfgateShipHeat` and `GetWolfgateShipHeat` are the way in for anything else that heats a ship. This heat is separate from the thermal signature sensors read. The shield panels show heat on a captioned gauge (`HeatGauge`): the percentage, the hard share in a brighter tone with its own reading, quarter marks, a red line where the guns hold fire and, on the helm and generator panels, a key for the two tones. They show **GUNS HOLDING** while the hold applies, and the cockpit lamp lights amber for it.

Generator specifications are available when examining an emitter and through **Generator stats** in the helm or generator Shields panel. Values come from the actual emitter: usable capacity (limited by both overload and power demand), overload threshold, cooling rates plain and venting, idle and maximum power demand, and configured overload lockout. The panel follows the active field owner, or the selected recovery emitter while offline; it does not add the capacities of multiple installed generators. The live recovery countdown remains the estimate for the current situation.

For tuning, `WGShipDuelTest` (explicit; run it with `test.ps1 -Where "class == 'Content.IntegrationTests.Tests._WG.ShipShields.WGShipDuelTest'"`) sets two real hulls nose to nose, fires every gun they carry with their own ammo, follows the NPC vent rule and writes each ship's heat, collapses, vents and gun locks to `test_results/integration/ship_duels`.

The MD-10 Parasol (`WGShieldGeneratorDeflector`, sold as a flatpack by the FlatpackVend, the FlatpackVend Premium and the systems broker) is a deflector: a white field with the MS-100's capacity on a fifth of its power. `WGShipShieldDeflectorComponent` on the generator makes the field slow solid rounds instead of stopping them (`ShipShieldsSystem.Deflector.cs`). A round loses `reduction` (70% on the Parasol) of its projectile damage and explosive intensity at the perimeter, the emitter is charged for what was removed, and the round carries on to the hull with its shooter and warhead intact; it is slowed once per field. Projectiles on the component's `stopped` list, and everything inheriting from them (missiles, rockets, energy bolts), are absorbed as by any shield, as are hitscan beams and hull collisions. A new upstream energy or missile projectile needs adding to that list, or it passes at the deflector's reduced damage. The component's `maxArc` (180 degrees on the Parasol) caps the field's width: while such an emitter holds the field, requests are clamped to that arc at full concentration, so nothing outside the arc is protected, and a ship whose allocation was never set starts with the whole arc facing forward. Narrower arcs and any bearing remain available.

Generator startup and shutdown use the supplied `shield_on` and `shield_off` recordings, converted to mono Ogg at their original pitch. Startup and shutdown share a five-second per-hull cooldown, including across generator replacement, so rapid toggling cannot stack or queue transition sounds.

The impact bank is generated by `Tools/_WG/ShipShields/synthesize_impacts.py` using NumPy and FFmpeg (`--ffmpeg PATH` when it is not on PATH). It reproduces all three approved previews with mono encoding, 3.4-second tails and safe peak headroom. The sound collection chooses a design randomly, with a small additional pitch variation during playback.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ShipShields/MoverController.ShipShields.cs`](MoverController.ShipShields.cs)
- [`Content.Server/_WG/ShipShields/ProjectileGrenadeSystem.ShipShields.cs`](ProjectileGrenadeSystem.ShipShields.cs)
- [`Content.Server/_WG/ShipShields/ProjectileSystem.ShipShields.cs`](ProjectileSystem.ShipShields.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Attacks.cs`](ShipShieldsSystem.Attacks.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Audio.cs`](ShipShieldsSystem.Audio.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Collisions.cs`](ShipShieldsSystem.Collisions.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Costs.cs`](ShipShieldsSystem.Costs.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Deflector.cs`](ShipShieldsSystem.Deflector.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Ftl.cs`](ShipShieldsSystem.Ftl.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Heat.cs`](ShipShieldsSystem.Heat.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Hitscan.cs`](ShipShieldsSystem.Hitscan.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Lifecycle.cs`](ShipShieldsSystem.Lifecycle.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Pvs.cs`](ShipShieldsSystem.Pvs.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Radar.cs`](ShipShieldsSystem.Radar.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Recovery.cs`](ShipShieldsSystem.Recovery.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Shunting.cs`](ShipShieldsSystem.Shunting.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Stats.cs`](ShipShieldsSystem.Stats.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Transitions.cs`](ShipShieldsSystem.Transitions.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.TriggerCollisions.cs`](ShipShieldsSystem.TriggerCollisions.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Vent.cs`](ShipShieldsSystem.Vent.cs)
- [`Content.Server/_WG/ShipShields/ShipShieldsSystem.Wolfgate.cs`](ShipShieldsSystem.Wolfgate.cs)
- [`Content.Server/_WG/ShipShields/ShuttleConsoleSystem.Shunting.cs`](ShuttleConsoleSystem.Shunting.cs)
- [`Content.Server/_WG/ShipShields/ShuttleSystem.Collisions.cs`](ShuttleSystem.Collisions.cs)
- [`Content.Server/_WG/ShipShields/ShuttleSystem.FtlShields.cs`](ShuttleSystem.FtlShields.cs)
- [`Content.Server/_WG/ShipShields/TriggerSystem.ShipShields.cs`](TriggerSystem.ShipShields.cs)
- [`Content.Server/_WG/ShipShields/WGShipGunHeatComponent.cs`](WGShipGunHeatComponent.cs)
- [`Content.Server/_WG/ShipShields/WGShipHeatComponent.cs`](WGShipHeatComponent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldAttackedEvent.cs`](WGShipShieldAttackedEvent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldCostComponent.cs`](WGShipShieldCostComponent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldDeflectedComponent.cs`](WGShipShieldDeflectedComponent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldHitscanDamageSystem.cs`](WGShipShieldHitscanDamageSystem.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldImpactAudioComponent.cs`](WGShipShieldImpactAudioComponent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldLimitSystem.cs`](WGShipShieldLimitSystem.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldProjectileRayHitEvent.cs`](WGShipShieldProjectileRayHitEvent.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldShuntSystem.cs`](WGShipShieldShuntSystem.cs)
- [`Content.Server/_WG/ShipShields/WGShipShieldShuntSystem.Toggle.cs`](WGShipShieldShuntSystem.Toggle.cs)

### Shared

- [`Content.Shared/_WG/ShipShields/FixtureDamageDealerSystem.ShipShields.cs`](../../../Content.Shared/_WG/ShipShields/FixtureDamageDealerSystem.ShipShields.cs)
- [`Content.Shared/_WG/ShipShields/WGShipMissileComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipMissileComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipOverloadedComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipOverloadedComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldDeflectorComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldDeflectorComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldEffects.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldEffects.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldGeneratorStats.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldGeneratorStats.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldGeometry.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldGeometry.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldHardenedComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldHardenedComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldHelmAngles.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldHelmAngles.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldHitscanEvents.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldHitscanEvents.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldHitscanMath.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldHitscanMath.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldHitscanSystem.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldHitscanSystem.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldInterceptAttemptEvent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldInterceptAttemptEvent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldKeys.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldKeys.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldMesh.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldMesh.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldRadarComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldRadarComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldRadarGeometry.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldRadarGeometry.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldShuntComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldShuntComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldShuntMath.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldShuntMath.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldShuntMessages.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldShuntMessages.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldStrengthControlComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldStrengthControlComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldTransitionEffects.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldTransitionEffects.cs)
- [`Content.Shared/_WG/ShipShields/WGShipShieldVisualsComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipShieldVisualsComponent.cs)
- [`Content.Shared/_WG/ShipShields/WGShipVentingComponent.cs`](../../../Content.Shared/_WG/ShipShields/WGShipVentingComponent.cs)

### Client

- [`Content.Client/_WG/ShipShields/FireControlWindow.ShipShields.cs`](../../../Content.Client/_WG/ShipShields/FireControlWindow.ShipShields.cs)
- [`Content.Client/_WG/ShipShields/ShuttleConsoleBoundUserInterface.ShipShields.cs`](../../../Content.Client/_WG/ShipShields/ShuttleConsoleBoundUserInterface.ShipShields.cs)
- [`Content.Client/_WG/ShipShields/ShuttleConsoleWindow.ShipShields.cs`](../../../Content.Client/_WG/ShipShields/ShuttleConsoleWindow.ShipShields.cs)
- [`Content.Client/_WG/ShipShields/ShuttleNavControl.ShipShields.cs`](../../../Content.Client/_WG/ShipShields/ShuttleNavControl.ShipShields.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldGeneratorBoundUserInterface.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldGeneratorBoundUserInterface.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldGeneratorWindow.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldGeneratorWindow.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldKeysSystem.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldKeysSystem.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldOverlay.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldOverlay.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldOverlaySystem.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldOverlaySystem.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldShuntScreen.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldShuntScreen.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldShuntScreen.Gunnery.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldShuntScreen.Gunnery.cs)
- [`Content.Client/_WG/ShipShields/WGShipShieldStatsWindow.cs`](../../../Content.Client/_WG/ShipShields/WGShipShieldStatsWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipDuelTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipDuelTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipHeatTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipHeatTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipRadarMarkTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipRadarMarkTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldAttackTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldAttackTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldBiodomeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldBiodomeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCentreTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCentreTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCollisionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCollisionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldControlsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldControlsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCostTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldCostTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldDeflectorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldDeflectorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldFixturesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldFixturesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldFtlTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldFtlTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGeneratorControlTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGeneratorControlTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGeneratorStatsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGeneratorStatsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGunneryControlTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldGunneryControlTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldHitscanTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldHitscanTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldHullDamageTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldHullDamageTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLifecycleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLifecycleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLimitTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLimitTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLiveShuntTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldLiveShuntTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldMovingProjectileTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldMovingProjectileTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldPinholeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldPinholeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldProjectileContactTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldProjectileContactTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldRecoveryTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldRecoveryTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldShuntRegenTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldShuntRegenTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldStatsLayoutTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldStatsLayoutTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldToggleKeyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldToggleKeyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldTransitionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipShields/WGShipShieldTransitionTest.cs)

### Unit tests

- [`Content.Tests/_WG/ShipShields/WGShipShieldEffectsTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldEffectsTest.cs)
- [`Content.Tests/_WG/ShipShields/WGShipShieldGeometryTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldGeometryTest.cs)
- [`Content.Tests/_WG/ShipShields/WGShipShieldHitscanMathTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldHitscanMathTest.cs)
- [`Content.Tests/_WG/ShipShields/WGShipShieldMeshTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldMeshTest.cs)
- [`Content.Tests/_WG/ShipShields/WGShipShieldRadarGeometryTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldRadarGeometryTest.cs)
- [`Content.Tests/_WG/ShipShields/WGShipShieldShuntMathTest.cs`](../../../Content.Tests/_WG/ShipShields/WGShipShieldShuntMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/ShipShields/deflector.yml`](../../../Resources/Prototypes/_WG/ShipShields/deflector.yml)
- [`Resources/Prototypes/_WG/ShipShields/effects.yml`](../../../Resources/Prototypes/_WG/ShipShields/effects.yml)
- [`Resources/Prototypes/_WG/ShipShields/Shaders/shaders.yml`](../../../Resources/Prototypes/_WG/ShipShields/Shaders/shaders.yml)
- [`Resources/Prototypes/_WG/ShipShields/sounds.yml`](../../../Resources/Prototypes/_WG/ShipShields/sounds.yml)

### Localization

- [`Resources/Locale/en-US/_WG/ShipShields/clientstats.ftl`](../../../Resources/Locale/en-US/_WG/ShipShields/clientstats.ftl)
- [`Resources/Locale/en-US/_WG/ShipShields/shunting.ftl`](../../../Resources/Locale/en-US/_WG/ShipShields/shunting.ftl)
- [`Resources/Locale/en-US/_WG/ShipShields/stats.ftl`](../../../Resources/Locale/en-US/_WG/ShipShields/stats.ftl)

### Textures

- [`Resources/Textures/_WG/ShipShields/gun_lock.rsi/`](../../../Resources/Textures/_WG/ShipShields/gun_lock.rsi/)
- [`Resources/Textures/_WG/ShipShields/heat_vent.rsi/`](../../../Resources/Textures/_WG/ShipShields/heat_vent.rsi/)
- [`Resources/Textures/_WG/ShipShields/Shaders/shield_shimmer.swsl`](../../../Resources/Textures/_WG/ShipShields/Shaders/shield_shimmer.swsl)

### Audio

- [`Resources/Audio/_WG/ShipShields/attributions.yml`](../../../Resources/Audio/_WG/ShipShields/attributions.yml)
- [`Resources/Audio/_WG/ShipShields/impact_1.ogg`](../../../Resources/Audio/_WG/ShipShields/impact_1.ogg)
- [`Resources/Audio/_WG/ShipShields/impact_2.ogg`](../../../Resources/Audio/_WG/ShipShields/impact_2.ogg)
- [`Resources/Audio/_WG/ShipShields/impact_3.ogg`](../../../Resources/Audio/_WG/ShipShields/impact_3.ogg)
- [`Resources/Audio/_WG/ShipShields/shield_off.ogg`](../../../Resources/Audio/_WG/ShipShields/shield_off.ogg)
- [`Resources/Audio/_WG/ShipShields/shield_on.ogg`](../../../Resources/Audio/_WG/ShipShields/shield_on.ogg)
- [`Resources/Audio/_WG/ShipShields/vent.ogg`](../../../Resources/Audio/_WG/ShipShields/vent.ogg)

### Tools

- [`Tools/_WG/ShipShields/synthesize_impacts.py`](../../../Tools/_WG/ShipShields/synthesize_impacts.py)
- [`Tools/_WG/ShipShields/synthesize_vent.py`](../../../Tools/_WG/ShipShields/synthesize_vent.py)

## Non-modular edits

- [`Content.Client/_Crescent/ShipShields/ShipShieldOverlay.cs`](../../../Content.Client/_Crescent/ShipShields/ShipShieldOverlay.cs): Hull shields use their own contour overlay.
- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): shield state for the gunner's shield control
- [`Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs`](../../../Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs)
  - shield hotkey
  - vent hotkey
- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs): route shield allocation requests through the helm.
- [`Content.Client/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../../Content.Client/Shuttles/Systems/ShuttleConsoleSystem.cs)
  - the shield hotkey works while piloting
  - the vent hotkey works while piloting
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs)
  - add the shield allocation tab.
  - hide shield allocation outside its helm tab.
  - expose allocation controls at the helm.
  - shield allocation mode.
  - share authoritative allocation across helms.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): hull contours and directional coverage on all radar views.
- [`Content.Server/_Crescent/ShipShields/ShipShieldsSystem.cs`](../../_Crescent/ShipShields/ShipShieldsSystem.cs)
  - refresh hull geometry and shield health
  - recover stale handles and keep one owner per hull
  - a ship generator at its power cap overloads like one past its limit
  - heat is the hull's, and a generator cools it by the hull's rules
  - complete recharge when healing lands exactly on zero damage
  - a powered ship generator waits out its overload, not zero heat
  - a ship generator's overload wait grows with its ship's capacity
  - respect the ship's manual field switch
  - record actual field transitions for diagnosis
  - only announce successful startup and rate-limit power transitions per hull
  - a standby emitter cannot remove another generator's field
  - a field beaten down by damage locks the ship's guns
  - share the startup cooldown and use shutdown audio parameters
  - announce only actual shutdown
  - track hull tile changes
  - map-parented shield still phases its ship's outgoing shots
  - unpowered sectors let shots pass without changing their shooter
  - apply damage only after a projectile contacts the perimeter
  - remove only the field owned by this emitter
  - block normal and administrative shield deployment throughout FTL
  - discard stale grid fields and reserve active fields for their owner
  - replace oval and interior blocker with the padded hull perimeter
  - dissipate visually after protection stops immediately
- [`Content.Server/_Crescent/ShipShields/ShipShieldsSystem.Emitter.cs`](../../_Crescent/ShipShields/ShipShieldsSystem.Emitter.cs)
  - removing a standby generator leaves the active field intact
  - preserve the EMP while absorbing explosive and spawn payloads
  - show runtime specifications for generator comparison.
- [`Content.Server/Explosion/EntitySystems/ProjectileGrenadeSystem.cs`](../../Explosion/EntitySystems/ProjectileGrenadeSystem.cs): preserve outgoing fragments and mines through their own shields.
- [`Content.Server/Explosion/EntitySystems/TriggerSystem.cs`](../../Explosion/EntitySystems/TriggerSystem.cs): absorb collision-triggered ship warheads before they can explode on the shield
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs)
  - a raised shield field slows the turn
  - a raised shield field cuts linear thrust
  - a raised shield field slows the turn brake too
- [`Content.Server/Projectiles/ProjectileSystem.cs`](../../Projectiles/ProjectileSystem.cs): a deflector field slows a round and lets it fly on unspent
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - include directional shield settings in the helm state
  - refresh open helm shield status on visible changes
- [`Content.Server/Shuttles/Systems/ShuttleSystem.FasterThanLight.cs`](../../Shuttles/Systems/ShuttleSystem.FasterThanLight.cs): drop departing shield fields immediately after successful spoolup.
- [`Content.Server/Shuttles/Systems/ShuttleSystem.Impact.cs`](../../Shuttles/Systems/ShuttleSystem.Impact.cs): debit each hull shield for absorbed collision damage
- [`Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs`](../../../Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs): hull shields are thin edge fixtures, so test the area inside the outline
- [`Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanDiffractSystem.cs`](../../../Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanDiffractSystem.cs): an intervening shield stops beams before they can split behind it
- [`Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanMultiRaycastSystem.cs`](../../../Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanMultiRaycastSystem.cs)
  - shield fixtures are resolved against the visible perimeter
  - log piercing targets after shield interception
  - stop piercing beams while retaining targets before the perimeter
- [`Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs`](../../../Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs): expose the ship's authoritative shield allocation at every helm.
- [`Content.Shared/Weapons/Hitscan/Systems/HitscanBasicRaycastSystem.cs`](../../../Content.Shared/Weapons/Hitscan/Systems/HitscanBasicRaycastSystem.cs)
  - resolve shield crossings against their visible perimeter
  - log only the target remaining after shield interception
  - intercept beams before their visuals and damage
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml)
  - shield and vent hotkeys while piloting
  - Descend moves from F to G, since F raises the shields at the helm
- [`Resources/Prototypes/_Mono/Catalogs/VendingMachines/Inventories/flatpackvendpremium.yml`](../../../Resources/Prototypes/_Mono/Catalogs/VendingMachines/Inventories/flatpackvendpremium.yml): the Parasol deflector is an off-the-shelf fitting
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Energy/launcher.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Energy/launcher.yml): point defence fires fast at missiles, so each beam barely heats the ship
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/projectiles.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/projectiles.yml)
  - intercept artillery payloads at the shield perimeter
  - let deployed mines contact shield fixtures
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardlarge_kinetic.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardlarge_kinetic.yml)
  - a fixed heat per round, so four of them don't outrun a corvette's cooling in seconds
  - about half its damage share; mostly Structural, it would fill a ship's heat in two salvos
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardlight_kinetic.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardlight_kinetic.yml): a rapid-fire cannon heats at a lower share of its damage than the hull's rate
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardmedium_kinetic.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/standardmedium_kinetic.yml): a rapid-fire cannon heats at a lower share of its damage than the hull's rate
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Missile/mpssm.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Missile/mpssm.yml)
  - firing a missile adds no heat to the ship
  - firing a rocket adds no heat to the ship
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Missile/projectiles.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Missile/projectiles.yml): firing a missile adds no heat to the ship
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/shield_generator.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/shield_generator.yml)
  - expose shared ship shield controls directly at every generator
  - use dedicated generator startup and shutdown sounds at their original pitch
  - a raised field costs thrust, turning and stealth, and locks the guns when beaten down
  - the hull's own heat capacity adds to the generator's (Mono: 85000)
  - a ship-sized wait, and the field returns with its heat left (Mono: 35)
  - the hull's own heat capacity adds to the generator's (Mono: 75000)
  - the Titan is back from an overload soonest (Mono: 40)
  - the hull's own heat capacity adds to the generator's 50000, which the power cap sets (was 85000 at 0.5)
  - a ship-sized wait, and the field returns with its heat left (Mono: 100)
  - the hull's own heat capacity adds to the generator's (Mono: 65000)
  - a ship-sized wait, and the field returns with its heat left (Mono: 80)
- [`Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/flatpackvend.yml`](../../../Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/flatpackvend.yml): the Parasol deflector is an off-the-shelf fitting

<!-- WOLFGATE-GENERATED END -->
