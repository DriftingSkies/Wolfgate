# Companies

Player-run companies that last across rounds. A character (not an account) joins one company and takes a rank in it.
All five phases of [the design](../../../Docs/_WG/Companies/DESIGN.md) are built: creating companies, the directory
with applications, invitations, ranks with permissions, wages and daily withdraw caps, deposits and withdrawals between
a character's bank and the company account, hourly payroll, the automatic cut, a ledger of every movement, and admin
commands, and company identity in the round.

Members whose character is in the lobby during a round get **Join the round as <rank>** in the Company window: they
spawn at the first standard station with a free slot for the rank's base job (the owner uses the top rank's), with the
rank title on their ID, mind and station record. Any member who spawns as their member character gets the company on
their ID's examine text and the company radio channel (`:4`), named after the company tag; membership is checked on
every send and receive, as for crew radio. A Crew Setup host whose rank has Field crews can fly the crew under the
company: its tag shows on crew rows, details and postings, members of the company see its crews first, and its ship
carries `WGCompanyShipComponent`, which the IFF label shows as `[TAG] Name`.

Creating a company needs 25 hours played in total (`wg.companies.create_hours`) and takes a 100,000 fee from the
founder's bank (`wg.companies.create_fee`), refunded if creation fails. The fee leaves the economy.

A player has one character per company: invitations, applications and open access refuse a character whose player
already has another in it, and the fleet licence minimum counts players, not characters.

Clicking a company in the list opens its profile: owner, worth, ranks and members with their rank icons. An owner can
make the member list private in Settings, so only members see it; the company still lists and takes applications.

Apply opens a form: the rank the character wants and up to 500 characters on why. Applications are saved, so they
survive restarts, and online members who can accept them are told. Accepting gives the asked-for rank when the accepter
can set it, otherwise the lowest. The owner can turn on open access at one rank in Settings; anyone can then press
Join as <rank> in the Companies list without applying.

