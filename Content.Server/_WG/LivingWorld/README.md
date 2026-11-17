# LivingWorld

Planet flora that does something. Two components make any plant prototype interactive, and the per-planet content
builds its plants from them:

- `WGReactiveFlora` makes a stationary plant react to living mobs within `range`: it lashes, grabs, spits, puffs a
  cloud, swells and bursts, irradiates or injects. It looks only when its timer runs out (`checkInterval` while idle,
  `cooldown` after reacting), so thousands of plants cost little. `ignoreFactions` spares a faction;
  `healInsteadFactions` tends one with `healDamage`/`healSolution` instead.
- `WGRegrowingHarvest` lets a plant be picked by hand (or with a `tool` quality) for a loot table, the first piece
  into the picker's hand. It stays standing, shows bare, and regrows after `regrowTime`.
- Wild produce bases in `Resources/Prototypes/_WG/LivingWorld/produce.yml`: `WGWildMushroomBase` (two
  `WGWildMushroom` and water in a big bowl cook into mushroom soup), `WGWildBerryBase`, `WGHealingHerbBase` (eat it
  for bicaridine and kelotane, or cut it into a `WGHerbPoultice`) and `WGPlantFibreBase` (cut into cotton).
- Carcinoma's `WGCarcinomaPimple` grows sparsely on the surface. When anyone outside the Chimera and aberrant flesh
  factions comes within 2 m it swells for 1.5 s, then bursts for 8 blunt and 7 slash within 1.5 m, leaving 20 u of
  blood and a 4 s tear gas spore cloud. Any hit bursts it at once, so shoot it from afar.
- Carcinoma's regions have their own life (Resources/Prototypes/_WG/Planets/Life/carcinoma.yml): grasping tendon vines hold a
  walker for 2 s in the tendon forest, weeping polyps on the scab shores mist blood and a thin toxin, vitae blisters in the
  marsh give a bead of vitae to a blade, bone piles give bone shards by hand, and meat-fruit bushes in the groves heal and
  seed a little mould. Bone burrowers, hive spiders, scab slurvas and marsh newborns join the usual flesh wildlife.
- Black Mesa sounds (`Tools/_WG/LivingWorld/import_bms.py`): plant sounds as `WGFlora*` sound collections in
  `sounds.yml`, and Xen and Black Mesa beds and accents added to every world's ambience in
  `Resources/Prototypes/_WG/Planets/planet_ambience.yml`.

A held mob sees the "Held fast" alert: clicking it struggles, taking `struggleStep` off the hold, and enough damage to
the plant (`cutFreeDamage`) cuts it free.

## Making a plant

Make a child of an existing flora prototype (the Decor Import ones the biomes place) and add the components:

```yaml
# Lashes whoever walks past it, and its pods can be picked.
- type: entity
  parent: GoonShrub
  id: WGFloraWhipvine
  name: whipvine
  components:
  - type: Appearance
  - type: WGReactiveFlora
    behaviour: Lash            # Lash, Grab, Spit, Cloud, Explode, Irradiate, Inject
    range: 1.5
    cooldown: 4
    sound: { collection: WGFloraLash }
    popup: wg-flora-whipvine-lash   # your own string; $plant is the plant
    damage: { types: { Slash: 6 } }
    knockback: 1.5
    ignoreFactions: [Xeno]
  - type: WGRegrowingHarvest
    regrowTime: 600
    harvestSound: { collection: WGFloraVineSnap }
    table: !type:AllSelector
      children:
      - id: WGPlantFibre
        amount: !type:RangeNumberSelector
          range: 1, 2
```

Per behaviour:

- `Lash`: `damage`, `knockback` (m) and `knockbackSpeed`, `stunTime`, `knockdownTime`; `maxTargets: 0` hits everyone.
- `Grab`: `holdTime`, `heldSpeed` (0 holds still), `damage` on the grab, `cutFreeDamage`, `struggleStep`,
  `releaseSound`.
