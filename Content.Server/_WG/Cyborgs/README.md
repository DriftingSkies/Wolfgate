# Cyborgs

Quality-of-life changes for player cyborgs, so one can do a ship job without a second pair of hands or a charger
on board.

It also covers player AI cores; see **Station AI** below.

- **Construction stock** (`materials.yml`): the construction module's stacks start stocked and gain reinforced
  glass. The cheap ones (steel, glass, plastic, rods, floor tiles) carry `WGBorgStackRefill`, and
  `WGBorgStackRefillSystem` adds one unit every five seconds, up to the component's target, paid for out of the
  cyborg's cell (`chargePerUnit`, 20 J a sheet and 40 J a medical pack). It never spends the last quarter of the
  cell, so restocking can't run the cyborg flat. Plasteel and reinforced glass are issued once.
- **Gripper hands**: the tool module, which every cyborg type starts with, has two droppable gripper hands. One takes power
  cells, gas tanks, jerry cans, AME jars, flatpacks, ship vouchers and drydock repair vouchers; the other takes any stack, so a cyborg can fuel a generator
  or build with materials its modules don't issue.
- **Scanners**: the RCD module also carries a gas analyzer, geiger counter and t-ray scanner.
- **Headlight**: back as a toggle action, lighting the chassis sprite where it has a light layer. A marked edit on
  `BaseBorgChassis`.
- The recharging ship repair device in the RCD module starts full.
- **Power**: every chassis has `BatteryDrinker`, so alt-clicking an APC or a loose cell drains it into the cyborg's
  own cell; `WGBorgPowerSystem` points the drinker at the cell slot and names the installed cell on examine. Job
  cyborgs start on a high-capacity cell.
- **Bank**: every chassis has `BankAccount`, which resolves to the player's selected character, so ATMs, pay and
  purchases work. Cash goes in the tool module's stack hand.
- **Medical** (`medical.yml`): the treatment module's stacks refill from the cell and it carries the defibrillator,
  so the defibrillator module is no longer a default. The advanced treatment module has four beaker hands and a
  hand labeler.
- TSF and PDV engineer and medical cyborgs have four spare module slots instead of two.
- Every chassis has `Buckle` and `Climbing`, so a cyborg can take a seat and get onto a table.
- **Ships** (`id.yml`): the tool module has a third hand holding a cyborg ID card, which goes in a shipyard console
  to buy a ship and unlocks that ship's consoles, through the stock ID card paths. The card stands in for the worn
  ID a cyborg can't have: `WGBorgIdCardServerSystem` files the cyborg on the sector records service when a player
  takes the chassis, so the card carries a crew record key, and `WGBorgIdCardSystem` has access readers count the
  card while its module is put away. Ship access and crews read it as the cyborg's own card, so a crew ship's
  doors open for a cyborg in the crew. A cyborg that spawns into a job has no tool module until it picks a type, so
  it is issued the card in its chassis at spawn; the tool module then carries that card in place of its own.

- **PDA** (`pda.yml`): every chassis is its own PDA, with a vagrant's apps (those of `BasePDA` plus the ones every
  PDA gets in code) and its slots locked, opened from the Open PDA action. Apps that read an ID card (NanoChat) read
  the cyborg's issued card; `WGBorgPdaSystem` names the PDA after the cyborg and sends app notifications to the
  cyborg's chat, since the stock path only tells whoever carries a PDA. A marked edit on `BaseBorgChassis`.

- **Encryption keys**: every chassis has eight key slots. Unlock the chassis, open its panel with a screwdriver and
  use a key on it; wirecutters take them all out. The stock key path (`IntrinsicRadioKeySystem`) would replace the
  cyborg's radio channels with its keys', so `WGBorgRadioKeysServerSystem` puts the built-in channels back on top,
  and takes the type's channels as the new built-in set when a type is picked. While an AI drives the cyborg it
  keeps the AI's channels and updates the ones it restores. The Corvax back-to-AI action restores no channels (only
  removing the controller does), so the system gives the cyborg its own back when that action or a reopened core
  (`WGAiCoreGhostRoleSystem`) lets the AI go. The key holder isn't networked, so
  `WGBorgRadioKeysSystem` opens the slots from the networked lock right before each use or examine. A marked edit on
  `BaseBorgChassis`.
- **NCI AK-220 weapon module** (`nci_ak220.yml`): Viper Assault starts with a one-handed AK-220 with integrated,
  self-refilling caseless ammunition and a machete; it has no detachable magazine or manual bolt.

