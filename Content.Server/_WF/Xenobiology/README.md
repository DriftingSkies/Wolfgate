# Xenobiology

Goobstation's slime xenobiology, ported through Trauma Station. Baby slimes eat food and latch onto creatures to digest
them, grow into adults, and split into one to four babies once well fed; each baby may mutate into another breed, up
through five tiers. Petting a slime tames it. The xenovac sucks slimes into a backpack tank and blows them back out.
Dead slimes dragged onto a slime grinder become extracts, and an extract injected with plasma, water or blood (or
radium) reacts: it makes a reagent, spawns items or mobs, or does something nasty. The slime scanner reads slimes and
extracts, and the xenobiological bounty console takes extracts for research points.

Players get in through the Xenobiology research tech (grinder and bounty console boards on the circuit imprinter,
scanner and xenovac on the protolathe) and the xenobiology starter crate from cargo.

Entry points: `XenobiologySystem` (breeding, mutation, taming), `MobGrowthSystem`, `SlimeLatchSystem` and the latch
HTN operators, `SharedXenoVacuumSystem`, `SlimeGrinderSystem`, `SlimeScannerSystem`, `XenobiologyBountyConsoleSystem`,
and the extract effects under `Effects/`. Breeds are `breed` prototypes; shared reaction steps are `xenobioEffectSet`
prototypes.

## Differences from Goob and Trauma

- Wolfgate is on the old entity effects API, so each extract reaction is one `XenobioReaction` that runs a shared
  effect set and its own effects at strength one (old effects scale with the injected amount; Goob's don't). The
  effects Wolfgate lacks are written here: spawn, relay to nearby, smoke, delayed effects, timed stealth, faction
  change and component grants, random teleport, and the extract bookkeeping.
- Slimes use Wolfgate hunger with Goob's satiation thresholds, and sell for 500, half a normal simple mob.
- The bounty board lives on the research server instead of the station, since ships are their own stations here, and
  every console linked to that server shows it. It draws five random bounties every twelve minutes, a skip swaps one of
  them, and spent extracts aren't accepted. A fully served board pays about two data farms' worth of research.
- Steroid and boost jelly add at most two extracts or two offspring to the slime that is dosed, and its offspring don't
  inherit the bonus. Goob stacks both without limit and passes them on, which doubles a line's yield every generation.
- The purple extract makes 5u of omnizine, not 15u; no chemical reaction makes omnizine here.
- Wolfgate has no species change, so the green jelly and the human, felinid and advanced mutation toxins are gone; the
  green extract makes unstable mutagen instead and the black extract keeps only its failing reactions. The light pink
  extract's DNA scramble, the sepia chronofield and the red extract's clothing coating are gone too.
