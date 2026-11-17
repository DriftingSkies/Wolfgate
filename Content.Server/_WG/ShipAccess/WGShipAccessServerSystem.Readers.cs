using System.Linq;
using Content.Server.Storage.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Lock;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAccess;

public sealed partial class WGShipAccessServerSystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private EmagSystem _emag = default!;

    /// <summary>
    /// An access level no ID card can carry. A reader that requires it admits only its record keys, which is how a
    /// locked ship shuts out everyone the deed and the allow list don't name.
    /// </summary>
    public static readonly ProtoId<AccessLevelPrototype> LockedAccess = "WGShipLocked";

    /// <summary>How often each ship's owner keys are recomputed, so a moved deed or a registered player's new card takes effect.</summary>
    private static readonly TimeSpan OwnerCheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>Doors and lockers whose readers are rewritten next tick, once a new door's electronics are in.</summary>
    private readonly HashSet<EntityUid> _pendingReaders = new();

    /// <summary>The owner keys each ship's readers were last written with.</summary>
    private readonly Dictionary<EntityUid, HashSet<StationRecordKey>> _writtenOwners = new();

    private TimeSpan _ownerCheckTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingReaders.Count > 0)
        {
            var pending = new List<EntityUid>(_pendingReaders);
            _pendingReaders.Clear();
            // A ship loaded or built in one go queues many readers; its owner keys are looked up once.
            var owners = new Dictionary<EntityUid, HashSet<StationRecordKey>>();
            foreach (var uid in pending)
            {
                if (!TerminatingOrDeleted(uid))
                    RefreshReader(uid, owners);
            }
        }

        _ownerCheckTimer += TimeSpan.FromSeconds(frameTime);
        if (_ownerCheckTimer < OwnerCheckInterval)
            return;

        _ownerCheckTimer = TimeSpan.Zero;
        CheckOwners();
    }

    /// <summary>Rewrites a ship's readers when its owner keys changed since they were last written, and locks a ship that was waiting for one.</summary>
    private void CheckOwners()
    {
        foreach (var grid in _writtenOwners.Keys.ToList())
        {
            if (!TryComp<WGShipAccessComponent>(grid, out var access))
            {
                _writtenOwners.Remove(grid);
                _lockWhenKeyed.Remove(grid);
                continue;
            }

            if (TryLockWhenKeyed((grid, access)))
                continue;

            if (!OwnerKeys((grid, access)).SetEquals(_writtenOwners[grid]))
                RefreshShip((grid, access));
        }
    }

    /// <summary>Rewrites the reader of a door or locker next tick, after a build or a move onto a ship.</summary>
    private void QueueReader(EntityUid uid)
    {
        if (!TerminatingOrDeleted(uid))
            _pendingReaders.Add(uid);
    }

    /// <summary>Rewrites every door and locker reader on the ship from its lock, list, faction and door rules.</summary>
    public void RefreshShip(Entity<WGShipAccessComponent> ship)
    {
        var owners = OwnerKeys(ship);
        _writtenOwners[ship.Owner] = owners;
        PublishOwnerKeys(ship, owners);
        var children = Transform(ship.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
            RefreshReader(ship, child, owners);
    }

    /// <summary>
    /// Rewrites one door or locker's reader for the ship it now sits on. Off a ship with access control, a reader
    /// the ship had taken over gets its own access back.
    /// </summary>
    public void RefreshReader(EntityUid uid)
    {
        RefreshReader(uid, null);
    }

    private void RefreshReader(EntityUid uid, Dictionary<EntityUid, HashSet<StationRecordKey>>? ownerCache)
    {
        if (Transform(uid).GridUid is { } grid && TryComp<WGShipAccessComponent>(grid, out var access))
        {
            if (ownerCache == null || !ownerCache.TryGetValue(grid, out var owners))
            {
                owners = OwnerKeys((grid, access));
                if (ownerCache != null)
                    ownerCache[grid] = owners;
            }

            RefreshReader((grid, access), uid, owners);
            return;
        }

        if (_accessReader.GetMainAccessReader(uid, out var reader) && TryComp<WGShipReaderBackupComponent>(reader.Value, out var backup) && !backup.Released)
            RestoreReader(reader.Value);
    }

    /// <summary>
    /// Sets a door or locker's main access reader (the door electronics, for an airlock). An unlocked ship's
    /// door without a rule keeps its own access, with the crew's record keys added when it asks for any; anything
    /// else requires <see cref="LockedAccess"/> plus the record keys its rule admits, so the door decides, denies,
    /// is hacked and emagged exactly as an airlock is.
    /// </summary>
    private void RefreshReader(Entity<WGShipAccessComponent> ship, EntityUid uid, HashSet<StationRecordKey> owners)
    {
        // Mono's deed reader stays off on anything aboard a ship this module runs; the airlock reader decides alone.
        if (TryComp<ShipAccessReaderComponent>(uid, out var mono) && mono.Enabled)
        {
            mono.Enabled = false;
            Dirty(uid, mono);
        }

        if (!IsShipReader(uid) || _emag.CheckFlag(uid, EmagType.Access) || !_accessReader.GetMainAccessReader(uid, out var found))
            return;

        var reader = found.Value;
        TryComp<WGShipReaderBackupComponent>(reader, out var backup);
        if (backup != null && backup.Released)
            return;

        // Someone else rewrote a reader the ship had locked, an emag or an access configurator: it keeps that change.
        if (backup != null && backup.Locked && !HasLockedAccess(reader))
        {
            backup.Released = true;
            return;
        }

        if (backup == null && IsForeignStorage(uid, reader))
            return;

        TryComp<WGDoorAccessRuleComponent>(uid, out var rule);
        var ruleKind = rule?.Rule ?? WGDoorAccessRule.Default;

        // On a public ship every door that follows the ship admits anyone. Lockers and doors with a rule keep theirs.
        if (ruleKind == WGDoorAccessRule.Default && ship.Comp.Mode == WGShipAccessMode.Public && IsRuledDoor(uid))
            ruleKind = WGDoorAccessRule.Public;

        var lists = reader.Comp.AccessLists;
        var keys = reader.Comp.AccessKeys;

        if (ruleKind == WGDoorAccessRule.Default && !ship.Comp.Locked)
        {
            // A reader that asks for nothing admits anyone; one that asks for access also admits the crew.
            if (backup != null)
                KeepOwnLists(reader, backup);

            if ((backup?.Access.Count ?? lists.Count(set => !set.Contains(LockedAccess))) == 0)
            {
                RestoreReader(reader);
                return;
            }

            backup ??= BackUpReader(reader);
            lists.Clear();
            foreach (var set in backup.Access)
                lists.Add(new HashSet<ProtoId<AccessLevelPrototype>>(set));

            keys.Clear();
            keys.UnionWith(backup.Keys);
            keys.UnionWith(owners);
            keys.Add(WGShipAiAccessSystem.AiKey(ship.Owner));
            foreach (var entry in ship.Comp.AllowList)
                keys.Add(_access.ToRecordKey(entry.Key));

            if (ship.Comp.Mode == WGShipAccessMode.Crew)
            {
                foreach (var key in ship.Comp.CrewKeys)
                    keys.Add(_access.ToRecordKey(key));
            }
            if (ship.Comp.Mode == WGShipAccessMode.Company)
            {
                foreach (var key in ship.Comp.CompanyKeys)
                    keys.Add(_access.ToRecordKey(key));
            }
            backup.Locked = false;
            backup.OwnLists = true;
            CommitReader(reader);
            return;
        }

        if (backup != null)
            KeepOwnLists(reader, backup);

        backup ??= BackUpReader(reader);
        backup.OwnLists = false;
        lists.Clear();
        keys.Clear();

        // Public keeps both empty: a reader with no lists admits anyone.
        if (ruleKind != WGDoorAccessRule.Public)
            lists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { LockedAccess });

        switch (ruleKind)
        {
            case WGDoorAccessRule.Public:
            case WGDoorAccessRule.Sealed:
                break;
            case WGDoorAccessRule.OwnerOnly:
            case WGDoorAccessRule.Code:
                keys.UnionWith(owners);
                break;
            case WGDoorAccessRule.Crew:
                keys.UnionWith(owners);
                foreach (var key in ship.Comp.CrewKeys)
                    keys.Add(_access.ToRecordKey(key));
                break;
            case WGDoorAccessRule.Company:
                keys.UnionWith(owners);
                foreach (var key in ship.Comp.CompanyKeys)
                    keys.Add(_access.ToRecordKey(key));
                break;
            case WGDoorAccessRule.Players:
            case WGDoorAccessRule.PlayersOrCode:
                keys.UnionWith(owners);
                foreach (var key in rule!.Players)
                    keys.Add(_access.ToRecordKey(key));
                break;
            case WGDoorAccessRule.Zone:
            {
                // The zone's owner names who it admits now; with nobody answering, only the owner.
                keys.UnionWith(owners);
                var zoneKeys = new WGDoorZoneKeysEvent(uid, rule?.Zone, keys);
                RaiseLocalEvent(ship.Owner, ref zoneKeys);
                break;
            }
            default:
                keys.UnionWith(owners);
                foreach (var entry in ship.Comp.AllowList)
                    keys.Add(_access.ToRecordKey(entry.Key));

                if (ship.Comp.Mode == WGShipAccessMode.Crew)
                {
                    foreach (var key in ship.Comp.CrewKeys)
                        keys.Add(_access.ToRecordKey(key));
                }
                if (ship.Comp.Mode == WGShipAccessMode.Company)
                {
                    foreach (var key in ship.Comp.CompanyKeys)
                        keys.Add(_access.ToRecordKey(key));
                }

                // Any one of the company's access levels is enough, each on its own list.
                if (ship.Comp.Mode == WGShipAccessMode.Faction && _access.IsFactionGrid(ship, out var company))
                {
                    foreach (var tag in company)
                        lists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { tag });
                }
                break;
        }

        // The ship's own AI opens any door that admits someone by card.
        if (ruleKind is not (WGDoorAccessRule.Public or WGDoorAccessRule.Sealed))
            keys.Add(WGShipAiAccessSystem.AiKey(ship.Owner));

        backup.Locked = ruleKind != WGDoorAccessRule.Public;
        CommitReader(reader);
    }

    /// <summary>Doors (not firelocks), lockers and lockable buttons: what ship access covers.</summary>
    private bool IsShipReader(EntityUid uid)
    {
        return IsRuledDoor(uid) || HasComp<EntityStorageComponent>(uid) || IsLockableSwitch(uid) || IsLockedReactor(uid) || IsLockedTurbine(uid);
    }

    /// <summary>A gas turbine with a lock, which nobody aboard could open while it asked for Captain access alone.</summary>
    private bool IsLockedTurbine(EntityUid uid)
    {
        return HasComp<TurbineComponent>(uid) && HasComp<LockComponent>(uid);
    }

    /// <summary>A door that can take a rule of its own. Firelocks answer to the atmosphere, not the owner.</summary>
    private bool IsRuledDoor(EntityUid uid)
    {
        return HasComp<DoorComponent>(uid) && !HasComp<FirelockComponent>(uid);
    }

    /// <summary>A nuclear reactor with a lock, so the ship's owner and crew can unlock its controls.</summary>
    private bool IsLockedReactor(EntityUid uid)
    {
        return HasComp<NuclearReactorComponent>(uid) && HasComp<LockComponent>(uid);
    }

    /// <summary>A button or switch with a lock, whose reader decides who may unlock it.</summary>
    private bool IsLockableSwitch(EntityUid uid)
    {
        return HasComp<SignalSwitchComponent>(uid) && HasComp<LockComponent>(uid);
    }

    /// <summary>
    /// Storage brought aboard that asks for access of its own. Taking it over would hand its owner's locker to the
    /// ship's owner, so it keeps its access; storage that came with the ship, or asks for none, is taken over.
    /// </summary>
    private bool IsForeignStorage(EntityUid uid, Entity<AccessReaderComponent> reader)
    {
        return HasComp<EntityStorageComponent>(uid)
            && !HasComp<WGShipStorageComponent>(uid)
            && (reader.Comp.AccessLists.Count > 0 || reader.Comp.AccessKeys.Count > 0);
    }

    /// <summary>Marks the lockers and crates on a ship as its own, when ship access first takes the ship over.</summary>
    private void MarkShipStorage(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<EntityStorageComponent>(child))
                EnsureComp<WGShipStorageComponent>(child);
        }
    }

    private static bool HasLockedAccess(Entity<AccessReaderComponent> reader)
    {
        foreach (var set in reader.Comp.AccessLists)
        {
            if (set.Contains(LockedAccess))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps the reader's own access the first time ship access takes it over. A reader that already wants the
    /// locked-ship access carries a stale lock from another ship; that part is dropped rather than kept as its own.
    /// </summary>
    private WGShipReaderBackupComponent BackUpReader(Entity<AccessReaderComponent> reader)
    {
        var backup = EnsureComp<WGShipReaderBackupComponent>(reader);
        var stale = HasLockedAccess(reader);
        foreach (var set in reader.Comp.AccessLists)
        {
            if (!set.Contains(LockedAccess))
                backup.Access.Add(new HashSet<ProtoId<AccessLevelPrototype>>(set));
        }

        if (!stale)
            backup.Keys.UnionWith(reader.Comp.AccessKeys);

        return backup;
    }

    /// <summary>
    /// While a reader holds its own lists with the crew's keys added, the lists are its own to change: whatever an
    /// access configurator or new electronics left there replaces the backup.
    /// </summary>
    private static void KeepOwnLists(Entity<AccessReaderComponent> reader, WGShipReaderBackupComponent backup)
    {
        if (!backup.OwnLists)
            return;

        backup.Access.Clear();
        foreach (var set in reader.Comp.AccessLists)
        {
            if (!set.Contains(LockedAccess))
                backup.Access.Add(new HashSet<ProtoId<AccessLevelPrototype>>(set));
        }
    }

    /// <summary>Puts a reader's own access back and forgets the backup; a reader left locked with no backup is cleared.</summary>
    private void RestoreReader(Entity<AccessReaderComponent> reader)
    {
        if (!TryComp<WGShipReaderBackupComponent>(reader, out var backup))
        {
            if (!HasLockedAccess(reader))
                return;

            reader.Comp.AccessLists.RemoveAll(set => set.Contains(LockedAccess));
            reader.Comp.AccessKeys.Clear();
            CommitReader(reader);
            return;
        }

        KeepOwnLists(reader, backup);
        reader.Comp.AccessLists.Clear();
        foreach (var set in backup.Access)
            reader.Comp.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>>(set));

        reader.Comp.AccessKeys.Clear();
        reader.Comp.AccessKeys.UnionWith(backup.Keys);
        RemComp<WGShipReaderBackupComponent>(reader);
        CommitReader(reader);
    }

    private void CommitReader(Entity<AccessReaderComponent> reader)
    {
        Dirty(reader);
        RaiseLocalEvent(reader.Owner, new AccessReaderConfigurationChangedEvent());
    }

    /// <summary>
    /// Record keys that open the ship as its owner: those of the ID cards and vouchers holding its deed, and of the
    /// card each player it is registered to wears right now. While no ID card holding the deed has a crew record (a
    /// voucher purchase), the buyer's own card stands in.
    /// </summary>
    private HashSet<StationRecordKey> OwnerKeys(Entity<WGShipAccessComponent> ship)
    {
        var keys = new HashSet<StationRecordKey>();
        var cardKey = false;
        var query = EntityQueryEnumerator<ShuttleDeedComponent, StationRecordKeyStorageComponent>();
        while (query.MoveNext(out var uid, out var deed, out var storage))
        {
            if (deed.ShuttleUid != ship.Owner || storage.Key is not { } key)
                continue;

            keys.Add(key);
            cardKey |= HasComp<IdCardComponent>(uid);
        }

        if (!cardKey && ship.Comp.BuyerKey is { } buyer)
            keys.Add(_access.ToRecordKey(buyer));

        foreach (var user in ship.Comp.OwnerUsers)
        {
            if (_player.TryGetSessionById(user, out var session)
                && session.AttachedEntity is { } body
                && _access.TryGetWornCard(body, out var card)
                && TryComp<StationRecordKeyStorageComponent>(card, out var worn)
                && worn.Key is { } wornKey)
            {
                keys.Add(wornKey);
            }
        }

        return keys;
    }

    /// <summary>Copies the owner keys onto the component, where the client and <see cref="WGShipAccessSystem.IsOwner"/> read them.</summary>
    private void PublishOwnerKeys(Entity<WGShipAccessComponent> ship, HashSet<StationRecordKey> owners)
    {
        var keys = new List<WGShipAccessKey>();
        foreach (var key in owners)
            keys.Add(_access.ToKey(key));

        if (keys.Count == ship.Comp.OwnerKeys.Count && keys.TrueForAll(ship.Comp.OwnerKeys.Contains))
            return;

        ship.Comp.OwnerKeys = keys;
        Dirty(ship);
    }

    /// <summary>Whether the ship can be locked without shutting its owner out: someone's card must carry an owner key.</summary>
    public bool CanLock(Entity<WGShipAccessComponent> ship)
    {
        return OwnerKeys(ship).Count > 0;
    }
}