`BorgPdaTest` covers the apps, the action and a cyborg's NanoChat, `BorgStackRefillTest` the issued kit and the
cell-paid refill, `BorgPowerTest` the drinker lookup, `BorgShipPurchaseTest` a cyborg buying a ship with its card,
`BorgShipAccessTest` a crew door admitting it, and `BorgRadioKeysTest` the lock-gated key slots and channels, and an AI's return.

## Station AI

- **Reopening abandoned cores** (`WGAiCoreGhostRoleSystem`): AI brains inherit the posibrain's toggleable ghost role,
  which strips the ghost-role components on take and on ghost, so a core whose player ghosted stayed empty for the
  round. Every five seconds the system puts the ghost role back, from the brain's prototype, on any brain that sits
  in a core with no mind and isn't out controlling a borg. Brains spawned without a ghost role (job AIs) are left
  alone. Any borg the old AI ghosted out of is released, so it can't hand that player back into the reopened core.
- **Names**: a player who takes an AI ghost role is named after the Cyborg name in their selected character's
  loadout (character editor, Jobs tab, Cyborg's loadout button), if they set one; otherwise the core keeps its
  themed random name. The Station AI job and its loadout are hidden from the editor, so the cyborg name stands in.
  The brain, its mind and the core are all renamed.
- **Off-grid borgs** (`WGAiRemoteLinkSystem`): an AI can still only take control of networked borgs on its core's
  grid, but every borg it controls is linked to it (`WGAiRemoteLinkComponent`). It can list and reconnect to a
  linked borg anywhere on the same map. Another AI taking the borg moves the link; pulling the network device
  breaks it. Hooked into the Corvax-Next `AiRemoteControlSystem` by marked edits. A marked guard in its
  `ReturnMindIntoAi` leaves the AI in its borg when the core has been destroyed, where it used to throw on the
  deleted brain.
- **Faction company**: the TSFMC and PDV AI brains carry `Company`, so they pass their faction's requisition station
  and overwatch console readers. So do the TSF and PDV borg chassis, so built and ghost-role borgs (and an AI
  controlling one) pass them too, not only job-spawned borgs.
- **Overwatch view** (`WGAiOverwatchSystem`): an AI can view a marine's camera through an overwatch console. An AI
  moves its remote eye rather than its brain, so the eye is marked (`WGAiOverwatchEyeComponent`) and moving it ends
  the feed; marked edits in the Rat `OverwatchSystem` hand the view back to the eye instead of the brain. On the
  client, a `StationAiOverlay` partial skips the camera static while a feed is up.

`WGAiCoreTest` covers reopening, the borg exception, the profile name, the borg link, a core destroyed under a
borg-bound AI, the overwatch view and the chassis company.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Cyborgs/BorgSystem.ChargeUi.cs`](BorgSystem.ChargeUi.cs)
- [`Content.Server/_WG/Cyborgs/WGAiCoreGhostRoleSystem.cs`](WGAiCoreGhostRoleSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGAiOverwatchEyeComponent.cs`](WGAiOverwatchEyeComponent.cs)
- [`Content.Server/_WG/Cyborgs/WGAiOverwatchSystem.cs`](WGAiOverwatchSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGAiRemoteLinkComponent.cs`](WGAiRemoteLinkComponent.cs)
- [`Content.Server/_WG/Cyborgs/WGAiRemoteLinkSystem.cs`](WGAiRemoteLinkSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGBorgIdCardServerSystem.cs`](WGBorgIdCardServerSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGBorgPdaSystem.cs`](WGBorgPdaSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGBorgPowerSystem.cs`](WGBorgPowerSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGBorgRadioKeysServerSystem.cs`](WGBorgRadioKeysServerSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGBorgStackRefillSystem.cs`](WGBorgStackRefillSystem.cs)
- [`Content.Server/_WG/Cyborgs/WGOverwatchZoomLimitComponent.cs`](WGOverwatchZoomLimitComponent.cs)

### Shared