- Trauma's adamantine bar only matters to its forging system, so the adamantine extract gives two plasteel
  sheets (not PDV composite, which is the PDV's currency); more would out-produce mining. With blood it grows a slimy
  flatpack of a slime heart instead (see Slime heart below), its real reward. The volatile organ is a self-charging power cell with a
  hyper cell's charge and a microreactor's recharge.
- Gold extract mob spawners only list mobs Wolfgate has. The dark blue jelly grants heat immunity, without ash storm
  immunity.

## Slime heart

Slime hearts are living ship fittings that only xenobiology can make. An adamantine slime extract injected with blood
grows a slimy flatpack (`WFSlimeHeartAdamantineFlatpack`) that a multitool unpacks into an adamantine slime heart
(`WFSlimeHeartAdamantine`). Bolted to a ship's hull and fed, it regrows everything damaged that is bolted to that
hull (walls, windows, thrusters, guns, machines, doors; not crew or other hearts): one percent of a part's full
integrity every five seconds, once that part has gone 30 seconds without taking damage. Every damaged part regrows at
once. Destroyed parts stay a job for
the repair device and the drydock.

- One heart beats per hull. A wrench refuses to bolt down a second, and one bolted at the same moment as another comes
  loose when it finishes. If two meet anyway (the repair device merging a hull section back), only one works and the
  other lies idle. A heart bonds to its first hull (HullBond).
- A new heart starts unfed and dormant, so it takes extracts from the start. Using an unspent adamantine extract on
  it feeds it for 20 minutes, up to 60. An unfed heart goes dormant and wakes when fed; it never starves to death.
- It has 200 health and no board, so killing it costs the ship its heart. It glows while it beats, and examining it
  shows how long it stays fed.
- While it is regrowing the hull it beats faster under a brighter glow (`Mending` state), and each part it regrows
  plays a short green burst (`WFEffectSlimeRegrowAdamantine`).

Entry point: `WFSlimeHeartSystem` (`Tick`, `TryFeed`, `IsBeating`). A heart for another breed is a prototype with its
own `food` tag; a different effect needs its own code. `SlimeHeartTest` covers the flatpack, regrowth and its visuals,
feeding and the hull limit.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Xenobiology/Effects/XenobioEffects.cs`](Effects/XenobioEffects.cs)
- [`Content.Server/_WF/Xenobiology/Effects/XenobioTimedEffects.cs`](Effects/XenobioTimedEffects.cs)
- [`Content.Server/_WF/Xenobiology/NPC/SlimeLatchOperators.cs`](NPC/SlimeLatchOperators.cs)
- [`Content.Server/_WF/Xenobiology/SlimeGrinderSystem.cs`](SlimeGrinderSystem.cs)
- [`Content.Server/_WF/Xenobiology/SlimeScannerSystem.cs`](SlimeScannerSystem.cs)
- [`Content.Server/_WF/Xenobiology/WFSlimeHeartComponent.cs`](WFSlimeHeartComponent.cs)
- [`Content.Server/_WF/Xenobiology/WFSlimeHeartSystem.cs`](WFSlimeHeartSystem.cs)
- [`Content.Server/_WF/Xenobiology/XenobiologyBountyConsoleSystem.cs`](XenobiologyBountyConsoleSystem.cs)

### Shared

- [`Content.Shared/_WF/Xenobiology/Bounties/XenobiologyBountyPrototype.cs`](../../../Content.Shared/_WF/Xenobiology/Bounties/XenobiologyBountyPrototype.cs)
- [`Content.Shared/_WF/Xenobiology/BreedPrototype.cs`](../../../Content.Shared/_WF/Xenobiology/BreedPrototype.cs)
- [`Content.Shared/_WF/Xenobiology/Components/MobGrowthComponent.cs`](../../../Content.Shared/_WF/Xenobiology/Components/MobGrowthComponent.cs)
- [`Content.Shared/_WF/Xenobiology/Components/SlimeComponent.cs`](../../../Content.Shared/_WF/Xenobiology/Components/SlimeComponent.cs)
- [`Content.Shared/_WF/Xenobiology/SlimeGrowthAttemptEvent.cs`](../../../Content.Shared/_WF/Xenobiology/SlimeGrowthAttemptEvent.cs)
- [`Content.Shared/_WF/Xenobiology/Systems/MobGrowthSystem.cs`](../../../Content.Shared/_WF/Xenobiology/Systems/MobGrowthSystem.cs)
- [`Content.Shared/_WF/Xenobiology/Systems/SharedXenoVacuumSystem.cs`](../../../Content.Shared/_WF/Xenobiology/Systems/SharedXenoVacuumSystem.cs)
- [`Content.Shared/_WF/Xenobiology/Systems/SlimeExtractSystem.cs`](../../../Content.Shared/_WF/Xenobiology/Systems/SlimeExtractSystem.cs)
- [`Content.Shared/_WF/Xenobiology/Systems/SlimeLatchSystem.cs`](../../../Content.Shared/_WF/Xenobiology/Systems/SlimeLatchSystem.cs)
- [`Content.Shared/_WF/Xenobiology/Systems/XenobiologySystem.cs`](../../../Content.Shared/_WF/Xenobiology/Systems/XenobiologySystem.cs)
- [`Content.Shared/_WF/Xenobiology/WFSlimeHeartVisuals.cs`](../../../Content.Shared/_WF/Xenobiology/WFSlimeHeartVisuals.cs)
- [`Content.Shared/_WF/Xenobiology/XenobioEffectSetPrototype.cs`](../../../Content.Shared/_WF/Xenobiology/XenobioEffectSetPrototype.cs)

### Client

- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyEntry.xaml`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyEntry.xaml)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyEntry.xaml.cs`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyEntry.xaml.cs)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyHistoryEntry.xaml`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyHistoryEntry.xaml)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyHistoryEntry.xaml.cs`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyHistoryEntry.xaml.cs)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyMenu.xaml`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyMenu.xaml)
- [`Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyMenu.xaml.cs`](../../../Content.Client/_WF/Xenobiology/Bounties/XenobiologyBountyMenu.xaml.cs)
- [`Content.Client/_WF/Xenobiology/XenobiologyVisualizerSystems.cs`](../../../Content.Client/_WF/Xenobiology/XenobiologyVisualizerSystems.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Xenobiology/SlimeHeartTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Xenobiology/SlimeHeartTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Xenobiology/XenobiologyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Xenobiology/XenobiologyTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Xenobiology/bounties.yml`](../../../Resources/Prototypes/_WF/Xenobiology/bounties.yml)
- [`Resources/Prototypes/_WF/Xenobiology/breeds.yml`](../../../Resources/Prototypes/_WF/Xenobiology/breeds.yml)
- [`Resources/Prototypes/_WF/Xenobiology/effect_sets.yml`](../../../Resources/Prototypes/_WF/Xenobiology/effect_sets.yml)
- [`Resources/Prototypes/_WF/Xenobiology/equipment.yml`](../../../Resources/Prototypes/_WF/Xenobiology/equipment.yml)
- [`Resources/Prototypes/_WF/Xenobiology/extracts.yml`](../../../Resources/Prototypes/_WF/Xenobiology/extracts.yml)
- [`Resources/Prototypes/_WF/Xenobiology/floors.yml`](../../../Resources/Prototypes/_WF/Xenobiology/floors.yml)
- [`Resources/Prototypes/_WF/Xenobiology/guidebook.yml`](../../../Resources/Prototypes/_WF/Xenobiology/guidebook.yml)
- [`Resources/Prototypes/_WF/Xenobiology/htn.yml`](../../../Resources/Prototypes/_WF/Xenobiology/htn.yml)
- [`Resources/Prototypes/_WF/Xenobiology/items.yml`](../../../Resources/Prototypes/_WF/Xenobiology/items.yml)
- [`Resources/Prototypes/_WF/Xenobiology/machines.yml`](../../../Resources/Prototypes/_WF/Xenobiology/machines.yml)
- [`Resources/Prototypes/_WF/Xenobiology/misc.yml`](../../../Resources/Prototypes/_WF/Xenobiology/misc.yml)
- [`Resources/Prototypes/_WF/Xenobiology/reagents.yml`](../../../Resources/Prototypes/_WF/Xenobiology/reagents.yml)
- [`Resources/Prototypes/_WF/Xenobiology/research.yml`](../../../Resources/Prototypes/_WF/Xenobiology/research.yml)
- [`Resources/Prototypes/_WF/Xenobiology/slime_heart.yml`](../../../Resources/Prototypes/_WF/Xenobiology/slime_heart.yml)
- [`Resources/Prototypes/_WF/Xenobiology/slimes.yml`](../../../Resources/Prototypes/_WF/Xenobiology/slimes.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Xenobiology/xenobiology.ftl`](../../../Resources/Locale/en-US/_WF/Xenobiology/xenobiology.ftl)

