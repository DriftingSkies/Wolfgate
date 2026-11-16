using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.Storage.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.StationRecords;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.ShipCommand;

public sealed partial class WFShipCommandSystem
{
    /// <summary>How often the card keys behind each ship's zones are recomputed, so a player's new card or an admin's arrival takes effect.</summary>
    private static readonly TimeSpan ZoneKeyInterval = TimeSpan.FromSeconds(2);

    /// <summary>The keys each ship's zone doors were last written with.</summary>
    private readonly Dictionary<EntityUid, ZoneKeys> _zoneKeys = new();

    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private TimeSpan _zoneKeyTimer;

    /// <summary>Who a ship's Staff and Locked zones admit besides the owner, as card record keys.</summary>
    private sealed record ZoneKeys(HashSet<StationRecordKey> Staff, HashSet<StationRecordKey> Locked)
    {
        public bool SameAs(ZoneKeys other) => Staff.SetEquals(other.Staff) && Locked.SetEquals(other.Locked);
    }

    private void InitializeZones()
    {
        SubscribeLocalEvent<WFShipZonesComponent, MapInitEvent>(OnZonesMapInit);
        SubscribeLocalEvent<WFShipZonesComponent, ComponentShutdown>(OnZonesShutdown);
        SubscribeLocalEvent<WFShipZonesComponent, WFDoorZoneKeysEvent>(OnZoneDoorKeys);
        SubscribeLocalEvent<WFShipZonesComponent, WFCrewPlaceNameEvent>(OnPlaceName);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>A crewman's death or revival changes who his ship's zones admit at once, not at the next sweep.</summary>
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (TryComp<WFCrewComponent>(args.Target, out var crew) && _crew.HomeGrid(args.Target, crew) is { } home)
            RefreshZoneKeys(home);
    }

