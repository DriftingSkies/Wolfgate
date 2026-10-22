using System.Linq;
using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.StationRecords;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.Access.Components;
using Content.Shared.Forensics.Components;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Shuttles.Components;
using Content.Shared.StationRecords;
using Content.Shared.Tag;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Registers an already-spawned ship to an owner the way a shipyard purchase would. Lives in the
/// shipyard partial because the deed and lock components are access-restricted to it.
/// </summary>
public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Runs every post-payment step of a shipyard purchase on an existing ship: company, late-join station,
    /// FTL lock, deeds, console locks, ownership, station and shuttle records, ship access, lifecycle,
    /// vessel tags and crew requirement. Skipped on purpose: payment, vouchers, purchase-attempt checks,
    /// ID access levels and job title, and the shipyard console messages.
    /// </summary>
    /// <param name="existingStation">A caller-owned station that already contains the owner record.</param>
    /// <param name="vessel">Design the ship was built from, if it is still known.</param>
    /// <param name="shipName">Name to keep, for a ship that already had one. Null takes the design's.</param>
    public bool TryAssignDeed(EntityUid shuttleUid, EntityUid idCard, ICommonSession owner, VesselPrototype? vessel, string? shipName = null, EntityUid? existingStation = null)
    {
        if (!HasComp<ShuttleComponent>(shuttleUid) || !TryComp<IdCardComponent>(idCard, out var card))
            return false;

        var ownerEntity = owner.AttachedEntity is { Valid: true } attached ? attached : (EntityUid?)null;
        var ownerName = ownerEntity != null ? Name(ownerEntity.Value).Trim() : owner.Name;
        var name = vessel?.Name ?? Name(shuttleUid);

        if (!string.IsNullOrEmpty(card.CompanyName))
        {
            var company = EnsureComp<CompanyComponent>(shuttleUid);
            company.CompanyName = card.CompanyName;
            Dirty(shuttleUid, company);
        }

        // Ships with a matching game map get a station so players can late-join onto them. An admin-spawned ship
        // may already have one from its spawn; it is reused rather than doubled, keeping any name it was given.
        EntityUid? shuttleStation = existingStation;
        if (shuttleStation == null && vessel != null)
        {
            if (_station.GetOwningStation(shuttleUid) is { } spawnStation
                && TryComp<ExtraShuttleInformationComponent>(spawnStation, out var info)
                && info.Vessel == vessel.ID)
            {
                shuttleStation = spawnStation;
                name = Name(spawnStation);
            }
            else if (TryCreateVesselStation(shuttleUid, vessel) is { } created)
            {
                shuttleStation = created;
                name = Name(created);
            }
        }

        // A ship that is changing hands rather than rolling off the line keeps the name it had.
        if (shipName != null)
            name = shipName;

        EnsureComp<FTLLockComponent>(shuttleUid);
        EntityManager.System<ShuttleConsoleSystem>().ToggleFTLLock(shuttleUid, new List<NetEntity>(), true);

        var deedId = EnsureComp<ShuttleDeedComponent>(idCard);
        AssignShuttleDeedProperties(deedId, shuttleUid, name, ownerName, false);
        deedId.DeedHolder = idCard;
        Dirty(idCard, deedId);

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(shuttleUid);
        AssignShuttleDeedProperties(deedShuttle, shuttleUid, name, ownerName, false);
        Dirty(shuttleUid, deedShuttle);

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var consoleUid, out _, out var xform))
        {
            if (xform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }

        _shipOwnership.RegisterShipOwnership(shuttleUid, owner);

        if (shuttleStation != null && existingStation == null)
            MoveOwnerRecord(shuttleStation.Value, idCard, ownerEntity, owner);

        AddShipAccessToEntities(shuttleUid);
        EnsureComp<LinkedLifecycleGridParentComponent>(shuttleUid);

        if (vessel != null)
        {
            EnsureComp<VesselComponent>(shuttleUid).VesselId = vessel.ID;
            EnsureComp<TagComponent>(shuttleUid);
            _tagSystem.TryAddTags(shuttleUid, vessel.Tags);
            if (vessel.RequireCrew || vessel.Classes.Contains(VesselClass.Capital) || _tagSystem.HasTag(shuttleUid, CrewedShuttleTag))
                EnsureComp<CrewedShuttleComponent>(shuttleUid);
        }

        _shuttleRecordsSystem.AddRecord(new ShuttleRecord(
            name: deedShuttle.ShuttleName ?? string.Empty,
            suffix: deedShuttle.ShuttleNameSuffix ?? string.Empty,
            ownerName: ownerName,
            entityUid: GetNetEntity(shuttleUid),
            purchasedWithVoucher: false,
            purchasePrice: (uint)(vessel?.Price ?? 0)));

        if (ownerEntity != null)
        {
            var purchaseEv = new ShipyardShuttlePurchaseEvent(shuttleUid, ownerEntity.Value);
            RaiseLocalEvent(purchaseEv);
        }

        var fullName = GetFullName(deedShuttle);
        _metaData.SetEntityName(shuttleUid, fullName);
        if (shuttleStation != null)
        {
            _station.RenameStation(shuttleStation.Value, fullName, loud: false);
            _metaData.SetEntityName(shuttleStation.Value, fullName);
        }

        return true;
    }

    /// <summary>
    /// Makes a late-join station, with the design's job slots, for a ship whose design has a matching game map.
    /// </summary>
    /// <returns>The new station, or null when the design has no game map.</returns>
    public EntityUid? TryCreateVesselStation(EntityUid shuttleUid, VesselPrototype vessel)
    {
        if (!_prototypeManager.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto)
            || !stationProto.Stations.TryGetValue(vessel.ID, out var stationConfig))
        {
            return null;
        }

        var station = _station.InitializeNewStation(stationConfig, new List<EntityUid> { shuttleUid });
        EnsureComp<ExtraShuttleInformationComponent>(station).Vessel = vessel.ID;
        return station;
    }

    /// <summary>
    /// Renames a ship: its deeds and shuttle record when it has them, otherwise the grid and its station.
    /// </summary>
    public bool TryRenameVessel(EntityUid shuttleUid, string newName)
    {
        var station = _station.GetOwningStation(shuttleUid);

        // TryRenameShuttle needs the ship's station, so a deeded ship without one only renames its grid.
        if (station != null && TryComp<ShuttleDeedComponent>(shuttleUid, out var deed) && deed.ShuttleUid == shuttleUid)
        {
            var suffix = deed.ShuttleNameSuffix;
            return TryRenameShuttle(shuttleUid, deed, newName, suffix);
        }

        _metaData.SetEntityName(shuttleUid, newName);
        if (station != null)
        {
            _station.RenameStation(station.Value, newName, loud: false);
            _metaData.SetEntityName(station.Value, newName);
        }

        return true;
    }

    /// <summary>
    /// Copies the owner's general station record onto the ship's station, or creates a Captain record
    /// from their profile. Same behaviour as a purchase, guarded so ghosts and admin bodies can't crash it.
    /// </summary>
    private void MoveOwnerRecord(EntityUid shuttleStation, EntityUid idCard, EntityUid? ownerEntity, ICommonSession owner)
    {
        var copied = false;
        if (TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage)
            && keyStorage.Key != null
            && _records.TryGetRecord<GeneralStationRecord>(keyStorage.Key.Value, out var record))
        {
            _records.AddRecordEntry(shuttleStation, record);
            copied = true;
        }

        if (!copied
            && ownerEntity != null
            && _prefManager.GetPreferencesOrNull(owner.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile
            && TryComp<FingerprintComponent>(ownerEntity, out var fingerprint)
            && TryComp<DnaComponent>(ownerEntity, out var dna)
            && TryComp<StationRecordsComponent>(shuttleStation, out var stationRecords))
        {
            _records.CreateGeneralRecord(shuttleStation, idCard, profile.Name, profile.Age, profile.Species, profile.Gender,
                Loc.GetString("job-name-captain"), fingerprint.Fingerprint, dna.DNA, profile, stationRecords);
        }

        _records.Synchronize(shuttleStation);
    }
}