- `Spit`: `projectile` (any projectile prototype) and `projectileSpeed`.
- `Cloud`: `solution`, `cloudPrototype` (`Smoke` or a foam), `cloudDuration`, `cloudSpread`. Tear gas blinds,
  space drugs or mindbreaker make you see things, toxin or mold poisons.
- `Explode`: `armDelay`, `armSound`, `armPopup`, then `sound`, `popup`, `burstRadius`, `burstDamage`, `burstSpill`,
  `burstSpawns`, a `solution` cloud, an optional `explosionType` (it never breaks tiles), and `deleteOnBurst` or
  `regrowTime`. `burstOnDamage` pops it when hit.
- `Irradiate`: `radsPerSecond` on every mob in range (`maxTargets: 0`), each `cooldown`.
- `Inject`: `solution` into the bloodstream, e.g. omnizine to heal or toxin to hurt.

The sprite can follow the plant: appearance key `enum.WGReactiveFloraVisuals.State` with values `Idle`, `Armed`
(swelling, holding, or just after a reaction for `activeTime`) and `Spent`, and `enum.WGRegrowingHarvestVisuals.Harvested`
(`True`/`False`). Use a `GenericVisualizer`, as `WGCarcinomaPimple` does.

To place it, add a `BiomeEntityLayer` to the planet's biome as `WGCarcinomaPimple` does in
`Resources/Prototypes/_WG/Planets/carcinoma.yml`, and add solid or dangerous ones to the world's cavern `avoid` list in
`Resources/Prototypes/_WG/Caverns/caverns.yml`. A planet's biome test may list the features it allows.

Entry points: `WGReactiveFloraSystem`, `WGRegrowingHarvestSystem`.

## Merak

Merak's regions each have their own wildlife and plants (`Resources/Prototypes/_WG/Planets/Life/merak.yml`; the sand
maws stay in `_WG/SandMaw`). Dunes: desert tarantulas (a sand-coloured space spider, slower and harder-hitting) and
tumbleweeds that rustle now and then. Badlands: lizards, cobras and spitting cacti that flick slow needles from 6 m.
Cactus fields: kangaroos, sting-nettles among the grass (a sting and a 1 s stun), barrel cacti picked by hand for
cactus water and agave cut for sugary sap. Gravel flats: ore crabs round the ore outcrops. Oases: goats and crabs, date
palms with pickable dates and, very rarely, a mimic posing as a crate.

## Sounds

The Black Mesa files come from the owner's copy of the mod's sounds, which the owner states are open source; the
attributions say `license: "Custom"` until the exact licence is confirmed. Rerun the script to re-import, and keep the
set to what a collection or ambience uses (`LivingWorldAudioTest` fails on an unused file or more than 60).

| Folder | Files | Collections |
|---|---|---|
| `lash` | tentacle strikes and swooshes | `WGFloraLash`, `WGFloraSwoosh` |
| `grab` | barnacle tongue pulls, gulp, bark | `WGFloraGrab`, `WGFloraGulp` |
| `spit` | bullsquid goo attacks, splat | `WGFloraSpit`, `WGFloraSplat` |
| `pods` | barrel cactus flinches and bursts, mold cyst, Xen pod breaks | `WGFloraPodSwell`, `WGFloraPodBurst`, `WGFloraSporePuff`, `WGFloraPodIdle` |
| `heal` | healing pool | `WGFloraHeal`, `WGFloraHealHum` |
| `light` | light plant extend and retract | `WGFloraLightExtend`, `WGFloraLightRetract` |
| `critters` | protozoan chirps, lily pad | `WGFloraChirp`, `WGFloraLilypad` |
| `hackertree` | attack, death, retract | `WGFloraTreeAttack`, `WGFloraTreeDie`, `WGFloraTreeRetract` |
| `vines` | vine and web breaks | `WGFloraVineSnap`, `WGFloraVineTear` |
| `crystal` | radiation crystal hum and charge | `WGFloraCrystalHum`, `WGFloraCrystalCharge` |
| `gore` | gore splats, gibbing | `WGFloraGore`, `WGFloraWetSquelch` |
| `ambience` | stereo, -20 LUFS | see below |