### Textures

- [`Resources/Textures/_WF/Xenobiology/Actions/actions_slime.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Actions/actions_slime.rsi/)
- [`Resources/Textures/_WF/Xenobiology/bounty_computer.rsi/`](../../../Resources/Textures/_WF/Xenobiology/bounty_computer.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Mobs/slimesAdult.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Mobs/slimesAdult.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Mobs/slimesBaby.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Mobs/slimesBaby.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Shaders/xeno_bluespace.swsl`](../../../Resources/Textures/_WF/Xenobiology/Shaders/xeno_bluespace.swsl)
- [`Resources/Textures/_WF/Xenobiology/Shaders/xeno_metallic.swsl`](../../../Resources/Textures/_WF/Xenobiology/Shaders/xeno_metallic.swsl)
- [`Resources/Textures/_WF/Xenobiology/Shaders/xeno_sepia.swsl`](../../../Resources/Textures/_WF/Xenobiology/Shaders/xeno_sepia.swsl)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/extract.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/extract.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/floors.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/floors.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/goo_ball.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/goo_ball.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_heart.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_heart.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_heart_flatpack.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_heart_flatpack.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_nozzle.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_nozzle.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_pack.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_pack.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_processor.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_processor.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_regrow.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_regrow.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_scanner.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/slime_scanner.rsi/)
- [`Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/volatile_organ.rsi/`](../../../Resources/Textures/_WF/Xenobiology/Specific/Xenobiology/volatile_organ.rsi/)

### Guidebook

- [`Resources/ServerInfo/_WF/Xenobiology/Guidebook/Xenobiology.xml`](../../../Resources/ServerInfo/_WF/Xenobiology/Guidebook/Xenobiology.xml)

## Non-modular edits

- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml)
  - slime scanner and xenovac
  - slime grinder and bounty console boards
- [`Resources/Prototypes/Guidebook/science.yml`](../../../Resources/Prototypes/Guidebook/science.yml)

<!-- WOLFGATE-GENERATED END -->