- [`Content.Shared/_WG/Cyborgs/WGBorgIdCardComponent.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgIdCardComponent.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgIdCardSystem.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgIdCardSystem.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgPdaComponent.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgPdaComponent.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgRadioKeysComponent.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgRadioKeysComponent.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgRadioKeysSystem.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgRadioKeysSystem.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgRescueSystem.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgRescueSystem.cs)
- [`Content.Shared/_WG/Cyborgs/WGBorgStackRefillComponent.cs`](../../../Content.Shared/_WG/Cyborgs/WGBorgStackRefillComponent.cs)

### Client

- [`Content.Client/_WG/Cyborgs/StationAiOverlay.Overwatch.cs`](../../../Content.Client/_WG/Cyborgs/StationAiOverlay.Overwatch.cs)
- [`Content.Client/_WG/Cyborgs/WGBorgRescueClientSystem.cs`](../../../Content.Client/_WG/Cyborgs/WGBorgRescueClientSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgPdaTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgPdaTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgPowerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgPowerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgRadioKeysTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgRadioKeysTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgRepairVoucherTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgRepairVoucherTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgShipAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgShipAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgShipPurchaseTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgShipPurchaseTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/BorgStackRefillTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/BorgStackRefillTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/FactionBorgCardTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/FactionBorgCardTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/WGAiCoreTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/WGAiCoreTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/WGBorgRescueTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/WGBorgRescueTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Cyborgs/WGViperAssaultModuleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Cyborgs/WGViperAssaultModuleTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Cyborgs/id.yml`](../../../Resources/Prototypes/_WG/Cyborgs/id.yml)
- [`Resources/Prototypes/_WG/Cyborgs/materials.yml`](../../../Resources/Prototypes/_WG/Cyborgs/materials.yml)
- [`Resources/Prototypes/_WG/Cyborgs/medical.yml`](../../../Resources/Prototypes/_WG/Cyborgs/medical.yml)
- [`Resources/Prototypes/_WG/Cyborgs/nci_ak220.yml`](../../../Resources/Prototypes/_WG/Cyborgs/nci_ak220.yml)
- [`Resources/Prototypes/_WG/Cyborgs/pda.yml`](../../../Resources/Prototypes/_WG/Cyborgs/pda.yml)
- [`Resources/Prototypes/_WG/Cyborgs/viper_borg_job.yml`](../../../Resources/Prototypes/_WG/Cyborgs/viper_borg_job.yml)
- [`Resources/Prototypes/_WG/Cyborgs/viper_borg_subtypes.yml`](../../../Resources/Prototypes/_WG/Cyborgs/viper_borg_subtypes.yml)
- [`Resources/Prototypes/_WG/Cyborgs/viper_borg_types.yml`](../../../Resources/Prototypes/_WG/Cyborgs/viper_borg_types.yml)
- [`Resources/Prototypes/_WG/Cyborgs/viper_group.yml`](../../../Resources/Prototypes/_WG/Cyborgs/viper_group.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Cyborgs/power.ftl`](../../../Resources/Locale/en-US/_WG/Cyborgs/power.ftl)
- [`Resources/Locale/en-US/_WG/Cyborgs/viper_group.ftl`](../../../Resources/Locale/en-US/_WG/Cyborgs/viper_group.ftl)

## Non-modular edits

- [`Content.Server/_CorvaxNext/Silicons/Borgs/AiRemoteControlSystem.cs`](../../_CorvaxNext/Silicons/Borgs/AiRemoteControlSystem.cs)
  - a borg the AI already controlled stays reachable off the core's grid
  - lets this AI reconnect once the borg leaves the grid
  - list linked borgs off the core's grid too
- [`Content.Server/_Rat/Overwatch/OverwatchSystem.cs`](../../_Rat/Overwatch/OverwatchSystem.cs)
  - a reused component must send its new target to the client
  - a feed is seen at the default zoom limit, not an AI's
  - an AI's view returns to its remote eye
  - the client sees the feed end, so an AI's static returns
- [`Content.Server/Chat/Systems/ChatSystem.Emote.cs`](../../Chat/Systems/ChatSystem.Emote.cs)
  - resolve shared typed emotes against entity restrictions
  - grants on shared trigger words bypass availability, not entity restrictions
- [`Content.Server/Silicons/Borgs/BorgSystem.cs`](../../Silicons/Borgs/BorgSystem.cs)
  - the charge changes every tick with the headlight on
  - an empty cell raises this on every failed draw
- [`Content.Shared/_CorvaxNext/Silicons/Borgs/SharedAiRemoteControlSystem.cs`](../../../Content.Shared/_CorvaxNext/Silicons/Borgs/SharedAiRemoteControlSystem.cs): a destroyed core leaves the AI in the borg instead of throwing on the deleted brain
- [`Resources/Maps/_Mono/POI/camelot.yml`](../../../Resources/Maps/_Mono/POI/camelot.yml): Mapped Viper Group cyborg spawns beside the other Viper spawns.
- [`Resources/Maps/_Mono/Test/dev_map.yml`](../../../Resources/Maps/_Mono/Test/dev_map.yml): Added a VG cyborg spawn point to the MonoDev test map.
- [`Resources/Prototypes/_DV/Voice/speech_emotes.yml`](../../../Resources/Prototypes/_DV/Voice/speech_emotes.yml)
  - share honk triggers while restricting the emote by entity
  - give borgs a separate Harpy-voiced honk emote
  - share beep triggers while restricting the emote by entity
  - give borgs a separate Harpy-voiced beep emote
- [`Resources/Prototypes/_Mono/borg_types.yml`](../../../Resources/Prototypes/_Mono/borg_types.yml)
  - was 2, too few to fit a printed kit
  - the treatment module carries the defibrillator
- [`Resources/Prototypes/_Mono/Entities/Mobs/Cyborgs/base_borg_chassis.yml`](../../../Resources/Prototypes/_Mono/Entities/Mobs/Cyborgs/base_borg_chassis.yml)
  - built and ghost-role borgs pass TSF company readers, not only job-spawned ones
  - Harpy noise emotes for TSF cyborgs
  - built and ghost-role borgs pass PDV company readers, not only job-spawned ones
  - Harpy noise emotes for PDV cyborgs
  - Harpy noise emotes for xenoborgs
- [`Resources/Prototypes/_Mono/Entities/Mobs/Player/silicon.yml`](../../../Resources/Prototypes/_Mono/Entities/Mobs/Player/silicon.yml)
  - passes TSF company readers (requisition station, overwatch console)
  - passes PDV company readers (requisition station, overwatch console)
- [`Resources/Prototypes/_Mono/Entities/Objects/Tools/repair.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Tools/repair.yml): was 50, a 20 minute wait before a cyborg's first real repair
- [`Resources/Prototypes/_Mono/PointsOfInterest/ussp.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/ussp.yml): raises Camelot's Viper borg cap to three
- [`Resources/Prototypes/_Mono/Roles/Jobs/departments.yml`](../../../Resources/Prototypes/_Mono/Roles/Jobs/departments.yml): registers the Viper Group borg job
- [`Resources/Prototypes/_Mono/Roles/Jobs/PDV/borg.yml`](../../../Resources/Prototypes/_Mono/Roles/Jobs/PDV/borg.yml): stamped on the issued card so the PDV shipyard sells to it
- [`Resources/Prototypes/_Mono/Roles/Jobs/TSFMC/borg.yml`](../../../Resources/Prototypes/_Mono/Roles/Jobs/TSFMC/borg.yml): stamped on the issued card so the TSFMC shipyard sells to it
- [`Resources/Prototypes/_NF/Maps/debug.yml`](../../../Resources/Prototypes/_NF/Maps/debug.yml): make VG cyborg selectable on MonoDev
- [`Resources/Prototypes/borg_types.yml`](../../../Resources/Prototypes/borg_types.yml): the treatment module carries the defibrillator
- [`Resources/Prototypes/Entities/Mobs/Cyborgs/base_borg_chassis.yml`](../../../Resources/Prototypes/Entities/Mobs/Cyborgs/base_borg_chassis.yml)
  - the built-in PDA's window and ringtone picker
  - encryption key slots behind the chassis lock and panel, adding to the built-in channels
  - the built-in PDA's installed apps
  - recharge from APCs and loose cells, and a bank account for ATMs and pay
  - buckle into seats, carry patients, and climb onto tables
  - a built-in PDA with a vagrant's apps, opened from an action
  - the headlight Monolith commented out, back as a toggle
  - Harpy noise emotes for cyborgs
  - installed keys drop when the chassis is destroyed
  - the action that opens the built-in PDA
  - Harpy noise emotes for syndicate, faction, and xenoborgs
- [`Resources/Prototypes/Entities/Mobs/Player/silicon.yml`](../../../Resources/Prototypes/Entities/Mobs/Player/silicon.yml): was PowerCellMedium, half an hour of work per charge
- [`Resources/Prototypes/Entities/Objects/Specific/Robotics/borg_modules.yml`](../../../Resources/Prototypes/Entities/Objects/Specific/Robotics/borg_modules.yml)
  - gripper hands for cells, tanks, fuel, flatpacks and materials, and one holding an ID card
  - stocked stacks that refill from the cell, plus reinforced glass
  - the scanners a ship job needs, without a printed advanced tool module
  - stacks that refill from the cell, and the defibrillator without a module of its own
  - the refilling WGSutureBorg above replaces it
  - to tell the beakers apart
  - four beakers, two were not enough to mix anything
- [`Resources/Prototypes/Voice/speech_emote_sounds.yml`](../../../Resources/Prototypes/Voice/speech_emote_sounds.yml)
  - Harpy noise emotes for cyborgs
  - Harpy noise emotes for syndicate, faction, and xenoborgs

<!-- WOLFGATE-GENERATED END -->