    /// <summary>The crew name the zone a trespasser stands in when they challenge him.</summary>
    private void OnPlaceName(Entity<WFShipZonesComponent> grid, ref WFCrewPlaceNameEvent args)
    {
        if (args.Name != null || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        var tile = _map.TileIndicesFor(grid, mapGrid, Transform(args.Visitor).Coordinates);
        if (TryGetZoneAt(grid.Comp, tile, out var zone))
            args.Name = Loc.GetString(zone.Name);
    }

    /// <summary>A spawned ship's doors take the rules of the modes its zones were saved with.</summary>
    private void OnZonesMapInit(Entity<WFShipZonesComponent> grid, ref MapInitEvent args)
    {
        foreach (var zone in grid.Comp.Zones)
            ApplyZone(grid.Owner, zone);
    }

    private void OnZonesShutdown(Entity<WFShipZonesComponent> grid, ref ComponentShutdown args)
    {
        _zoneKeys.Remove(grid.Owner);
    }

    /// <summary>Names who a Staff or Locked zone's door admits.</summary>
    private void OnZoneDoorKeys(Entity<WFShipZonesComponent> grid, ref WFDoorZoneKeysEvent args)
    {
        if (!TryGetZone(grid.Comp, args.Zone, out var zone))
            return;

        var keys = CurrentZoneKeys(grid.Owner);
        switch (zone.Mode)
        {
            case WFShipZoneMode.Staff:
                args.Keys.UnionWith(keys.Staff);
                break;
            case WFShipZoneMode.Locked:
                args.Keys.UnionWith(keys.Locked);
                break;
        }
    }

    /// <summary>
    /// Switches a zone. Public and Sealed give its doors those ShipAccess rules; Staff and Locked give them the Zone
    /// rule, whose keys this system names. False when there is no such zone or it is already in that mode.
    /// </summary>
    public bool SetZoneMode(Entity<WFShipZonesComponent?> grid, string zoneId, WFShipZoneMode mode, EntityUid? actor = null)
    {
        if (!Resolve(grid, ref grid.Comp, false)
            || !Enum.IsDefined(mode)
            || !TryGetZone(grid.Comp, zoneId, out var zone)
            || zone.Mode == mode)
            return false;

        var old = zone.Mode;
        zone.Mode = mode;
        Dirty(grid.Owner, grid.Comp);
        var doors = ApplyZone(grid.Owner, zone);

        if (actor is { } user)
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):actor} switched zone {zone.Id} on {ToPrettyString(grid.Owner):grid} from {old} to {mode} ({doors} doors)");
        }
        else
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"Zone {zone.Id} on {ToPrettyString(grid.Owner):grid} was switched from {old} to {mode} ({doors} doors)");
        }

        var ev = new WFShipZoneModeChangedEvent(zone.Id, old, mode);
        RaiseLocalEvent(grid.Owner, ref ev);
        return true;
    }

    /// <summary>The zone with this id, if the ship has one.</summary>
    public bool TryGetZone(WFShipZonesComponent comp, string? zoneId, [NotNullWhen(true)] out WFShipZone? zone)
    {
        zone = zoneId == null ? null : comp.Zones.FirstOrDefault(z => z.Id == zoneId);
        return zone != null;
    }

    /// <summary>The zone covering a grid tile, if any; the first listed wins where areas overlap.</summary>
    public bool TryGetZoneAt(WFShipZonesComponent comp, Vector2i tile, [NotNullWhen(true)] out WFShipZone? zone)
    {
        foreach (var candidate in comp.Zones)
        {
            foreach (var area in candidate.Areas)
            {
                if (tile.X >= area.Left && tile.X <= area.Right && tile.Y >= area.Bottom && tile.Y <= area.Top)
                {
                    zone = candidate;
                    return true;
                }
            }
        }

        zone = null;
        return false;
    }

    /// <summary>The doors on the grid that belong to a zone.</summary>
    public List<EntityUid> ZoneDoors(EntityUid grid, string zoneId)
    {
        var doors = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WFDoorAccessRuleComponent>(child, out var rule) && rule.Zone == zoneId)
                doors.Add(child);
        }

        return doors;
    }

    /// <summary>How many doors and lockers each zone holds, from one pass over the grid.</summary>
    public Dictionary<string, (int Doors, int Lockers)> ZoneCounts(EntityUid grid)
    {
        var counts = new Dictionary<string, (int Doors, int Lockers)>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<WFDoorAccessRuleComponent>(child, out var rule) || rule.Zone is not { } zone)
                continue;

            var (doors, lockers) = counts.GetValueOrDefault(zone);
            counts[zone] = HasComp<EntityStorageComponent>(child) ? (doors, lockers + 1) : (doors + 1, lockers);
        }

        return counts;
    }

    /// <summary>Gives a zone's doors the rule its mode calls for and rewrites their readers. Returns how many doors it has.</summary>
    private int ApplyZone(EntityUid grid, WFShipZone zone)
    {
        var rule = zone.Mode switch
        {
            WFShipZoneMode.Public => WFDoorAccessRule.Public,
            WFShipZoneMode.Sealed => WFDoorAccessRule.Sealed,
            _ => WFDoorAccessRule.Zone,
        };

        var doors = ZoneDoors(grid, zone.Id);
        _shipAccess.ApplyZoneRule(doors, rule);
        return doors.Count;
    }

    /// <summary>
    /// Recomputes who a ship's zones admit now and rewrites its Staff and Locked doors when that changed. Command
    /// list edits and admin power switches call it at once; the two-second check catches new cards and arriving admins.
    /// </summary>
    public void RefreshZoneKeys(EntityUid grid)
    {
        if (!TryComp<WFShipZonesComponent>(grid, out var zones))
            return;

        var keys = ComputeZoneKeys(grid);
        if (_zoneKeys.TryGetValue(grid, out var written) && written.SameAs(keys))
            return;

        _zoneKeys[grid] = keys;
        foreach (var zone in zones.Zones)
        {
            if (zone.Mode is WFShipZoneMode.Staff or WFShipZoneMode.Locked)
            {
                foreach (var door in ZoneDoors(grid, zone.Id))
                    _shipAccess.RefreshReader(door);
            }
        }
    }

    private void PeopleChanged(EntityUid grid)
    {
        RefreshZoneKeys(grid);
    }

    private void UpdateZoneKeys(float frameTime)
    {
        _zoneKeyTimer += TimeSpan.FromSeconds(frameTime);
        if (_zoneKeyTimer < ZoneKeyInterval)
            return;

        _zoneKeyTimer = TimeSpan.Zero;
        var query = EntityQueryEnumerator<WFShipZonesComponent>();
        while (query.MoveNext(out var grid, out _))
            RefreshZoneKeys(grid);
    }

    private ZoneKeys CurrentZoneKeys(EntityUid grid)
    {
        if (!_zoneKeys.TryGetValue(grid, out var keys))
        {
            keys = ComputeZoneKeys(grid);
            _zoneKeys[grid] = keys;
        }

        return keys;
    }

    /// <summary>
    /// Staff zones admit officers, the staff list and admins; Locked zones officers and admins. The owner's keys
    /// are ShipAccess's. The barred list wins over the staff and officer lists, but not over the admins.
    /// </summary>
    private ZoneKeys ComputeZoneKeys(EntityUid grid)
    {
        var staff = new HashSet<StationRecordKey>();
        var locked = new HashSet<StationRecordKey>();
        if (!TryComp<WFShipCommandComponent>(grid, out var command))
            return new ZoneKeys(staff, locked);

        AddKeys(command.Officers, locked);
        var barred = new HashSet<StationRecordKey>();
        AddKeys(command.Barred, barred);
        locked.ExceptWith(barred);

        staff.UnionWith(locked);
        AddKeys(command.Staff, staff);
        staff.ExceptWith(barred);

        // The ship's own living NPC crew walk its Staff and Locked zones with the cards they were issued.
        var crew = EntityQueryEnumerator<WFCrewComponent>();
        while (crew.MoveNext(out var mob, out var member))
        {
            if (_crew.HomeGrid(mob, member) == grid && !_mobState.IsDead(mob)
                && _access.TryGetCard(mob, out var card) && _access.TryGetKey(card, out var crewKey))
            {
                var record = _access.ToRecordKey(crewKey);
                staff.Add(record);
                locked.Add(record);
            }
        }

        if (command.AdminsInZones)
        {
            foreach (var session in _player.Sessions)
            {
                if (CountsAsAdmin(_admin.GetAdminData(session), command.EventStaffAreAdmins) && TryGetWornKey(session, out var key))
                {
                    staff.Add(key.Value);
                    locked.Add(key.Value);
                }
            }
        }

        return new ZoneKeys(staff, locked);
    }
}
