using Content.Server._NF.SectorServices;
using Content.Server._WF.Companies;
using Content.Server._WF.Traders;
using Content.Server.Silicons.Borgs;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Interaction.Components;
using Content.Shared._NF.Interaction.Systems;
using Content.Shared._NF.Silicons.Borgs;
using Content.Shared._WF.Cyborgs;
using Content.Shared.Access.Systems;
using Content.Shared.Hands;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.StationRecords;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Cyborgs;

/// <summary>
/// Issues a crew cyborg its ID card, gives the card a crew record, the key ship doors, allow lists and crews
/// admit a card by, and stamps it with its player so traders take it.
/// </summary>
public sealed partial class WFBorgIdCardServerSystem : EntitySystem
{
    /// <summary>The job a cyborg's record is filed under.</summary>
    private static readonly ProtoId<JobPrototype> RecordJob = "Borg";

    /// <summary>The card a crew cyborg is issued.</summary>
    private static readonly EntProtoId IssuedCard = "WFBorgIdCard";

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private HandPlaceholderSystem _placeholder = default!;
    [Dependency] private IdCardOwnerSystem _idOwner = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private StationRecordKeyStorageSystem _keyStorage = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private WFBorgIdCardSystem _cards = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<WFBorgIdCardComponent, GotEquippedHandEvent>(OnCardEquipped);
        // Before the company marks the spawned member's card.
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete, before: new[] { typeof(WFCompanySystem) });
        // Before the borg system selects the module and puts its items in hand.
        SubscribeLocalEvent<DroppableBorgModuleComponent, BorgModuleInstalledEvent>(OnModuleInstalled, before: new[] { typeof(BorgSystem) });
    }

    private void OnPlayerAttached(Entity<BorgChassisComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!_cards.TryGetCard(ent, out var card))
            return;

        EnsureRecord(ent, card);
        // Overwritten: the card follows whoever runs the chassis now, such as after a brain swap.
        _idOwner.Stamp(card, ent, overwrite: true);
    }

    /// <summary>
    /// Issues a cyborg that spawned into a job its card in the chassis, since it has no tool module to carry one
    /// until it picks a type. The card takes the job's access, so a faction cyborg can buy its faction's ships.
    /// </summary>
    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!HasComp<BorgChassisComponent>(args.Mob) || _cards.TryGetCard(args.Mob, out _))
            return;

        _container.EnsureContainer<Container>(args.Mob, WFBorgIdCardSystem.ContainerId);
        if (!TrySpawnInContainer(IssuedCard, args.Mob, WFBorgIdCardSystem.ContainerId, out var card))
            return;

        if (args.JobId != null && _proto.TryIndex<JobPrototype>(args.JobId, out var job))
            _access.SetAccessToJob(card.Value, job, extended: false);

        EnsureRecord(args.Mob, card.Value);
        _idOwner.Stamp(card.Value, args.Mob);
    }

    /// <summary>A tool module carries the card the chassis was already issued, in place of its own blank one.</summary>
    private void OnModuleInstalled(Entity<DroppableBorgModuleComponent> ent, ref BorgModuleInstalledEvent args)
    {
        if (!_container.TryGetContainer(args.ChassisEnt, WFBorgIdCardSystem.ContainerId, out var own) || own.ContainedEntities.Count == 0
            || !_container.TryGetContainer(ent, ent.Comp.ContainerId, out var stowed))
            return;

        var issued = own.ContainedEntities[0];
        foreach (var blank in stowed.ContainedEntities)
        {
            if (!HasComp<WFBorgIdCardComponent>(blank) || !TryComp<HandPlaceholderRemoveableComponent>(blank, out var removeable))
                continue;

            _placeholder.SetPlaceholder(issued, removeable.Placeholder);
            _placeholder.SetEnabled(issued, false);
            _container.Insert(issued, stowed, force: true);
            // Deleted now: selecting the module hands out one stowed item per hand.
            Del(blank);
            return;
        }
    }

    // Covers a tool module fitted after the player took the chassis.
    private void OnCardEquipped(Entity<WFBorgIdCardComponent> ent, ref GotEquippedHandEvent args)
    {
        if (!HasComp<BorgChassisComponent>(args.User))
            return;

        EnsureRecord(args.User, ent);
        // Only a card nobody owns yet, so picking up another cyborg's card doesn't take it over.
        _idOwner.Stamp(ent, args.User);
    }

    /// <summary>
    /// Files the cyborg on the sector records service, as a crew spawn is, and keys a card that has no record yet
    /// to it.
    /// </summary>
    private void EnsureRecord(EntityUid borg, EntityUid card)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(card, out var storage) || storage.Key != null)
            return;

        var service = _sectorService.GetServiceEntity();
        if (!TryComp<StationRecordsComponent>(service, out var records) || !_proto.TryIndex(RecordJob, out var job))
            return;

        var name = Name(borg);
        // Always a record of its own: one shared by name would let a namesake's card open the same doors.
        var key = _records.AddRecordEntry(service, new GeneralStationRecord
        {
            Name = name,
            JobTitle = job.LocalizedName,
            JobIcon = job.Icon,
            JobPrototype = job.ID,
            DisplayPriority = job.RealDisplayWeight,
        }, records);

        if (!key.IsValid())
            return;

        _keyStorage.AssignKey(card, key, storage);
        _idCard.TryChangeFullName(card, name);
    }
}
