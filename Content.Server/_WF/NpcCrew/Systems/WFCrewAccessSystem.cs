using Content.Server.Shuttles.Components;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Station.Components;
using Content.Shared.Tag;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Issues ordinary crew ID cards and enrolls them on the ship where the crew is spawned.</summary>
public sealed partial class WFCrewAccessSystem : EntitySystem
{
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _id = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private WFShipAccessServerSystem _ships = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private StationRecordKeyStorageSystem _keys = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    [Dependency] private TagSystem _tags = default!;

    private static readonly EntProtoId CrewCard = "PassengerIDCard";
    private static readonly ProtoId<TagPrototype> DoorBumpTag = "DoorBumpOpener";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewComponent, EntityTerminatingEvent>(OnCrewTerminating);
    }

    /// <summary>
    /// Registers a newly spawned crewman's worn card on its spawn ship. A ship that does not manage access yet is set
    /// up first; stations and other non-ship grids are left alone, so the mapper's own door access stays in force.
    /// </summary>
    public void RegisterSpawnShip(EntityUid uid)
    {
        if (Transform(uid).GridUid is not { } grid)
            return;

        Entity<WFShipAccessComponent> ship;
        if (TryComp<WFShipAccessComponent>(grid, out var managed))
            ship = (grid, managed);
        else if (HasComp<ShuttleComponent>(grid) && !HasComp<StationMemberComponent>(grid))
            ship = _ships.SetupShip(grid, null);
        else
            return;

        if (!_access.TryGetWornCard(uid, out var card))
        {
            card = Spawn(CrewCard, Transform(uid).Coordinates);
            if (!_inventory.TryEquip(uid, card, "id", silent: true, force: true))
            {
                Del(card);
                return;
            }
        }

        // The card names him and his post; a loadout's faction card comes blank.
        var title = TryComp<WFCrewComponent>(uid, out var member) ? _crew.Title((uid, member)) : null;
        var name = Name(uid);
        if (title != null && name.StartsWith(title + " ", StringComparison.Ordinal))
            name = name[(title.Length + 1)..];
        Label(uid, name, title, card);

        if (!_access.TryGetKey(card, out _))
        {
            var records = EnsureComp<StationRecordsComponent>(grid);
            var key = _records.AddRecordEntry(grid, new GeneralStationRecord { Name = Name(uid) }, records);
            if (!key.IsValid())
                return;
            var storage = EnsureComp<StationRecordKeyStorageComponent>(card);
            _keys.AssignKey(card, key, storage);
        }

        if (_ships.TryAddCard(ship, card, Name(uid)) && TryComp<WFCrewComponent>(uid, out var crew)
            && _access.TryGetKey(card, out var enrolled))
        {
            crew.AccessShip = grid;
            crew.AccessKey = enrolled;
        }
    }

    /// <summary>Writes a crewman's name and post on the card he wears.</summary>
    public void Label(EntityUid uid, string name, string? title, EntityUid? card = null)
    {
        EntityUid id;
        if (card is { } given)
            id = given;
        else if (!_access.TryGetWornCard(uid, out id))
            return;

        _id.TryChangeFullName(id, name);
        if (title != null)
            _id.TryChangeJobTitle(id, title);
    }

    /// <summary>
    /// A downed crewman no longer bumps doors open, so nobody drags his body through them; a dead one's card also comes
    /// off his ship's allow list, so it opens nothing looted. Both come back if he is revived.
    /// </summary>
    public void SetDown(EntityUid uid, WFCrewComponent crew, MobState state)
    {
        if (state == MobState.Alive)
            _tags.AddTag(uid, DoorBumpTag);
        else
            _tags.RemoveTag(uid, DoorBumpTag);

        if (crew.AccessShip is not { } grid || TerminatingOrDeleted(grid)
            || !TryComp<WFShipAccessComponent>(grid, out var ship))
            return;

        // The stored key is held off the list even if the card was taken off him while he was down.
        if (state == MobState.Dead && crew.AccessKey is { } enrolled && crew.AccessHold == null)
            crew.AccessHold = _ships.SuspendEntry((grid, ship), enrolled);
        else if (state == MobState.Alive && _access.TryGetWornCard(uid, out var card) && _access.TryGetKey(card, out var key))
            Reenroll(uid, crew, (grid, ship), card, key);
    }

    /// <summary>A revived crewman's worn card goes back on the list with its door lists; a card he no longer wears comes off.</summary>
    private void Reenroll(EntityUid uid, WFCrewComponent crew, Entity<WFShipAccessComponent> ship, EntityUid card, WFShipAccessKey key)
    {
        if (crew.AccessKey is { } old && old != key && crew.AccessHold == null)
            _ships.RemoveEntry(ship, old);

        if (crew.AccessHold is { } hold && hold.Entry.Key == key)
            _ships.RestoreEntry(ship, hold);
        else
            _ships.TryAddCard(ship, card, Name(uid));

        crew.AccessKey = key;
        crew.AccessHold = null;
    }

    /// <summary>A deleted crewman's card key comes off the ship's allow list.</summary>
    private void OnCrewTerminating(Entity<WFCrewComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.AccessShip is not { } grid || TerminatingOrDeleted(grid)
            || !TryComp<WFShipAccessComponent>(grid, out var ship) || ent.Comp.AccessKey is not { } key)
            return;

        _ships.RemoveEntry((grid, ship), key);
    }
}