Ambience: Aerumna adds the Xen outdoors bed and far gargantua, boid and tower manta calls; Carcinoma the Xen swamp bed by
day; Merak desert wind after each bed; Thrascias the howling Nihilanth wind bed and a wind gust; Fervidus a fire bed and
a deep rumble; Asclepiu hawks and eagles by day.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/LivingWorld/WGFloraGrabbedComponent.cs`](WGFloraGrabbedComponent.cs)
- [`Content.Server/_WG/LivingWorld/WGReactiveFloraSystem.cs`](WGReactiveFloraSystem.cs)
- [`Content.Server/_WG/LivingWorld/WGRegrowingHarvestSystem.cs`](WGRegrowingHarvestSystem.cs)

### Shared

- [`Content.Shared/_WG/LivingWorld/WGReactiveFloraComponent.cs`](../../../Content.Shared/_WG/LivingWorld/WGReactiveFloraComponent.cs)
- [`Content.Shared/_WG/LivingWorld/WGRegrowingHarvestComponent.cs`](../../../Content.Shared/_WG/LivingWorld/WGRegrowingHarvestComponent.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/LivingWorld/CarcinomaPimpleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/LivingWorld/CarcinomaPimpleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/LivingWorld/LivingWorldAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WG/LivingWorld/LivingWorldAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WG/LivingWorld/ReactiveFloraTest.cs`](../../../Content.IntegrationTests/Tests/_WG/LivingWorld/ReactiveFloraTest.cs)
- [`Content.IntegrationTests/Tests/_WG/LivingWorld/RegrowingHarvestTest.cs`](../../../Content.IntegrationTests/Tests/_WG/LivingWorld/RegrowingHarvestTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/LivingWorld/alerts.yml`](../../../Resources/Prototypes/_WG/LivingWorld/alerts.yml)
- [`Resources/Prototypes/_WG/LivingWorld/carcinoma_pimple.yml`](../../../Resources/Prototypes/_WG/LivingWorld/carcinoma_pimple.yml)
- [`Resources/Prototypes/_WG/LivingWorld/produce.yml`](../../../Resources/Prototypes/_WG/LivingWorld/produce.yml)
- [`Resources/Prototypes/_WG/LivingWorld/recipes.yml`](../../../Resources/Prototypes/_WG/LivingWorld/recipes.yml)
- [`Resources/Prototypes/_WG/LivingWorld/sounds.yml`](../../../Resources/Prototypes/_WG/LivingWorld/sounds.yml)

### Localization

- [`Resources/Locale/en-US/_WG/LivingWorld/living_world.ftl`](../../../Resources/Locale/en-US/_WG/LivingWorld/living_world.ftl)

### Audio

- [`Resources/Audio/_WG/LivingWorld/ambience/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/ambience/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/ambience/boid_xen_medium01.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/boid_xen_medium01.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/eagle1.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/eagle1.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/garg_xen_far01.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/garg_xen_far01.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/hawk1.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/hawk1.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/o_ambience_disaster_fire1.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/o_ambience_disaster_fire1.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/o_ambience_disaster_rumble1.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/o_ambience_disaster_rumble1.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/towermantas_far01.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/towermantas_far01.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/wind1.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/wind1.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/wind_fall.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/wind_fall.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/xen_nih_wind1_mix.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/xen_nih_wind1_mix.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/xen_outdoor1_mix.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/xen_outdoor1_mix.ogg)
- [`Resources/Audio/_WG/LivingWorld/ambience/xen_swamp1_mix.ogg`](../../../Resources/Audio/_WG/LivingWorld/ambience/xen_swamp1_mix.ogg)
- [`Resources/Audio/_WG/LivingWorld/critters/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/critters/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/critters/chirp03.ogg`](../../../Resources/Audio/_WG/LivingWorld/critters/chirp03.ogg)
- [`Resources/Audio/_WG/LivingWorld/critters/chirp05.ogg`](../../../Resources/Audio/_WG/LivingWorld/critters/chirp05.ogg)
- [`Resources/Audio/_WG/LivingWorld/critters/lilypad_landing_soft01.ogg`](../../../Resources/Audio/_WG/LivingWorld/critters/lilypad_landing_soft01.ogg)
- [`Resources/Audio/_WG/LivingWorld/critters/lilypad_sinking01.ogg`](../../../Resources/Audio/_WG/LivingWorld/critters/lilypad_sinking01.ogg)
- [`Resources/Audio/_WG/LivingWorld/crystal/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/crystal/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/crystal/xen_crystal_radiation_charging01.ogg`](../../../Resources/Audio/_WG/LivingWorld/crystal/xen_crystal_radiation_charging01.ogg)
- [`Resources/Audio/_WG/LivingWorld/crystal/xen_crystal_radiation_hum01.ogg`](../../../Resources/Audio/_WG/LivingWorld/crystal/xen_crystal_radiation_hum01.ogg)
- [`Resources/Audio/_WG/LivingWorld/gore/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/gore/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/gore/gibbing1.ogg`](../../../Resources/Audio/_WG/LivingWorld/gore/gibbing1.ogg)
- [`Resources/Audio/_WG/LivingWorld/gore/gore1.ogg`](../../../Resources/Audio/_WG/LivingWorld/gore/gore1.ogg)
- [`Resources/Audio/_WG/LivingWorld/gore/gore_splat1.ogg`](../../../Resources/Audio/_WG/LivingWorld/gore/gore_splat1.ogg)
- [`Resources/Audio/_WG/LivingWorld/gore/gore_splat2.ogg`](../../../Resources/Audio/_WG/LivingWorld/gore/gore_splat2.ogg)
- [`Resources/Audio/_WG/LivingWorld/grab/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/grab/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/grab/barnacle_bark1.ogg`](../../../Resources/Audio/_WG/LivingWorld/grab/barnacle_bark1.ogg)
- [`Resources/Audio/_WG/LivingWorld/grab/barnacle_gulp1.ogg`](../../../Resources/Audio/_WG/LivingWorld/grab/barnacle_gulp1.ogg)
- [`Resources/Audio/_WG/LivingWorld/grab/barnacle_pull1.ogg`](../../../Resources/Audio/_WG/LivingWorld/grab/barnacle_pull1.ogg)
- [`Resources/Audio/_WG/LivingWorld/grab/barnacle_pull2.ogg`](../../../Resources/Audio/_WG/LivingWorld/grab/barnacle_pull2.ogg)
- [`Resources/Audio/_WG/LivingWorld/grab/barnacle_tongue_pull1.ogg`](../../../Resources/Audio/_WG/LivingWorld/grab/barnacle_tongue_pull1.ogg)
- [`Resources/Audio/_WG/LivingWorld/hackertree/attack1.ogg`](../../../Resources/Audio/_WG/LivingWorld/hackertree/attack1.ogg)
- [`Resources/Audio/_WG/LivingWorld/hackertree/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/hackertree/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/hackertree/die1.ogg`](../../../Resources/Audio/_WG/LivingWorld/hackertree/die1.ogg)
- [`Resources/Audio/_WG/LivingWorld/hackertree/retract_to_idle01.ogg`](../../../Resources/Audio/_WG/LivingWorld/hackertree/retract_to_idle01.ogg)
- [`Resources/Audio/_WG/LivingWorld/heal/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/heal/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/heal/healingpool_active01.ogg`](../../../Resources/Audio/_WG/LivingWorld/heal/healingpool_active01.ogg)
- [`Resources/Audio/_WG/LivingWorld/heal/healingpool_hum01.ogg`](../../../Resources/Audio/_WG/LivingWorld/heal/healingpool_hum01.ogg)
- [`Resources/Audio/_WG/LivingWorld/lash/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/lash/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/lash/tent_strike1.ogg`](../../../Resources/Audio/_WG/LivingWorld/lash/tent_strike1.ogg)
- [`Resources/Audio/_WG/LivingWorld/lash/tent_strike2.ogg`](../../../Resources/Audio/_WG/LivingWorld/lash/tent_strike2.ogg)
- [`Resources/Audio/_WG/LivingWorld/lash/tent_swoosh1.ogg`](../../../Resources/Audio/_WG/LivingWorld/lash/tent_swoosh1.ogg)
- [`Resources/Audio/_WG/LivingWorld/lash/tent_swoosh2.ogg`](../../../Resources/Audio/_WG/LivingWorld/lash/tent_swoosh2.ogg)
- [`Resources/Audio/_WG/LivingWorld/lash/tent_swoosh3.ogg`](../../../Resources/Audio/_WG/LivingWorld/lash/tent_swoosh3.ogg)
- [`Resources/Audio/_WG/LivingWorld/light/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/light/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/light/extend01.ogg`](../../../Resources/Audio/_WG/LivingWorld/light/extend01.ogg)
- [`Resources/Audio/_WG/LivingWorld/light/retract01.ogg`](../../../Resources/Audio/_WG/LivingWorld/light/retract01.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/pods/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/pods/explode01.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/explode01.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/explode02.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/explode02.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/flinch01.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/flinch01.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/flinch02.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/flinch02.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/moldcyst_idle.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/moldcyst_idle.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/moldcyst_pop.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/moldcyst_pop.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/xen_podbreak.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/xen_podbreak.ogg)
- [`Resources/Audio/_WG/LivingWorld/pods/xen_podbreak2.ogg`](../../../Resources/Audio/_WG/LivingWorld/pods/xen_podbreak2.ogg)
- [`Resources/Audio/_WG/LivingWorld/spit/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/spit/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/spit/goo_attack1.ogg`](../../../Resources/Audio/_WG/LivingWorld/spit/goo_attack1.ogg)
- [`Resources/Audio/_WG/LivingWorld/spit/goo_attack2.ogg`](../../../Resources/Audio/_WG/LivingWorld/spit/goo_attack2.ogg)
- [`Resources/Audio/_WG/LivingWorld/spit/splat1.ogg`](../../../Resources/Audio/_WG/LivingWorld/spit/splat1.ogg)
- [`Resources/Audio/_WG/LivingWorld/vines/attributions.yml`](../../../Resources/Audio/_WG/LivingWorld/vines/attributions.yml)
- [`Resources/Audio/_WG/LivingWorld/vines/int_vinebreak1.ogg`](../../../Resources/Audio/_WG/LivingWorld/vines/int_vinebreak1.ogg)
- [`Resources/Audio/_WG/LivingWorld/vines/int_vinebreak2.ogg`](../../../Resources/Audio/_WG/LivingWorld/vines/int_vinebreak2.ogg)
- [`Resources/Audio/_WG/LivingWorld/vines/web_break01.ogg`](../../../Resources/Audio/_WG/LivingWorld/vines/web_break01.ogg)
- [`Resources/Audio/_WG/LivingWorld/vines/web_largebreak01.ogg`](../../../Resources/Audio/_WG/LivingWorld/vines/web_largebreak01.ogg)

### Tools

- [`Tools/_WG/LivingWorld/import_bms.py`](../../../Tools/_WG/LivingWorld/import_bms.py)

## Non-modular edits

None.

<!-- WOLFGATE-GENERATED END -->