Each rank (and the owner's) can have a loadout of up to twenty entries from the loadouts any ship crew job can pick in the
character loadout. At every spawn as that character, the company pays for each entry while its account covers it (once
per member per round, so respawning can't farm it), then the member's bank, and anything neither can pay for is skipped. The items go into the bag, or the hands if it's full.
Custom rank icons also replace the job icon on HUDs, the examine card, the ghost orbit menu, the crew manifest, crew
monitoring and criminal records: the ID carries the rank, and the station record and suit sensor status copy it.
Job icon presets in the rank icon editor are read by the server, because packaged clients pack RSIs into one file.

Payroll counts a minute at a time while a member plays the character the membership belongs to: a living or critical
body spawned from it, not a ghost, not AFK. Each full hour pays the rank's wage from the account into the character's
bank; the owner has no wage. If the account can't cover every wage due at once, everyone due gets the same percentage
and the owner is told. Counted time is saved every five minutes, on disconnect and at round restart.

The owner's cut (0-50%, default 10%) comes off a member's cargo pallet sales (the cash the console spawns), encounter
kill bounties and rewards, and bounties and shipbreaker warrants paid through `WGEncounterBountySystem.Deposit`, and is
paid into the account with a ledger row per payout. Cargo bounty cash and other income are not cut.

The Company window opens on the Companies list: the player's own company first with Open your company, then
invitations, then every other company with its logo, tag, owner and description, and Apply on each (recruiting only
highlights a company). Members also get Overview, Members, Ranks, Ledger and Settings. Settings has a 32x32 logo editor
(pencil, fill, colour pick, palette and the character creator's colour picker, PNG import averaged down to fit) for
ranks with Design, and the Ranks tab the same editor at 8x8 for each rank's job icon. A member's ID carries their rank
(`WGCompanyRankIconComponent`; the owner shows the top rank's), every spawned body gets `WGCompanyRankHudComponent`,
and the client's `WGCompanyRankHudSystem` swaps the HUD job icon for the rank icon, drawn from an in-memory content
root (`WGCompanyIconResources`). Icons are raw RGBA on the rank row and pushed to every client when they change; logos are raw RGBA in the company row, sent to clients by version on request (`companyart` wipes or
locks them). Overview links to Crew Setup to find or start company crews, and Crew Setup offers the company when
creating a crew and on the crew's front page.

The rank icon editor can start from any existing job icon. The round end summary lists every company that played or
moved money that round: members who played and hours, new members, crews fielded, wages, cuts, deposits, withdrawals
and the account's start and end.

**Crews under a company.** A company's members in its crew always play their company rank: job, title and icon. They
can't pick another job, and a character outside the company picks from the openings as usual. A host who switches to a
character who can't field the company drops it from the crew.

**Quick join.** `WGCompanyQuickJoin` is one card with every way a member can get into the round: their company's crews,
hangar ships ready to launch, and joining as their rank. It shows on the lobby menu, the crew browser, the late-join
window and the Companies tab. A player whose selected character isn't in a company but who has one that is gets a button
to switch to it.

**Hangar.** The owner buys fleet licences (up to 3) for civilian designs any character may fly, or for the company-only
Mercenary and Mieyo designs, at 35x the shipyard
price and at least 2,000,000, from the account; selling one refunds nothing. A member with Launch presses Launch on the
Hangar tab: that creates a Crew Setup ship crew for the design, flying under the company, with the design fixed and no
crew payment. When the ship actually spawns, the account pays the registry fee (5%, 15% then 30% of the shipyard price
by how many company ships are already out) and the launching character pays the owner's launch fee into the account;
a failed spawn refunds both. Each licence launches once per round. The ship gets `WGCompanyHangarShipComponent` and
the shipyard refuses to buy it back. The drydock and chop shop refuse to break it, and everything aboard at launch,
contents included, gets `WGCompanyPropertyComponent` and appraises at nothing (anything put inside it later keeps its
value), so a free ship each round can't be turned into cash. Server side: `WGCompanySystem.Hangar` and `WGRoundSetupSystem.Hangar`.
Preview on a licence or beside the licence picker opens the `ShipPreview` window; it follows the picker while open.

**Paying members.** Besides hourly wages, the owner can pay any online member from the Members tab, straight into that
character's bank, with an optional note; the member gets a chat message and the ledger a Payment row.

**Ship access.** Ship access consoles gain a Company mode and a Company door rule beside Crew: choosing either links the
ship to the player's company (`WGShipCompanyLinkEvent`), and the ID cards of members playing this round open it.
Company crew and hangar ships link automatically. Their IFF label shows `[TAG] Name` and the radar colour turns to the
company colour, restored if the ship stops flying for it.

**Joining as your rank.** Outside crews, Join the round as <rank> in the Company window. In Crew Setup, a member sees
Join as <rank> on their own company's crews: it takes no opening, gives the rank's base job and puts the rank title on
their ID (live members just join). Requests made this way show as "their company rank" on the host's card.

**Owner rank.** The owner has a rank of their own (title, base job and job icon, stored on the company row; its id is
the company id negated), set at the top of the Ranks tab. It defaults to "Owner" and Contractor and grants every
permission whatever it's called.

**Companies list.** Companies are ranked by member count, then worth (the account plus the cost of their fleet
licences), and each entry's headline gives both. Tags are unique in any case. A member's examine card (IDENTITY or
PERSONAL RECORD) gets a COMPANY row from their ID with the logo, rank icon, company and rank.

**Fleet licences need 3 members** before a company can buy or launch one, so a one-person company can't farm them.

The character creator's Company tab shows the character's company, logo and rank icon, then every company on the
server.

Players open the Company window from the Company button in Crew Setup or from the character creator's Company tab,
which now shows the character's company instead of the Mono company picker. The window acts as the character being
played, or the selected one in the lobby. Owners hold every permission; other ranks act only on ranks below their own
and can't grant permissions they lack. Ironman characters can't join. Deleting a character removes its membership;
a deleted owner's company passes to the highest-ranked member at the next round restart, and an empty one is disbanded.

The Mono company on a character profile no longer applies at spawn; jobs still set gameplay factions. The selectable
flavour companies are commented out of `_Mono/companies.yml`.

`WGCompanySystem` keeps every company in memory and writes each change through to the database (the `WolfgateCompany*`
tables via `ServerDbBase.Companies`); it reloads at each round restart once pending writes land. Admin commands:
`companylist`, `companyinfo`, `companyledger`, `companyfreeze`, `companyadjust`, `companyrename`, `companytransfer`,
`companydisband`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Companies/RadioSystem.Company.cs`](RadioSystem.Company.cs)
- [`Content.Server/_WG/Companies/ServerDbBase.Companies.cs`](ServerDbBase.Companies.cs)
- [`Content.Server/_WG/Companies/ServerDbManager.Companies.cs`](ServerDbManager.Companies.cs)
- [`Content.Server/_WG/Companies/WGCompanyCommands.cs`](WGCompanyCommands.cs)
- [`Content.Server/_WG/Companies/WGCompanyHangarShipComponent.cs`](WGCompanyHangarShipComponent.cs)
- [`Content.Server/_WG/Companies/WGCompanyPropertyComponent.cs`](WGCompanyPropertyComponent.cs)
- [`Content.Server/_WG/Companies/WGCompanyRadioComponent.cs`](WGCompanyRadioComponent.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Access.cs`](WGCompanySystem.Access.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Admin.cs`](WGCompanySystem.Admin.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.cs`](WGCompanySystem.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Hangar.cs`](WGCompanySystem.Hangar.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Identity.cs`](WGCompanySystem.Identity.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Loadouts.cs`](WGCompanySystem.Loadouts.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Payroll.cs`](WGCompanySystem.Payroll.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Report.cs`](WGCompanySystem.Report.cs)
- [`Content.Server/_WG/Companies/WGCompanySystem.Requests.cs`](WGCompanySystem.Requests.cs)
- [`Content.Server/_WG/Companies/WGStoredCompany.cs`](WGStoredCompany.cs)

### Shared

- [`Content.Shared/_WG/Companies/WGCompanyCVars.cs`](../../../Content.Shared/_WG/Companies/WGCompanyCVars.cs)
- [`Content.Shared/_WG/Companies/WGCompanyIdComponent.cs`](../../../Content.Shared/_WG/Companies/WGCompanyIdComponent.cs)
- [`Content.Shared/_WG/Companies/WGCompanyLoadouts.cs`](../../../Content.Shared/_WG/Companies/WGCompanyLoadouts.cs)
- [`Content.Shared/_WG/Companies/WGCompanyMessages.cs`](../../../Content.Shared/_WG/Companies/WGCompanyMessages.cs)
- [`Content.Shared/_WG/Companies/WGCompanyPolicy.cs`](../../../Content.Shared/_WG/Companies/WGCompanyPolicy.cs)
- [`Content.Shared/_WG/Companies/WGCompanyRankIconComponent.cs`](../../../Content.Shared/_WG/Companies/WGCompanyRankIconComponent.cs)
- [`Content.Shared/_WG/Companies/WGCompanyShipComponent.cs`](../../../Content.Shared/_WG/Companies/WGCompanyShipComponent.cs)

### Client

- [`Content.Client/_WG/Companies/CharacterPickerButton.Companies.cs`](../../../Content.Client/_WG/Companies/CharacterPickerButton.Companies.cs)
- [`Content.Client/_WG/Companies/HumanoidProfileEditor.Companies.cs`](../../../Content.Client/_WG/Companies/HumanoidProfileEditor.Companies.cs)
- [`Content.Client/_WG/Companies/WGCompanyApplyWindow.cs`](../../../Content.Client/_WG/Companies/WGCompanyApplyWindow.cs)
- [`Content.Client/_WG/Companies/WGCompanyClientSystem.cs`](../../../Content.Client/_WG/Companies/WGCompanyClientSystem.cs)
- [`Content.Client/_WG/Companies/WGCompanyIconResources.cs`](../../../Content.Client/_WG/Companies/WGCompanyIconResources.cs)
- [`Content.Client/_WG/Companies/WGCompanyJoinActions.cs`](../../../Content.Client/_WG/Companies/WGCompanyJoinActions.cs)
- [`Content.Client/_WG/Companies/WGCompanyLoadoutWindow.cs`](../../../Content.Client/_WG/Companies/WGCompanyLoadoutWindow.cs)
- [`Content.Client/_WG/Companies/WGCompanyLogoCanvas.cs`](../../../Content.Client/_WG/Companies/WGCompanyLogoCanvas.cs)
- [`Content.Client/_WG/Companies/WGCompanyLogoWindow.cs`](../../../Content.Client/_WG/Companies/WGCompanyLogoWindow.cs)
- [`Content.Client/_WG/Companies/WGCompanyProfileWindow.cs`](../../../Content.Client/_WG/Companies/WGCompanyProfileWindow.cs)
- [`Content.Client/_WG/Companies/WGCompanyQuickJoin.cs`](../../../Content.Client/_WG/Companies/WGCompanyQuickJoin.cs)
- [`Content.Client/_WG/Companies/WGCompanyRankHudSystem.cs`](../../../Content.Client/_WG/Companies/WGCompanyRankHudSystem.cs)
- [`Content.Client/_WG/Companies/WGCompanyRankIcons.cs`](../../../Content.Client/_WG/Companies/WGCompanyRankIcons.cs)
- [`Content.Client/_WG/Companies/WGCompanyWindow.cs`](../../../Content.Client/_WG/Companies/WGCompanyWindow.cs)
- [`Content.Client/_WG/Companies/WGCompanyWindow.Hangar.cs`](../../../Content.Client/_WG/Companies/WGCompanyWindow.Hangar.cs)
- [`Content.Client/_WG/Companies/WGCompanyWindow.Join.cs`](../../../Content.Client/_WG/Companies/WGCompanyWindow.Join.cs)
- [`Content.Client/_WG/Companies/WGCompanyWindow.Members.cs`](../../../Content.Client/_WG/Companies/WGCompanyWindow.Members.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Applications.cs`](../../../Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Applications.cs)
- [`Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Hangar.cs`](../../../Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Hangar.cs)
- [`Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Identity.cs`](../../../Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Identity.cs)
- [`Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Payroll.cs`](../../../Content.IntegrationTests/Tests/_WG/Companies/WGCompanyTest.Payroll.cs)

### Unit tests

- [`Content.Tests/_WG/Companies/WGCompanyPolicyTest.cs`](../../../Content.Tests/_WG/Companies/WGCompanyPolicyTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Companies/radio.yml`](../../../Resources/Prototypes/_WG/Companies/radio.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Companies/companies.ftl`](../../../Resources/Locale/en-US/_WG/Companies/companies.ftl)

### Docs

- [`Docs/_WG/Companies/DESIGN.md`](../../../Docs/_WG/Companies/DESIGN.md)

## Non-modular edits

- [`Content.Client/_NF/LateJoin/Windows/PickerWindow.xaml`](../../../Content.Client/_NF/LateJoin/Windows/PickerWindow.xaml)
  - xmlns:companies for the join as rank bar
  - company members can join the round as their rank
- [`Content.Client/CrewManifest/UI/CrewManifestSection.cs`](../../../Content.Client/CrewManifest/UI/CrewManifestSection.cs): rank icon
- [`Content.Client/CriminalRecords/CriminalRecordsConsoleWindow.xaml.cs`](../../../Content.Client/CriminalRecords/CriminalRecordsConsoleWindow.xaml.cs): rank icon
- [`Content.Client/Lobby/UI/CharacterSetupGui.xaml.cs`](../../../Content.Client/Lobby/UI/CharacterSetupGui.xaml.cs): the character list names their company
- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs)
- [`Content.Client/Medical/CrewMonitoring/CrewMonitoringWindow.xaml.cs`](../../../Content.Client/Medical/CrewMonitoring/CrewMonitoringWindow.xaml.cs): rank icon
- [`Content.IntegrationTests/PoolManager.Cvars.cs`](../../../Content.IntegrationTests/PoolManager.Cvars.cs)
  - test players have no playtime
  - tests check balances without the creation fee
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - player-run companies, their ranks, members and ledger
  - names and tags are unique; a character's membership goes with the character
  - a company's ship licences
- [`Content.Server/_Mono/Company/CompanySystem.cs`](../../_Mono/Company/CompanySystem.cs): player companies replace the profile company; jobs still set factions
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs): a member's ship flies under their company
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.cs): company hangar ships belong to the company and can't be sold
- [`Content.Server/Cargo/Systems/CargoSystem.Shuttle.cs`](../../Cargo/Systems/CargoSystem.Shuttle.cs): the seller's company takes its cut
- [`Content.Server/CrewManifest/CrewManifestSystem.cs`](../../CrewManifest/CrewManifestSystem.cs): the card's rank icon replaces JobIcon
- [`Content.Server/GameTicking/GameTicker.Spawning.cs`](../../GameTicking/GameTicker.Spawning.cs): arrival announcement uses the rank, not Vagrant
- [`Content.Server/Medical/SuitSensors/SuitSensorSystem.cs`](../../Medical/SuitSensors/SuitSensorSystem.cs): the company rank on the ID, for its custom job icon
- [`Content.Shared/CrewManifest/SharedCrewManifestSystem.cs`](../../../Content.Shared/CrewManifest/SharedCrewManifestSystem.cs): rank whose custom icon replaces JobIcon
- [`Content.Shared/Medical/SuitSensor/SharedSuitSensor.cs`](../../../Content.Shared/Medical/SuitSensor/SharedSuitSensor.cs): rank whose custom icon replaces JobIcon
- [`Content.Shared/Shuttles/Systems/SharedShuttleSystem.IFF.cs`](../../../Content.Shared/Shuttles/Systems/SharedShuttleSystem.IFF.cs): a player company's ship shows its tag and name
- [`Content.Shared/StationRecords/GeneralStationRecord.cs`](../../../Content.Shared/StationRecords/GeneralStationRecord.cs): the holder's company rank, whose custom job icon screens show instead of JobIcon
- [`Resources/Prototypes/_Mono/companies.yml`](../../../Resources/Prototypes/_Mono/companies.yml): flavour companies replaced by player companies

<!-- WOLFGATE-GENERATED END -->
