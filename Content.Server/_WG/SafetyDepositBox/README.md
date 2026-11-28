# SafetyDepositBox

Safety deposit boxes, ported from Wayfarer and inherited through upstream Monolith (#4424). A console sells small,
medium and large boxes paid from the player's bank accounts, keeps a deposited box between rounds and hands it back
on withdrawal. A lost box can be reclaimed: its record is dropped and a new empty box spawned. Items that have been
in a box say so on examine.

Entry points: `SafetyDepositBoxSystem` (purchase, deposit, withdraw, reclaim), `SafetyDepositConsoleComponent` and
`SafetyDepositBoxComponent`, `SafetyDepositStoredSystem` (examine text), and `SafetyDepositConsoleWindow`.

## Wolfgate changes

The C# under `Content.Server/_WG/SafetyDepositBox`, `Content.Client/_WG/SafetyDepositBox` and
`Content.Shared/_WG/SafetyDepositBox` is upstream Monolith's, at the same paths. It sits under `_WG`, so these edits
carry no markers; check them on an upstream merge:

- `SafetyDepositBoxSystem.cs`: hard-coded popups converted to Fluent.
- `SafetyDepositConsoleWindow.xaml.cs`: the unnamed box label uses `safety-deposit-console-box-unnamed`.
- The locale moved from `Resources/Locale/en-US/_WG/` into `Resources/Locale/en-US/_WG/SafetyDepositBox/`, and the
  prototype, the `SafetyDepositBox` tag and the textures moved from the `_WG` roots into `SafetyDepositBox` module
  folders.
- `SafetyDepositBoxSystem.cs`: in `WithdrawBoxAsync`, a stored item that fails to load is logged and skipped
  (`continue`) where upstream returned, which left the box in the world with its items still in the database.
- `SafetyDepositBoxSystem.cs`: in `WithdrawBoxAsync`, a stored item the box no longer takes (blacklist, size) goes to
  `DropRefusedItem` where upstream deleted it, and the popup comes from `WithdrawPopup`, which counts those items.
- `SafetyDepositBoxSystem.Refused.cs` is Wolfgate's own: `DropRefusedItem` puts a refused item down at the box, and
  `WithdrawPopup` tells the player.
- `SafetyDepositBoxSystem.Timers.cs` is Wolfgate's own: `ClearSavedCooldowns`, called in `WithdrawBoxAsync`, ends the
  `UseDelay` cooldowns an item was saved with, which held the old round's clock.
- `SafetyDepositBoxSystem.cs`: `OnPurchase` calls `BankSystem.RefreshBalance` (`BankSystem.Refresh.cs`, Wolfgate's own)
  after charging. The charge goes through the session, which leaves the bank component and the ATM showing the old
  balance. The bank is spent before savings (the Savings module changed the order in `BankSystem.TryBankWithdraw`).
- Box sizes are three times upstream's: small 4x3 up to Normal items, medium 6x4 and large 9x4 up to Huge, so armour
  vests, hardsuits and backpacks fit. Storage refuses a bag that holds items as big as its own limit, so the medium and
  large boxes' storage takes Ginormous and the allow list's `maxItemSize` keeps single items to Huge. A box's item size
  matches its storage (`StorageSizeArbitrageTest`), so those two are Ginormous.
- `SafetyDepositAllowListComponent` and `SafetyDepositAllowListSystem` are Wolfgate's own. The storage blacklist
  no longer refuses storage, containers or solutions, so bags, boxes, guns, powered tools, food and drink go in.
  Currency, contraband and machine parts moved to the box's `restricted` list, which refuses them unless the allow
  list names them: thalers by stack type, tier 1 faction suits and webbing by prototype, and the rest by `allowances`
  (faction weapons up to tier 3 and armour up to tier 2 by contraband severity, tier 1 faction gear, power cells, tier 1 and 2
  stock parts by exact prototype). An allowance naming severities takes only contraband of them; one naming none takes
  no contraband. An allowance with a `limit` lets in that many items, stacks counted, across the whole box. `refused`
  groups name launchers, bluespace storage, built-in, mech, magic and admin items, abstract bases included, and refuse
  everything inheriting from them whatever else lets them in. An item's solutions may hold the `reagentGroups` (food
  and drink) freely, the `reagentLimits` groups (medicine) past what it spawns with up to a total across the box, and
  other reagents only up to what its prototype spawns with. Living things, minds, plant analyzers, seeds, emags, body
  parts, organs and machine parts in raw form (boards, electronics, reactor parts, mech equipment, cyborg modules,
  vehicle hardpoints) joined the blacklist or the restricted list.
- `SafetyDepositAllowListSystem.FindRefused`, called by `EjectNestedRefused` on deposit, holds everything in the box,
  however deep, to the blacklist, the allow list and the limits, and hands back what fails. Stored items count first,
  so what goes over a limit comes out of the bags. A hardsuit's helmet is exempt, as it belongs to the suit. Inserting
  counts the same way, against what the box already holds.
- The guidebook lists the refused and allowed items from the box prototype (`SafetyDepositRefusedEmbed`,
  `List="Allowed"`). The implant string's key now matches its component, `SubdermalImplant`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/SafetyDepositBox/BankSystem.Refresh.cs`](BankSystem.Refresh.cs)
- [`Content.Server/_WG/SafetyDepositBox/SafetyDepositBoxSystem.cs`](SafetyDepositBoxSystem.cs)
- [`Content.Server/_WG/SafetyDepositBox/SafetyDepositBoxSystem.Refused.cs`](SafetyDepositBoxSystem.Refused.cs)
- [`Content.Server/_WG/SafetyDepositBox/SafetyDepositBoxSystem.Timers.cs`](SafetyDepositBoxSystem.Timers.cs)

### Shared

- [`Content.Shared/_WG/SafetyDepositBox/BUI/SafetyDepositConsoleState.cs`](../../../Content.Shared/_WG/SafetyDepositBox/BUI/SafetyDepositConsoleState.cs)
- [`Content.Shared/_WG/SafetyDepositBox/BUI/SafetyDepositConsoleUiKey.cs`](../../../Content.Shared/_WG/SafetyDepositBox/BUI/SafetyDepositConsoleUiKey.cs)
- [`Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositAllowListComponent.cs`](../../../Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositAllowListComponent.cs)
- [`Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositBoxComponent.cs`](../../../Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositBoxComponent.cs)
- [`Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositConsoleComponent.cs`](../../../Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositConsoleComponent.cs)
- [`Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositStoredComponent.cs`](../../../Content.Shared/_WG/SafetyDepositBox/Components/SafetyDepositStoredComponent.cs)
- [`Content.Shared/_WG/SafetyDepositBox/Events/SafetyDepositMessages.cs`](../../../Content.Shared/_WG/SafetyDepositBox/Events/SafetyDepositMessages.cs)
- [`Content.Shared/_WG/SafetyDepositBox/SafetyDepositAllowListSystem.cs`](../../../Content.Shared/_WG/SafetyDepositBox/SafetyDepositAllowListSystem.cs)
- [`Content.Shared/_WG/SafetyDepositBox/SafetyDepositStoredSystem.cs`](../../../Content.Shared/_WG/SafetyDepositBox/SafetyDepositStoredSystem.cs)

### Client

- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleBoundUserInterface.cs`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleBoundUserInterface.cs)
- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleWindow.xaml`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleWindow.xaml)
- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleWindow.xaml.cs`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositConsoleWindow.xaml.cs)
- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositPurchaseConfirmWindow.xaml`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositPurchaseConfirmWindow.xaml)
- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositPurchaseConfirmWindow.xaml.cs`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositPurchaseConfirmWindow.xaml.cs)
- [`Content.Client/_WG/SafetyDepositBox/SafetyDepositRefusedEmbed.cs`](../../../Content.Client/_WG/SafetyDepositBox/SafetyDepositRefusedEmbed.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositAllowListTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositAllowListTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxDamoclesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxDamoclesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxPurchaseTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxPurchaseTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxWithdrawTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SafetyDepositBox/SafetyDepositBoxWithdrawTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/SafetyDepositBox/Entities/Structures/Machines/safety_deposit_box.yml`](../../../Resources/Prototypes/_WG/SafetyDepositBox/Entities/Structures/Machines/safety_deposit_box.yml)
- [`Resources/Prototypes/_WG/SafetyDepositBox/tags.yml`](../../../Resources/Prototypes/_WG/SafetyDepositBox/tags.yml)

### Localization

- [`Resources/Locale/en-US/_WG/SafetyDepositBox/safety-deposit-box.ftl`](../../../Resources/Locale/en-US/_WG/SafetyDepositBox/safety-deposit-box.ftl)

### Textures

- [`Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxlarge.rsi/`](../../../Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxlarge.rsi/)
- [`Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxmedium.rsi/`](../../../Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxmedium.rsi/)
- [`Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxsmall.rsi/`](../../../Resources/Textures/_WG/SafetyDepositBox/Objects/Economy/depositboxsmall.rsi/)
- [`Resources/Textures/_WG/SafetyDepositBox/Structures/Machines/atm/depositatm.rsi/`](../../../Resources/Textures/_WG/SafetyDepositBox/Structures/Machines/atm/depositatm.rsi/)

## Non-modular edits

- [`Resources/Prototypes/_Mono/Entities/Objects/Weapons/Guns/Pistols/pistol.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Weapons/Guns/Pistols/pistol.yml): the Godfather's exclusive sidearm can't be banked
- [`Resources/ServerInfo/_NF/Guidebook/SafetyDepositBox.xml`](../../../Resources/ServerInfo/_NF/Guidebook/SafetyDepositBox.xml)
  - boxes hold three times as much; medium and large take suits and armour
  - what a box refuses is read from the box prototype, not hand-listed

<!-- WOLFGATE-GENERATED END -->
