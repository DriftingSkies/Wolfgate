using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server._WG.ShipAccess;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.StationRecords;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// Owner control for large admin vessels: who stands where with the ship (<see cref="WGShipAuthority"/>), its
/// command lists, and its zones, whose switches give the zone's doors their access through ShipAccess.
/// </summary>
public sealed partial class WGShipCommandSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WGShipAccessSystem _access = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeZones();
        InitializeCrew();
        InitializeWires();
        SubscribeLocalEvent<WGShipCommandComponent, WGShipManagerEvent>(OnShipManager);
    }

    /// <summary>Whoever configures the ship, admins included, also manages its access tab and console locks.</summary>
    private void OnShipManager(Entity<WGShipCommandComponent> grid, ref WGShipManagerEvent args)
    {
        if (!args.Allowed && CanConfigure(args.User, grid))
            args.Allowed = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateZoneKeys(frameTime);
    }

    /// <summary>Where someone stands with a ship. A grid without command lists has only its owner and guests.</summary>
    public WGShipAuthority GetAuthority(EntityUid user, EntityUid grid)
    {
        return GetAuthority(user, grid, out _);
    }

    /// <summary>
    /// Where someone stands with a ship, and in <paramref name="listed"/> where the command lists alone put them (guest
    /// on none). An admin falls back to their listed standing wherever the owner turned that admin power off.
    /// </summary>
    public WGShipAuthority GetAuthority(EntityUid user, EntityUid grid, out WGShipAuthority listed)
    {
        listed = WGShipAuthority.Guest;
        if (_access.IsOwner(user, grid))
            return WGShipAuthority.Owner;

        if (!TryComp<WGShipCommandComponent>(grid, out var command))
            return WGShipAuthority.Guest;

        _player.TryGetSessionByEntity(user, out var session);
        WGShipAccessKey? key = _access.TryGetCard(user, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        var userId = session?.UserId;
        if (Listed(command.Barred, key, userId))
            listed = WGShipAuthority.Barred;
        else if (Listed(command.Officers, key, userId))
            listed = WGShipAuthority.Officer;
        else if (Listed(command.Staff, key, userId))
            listed = WGShipAuthority.Staff;

        return session != null && CountsAsAdmin(_admin.GetAdminData(session), command.EventStaffAreAdmins)
            ? WGShipAuthority.Admin
            : listed;
    }

    /// <summary>Changes the ship's settings: the owner, and admins unless the owner turned that off.</summary>
    public bool CanConfigure(EntityUid user, EntityUid grid)
    {
        return GetAuthority(user, grid) switch
        {
            WGShipAuthority.Owner => true,
            WGShipAuthority.Admin => Comp<WGShipCommandComponent>(grid).AdminsAtConsole,
            _ => false,
        };
    }

    /// <summary>
    /// Operates the ship without changing its settings: whoever configures it, and its officers, an admin on the
    /// officers list among them whatever the console switch says.
    /// </summary>
    public bool CanOperate(EntityUid user, EntityUid grid)
    {
        GetAuthority(user, grid, out var listed);
        return listed == WGShipAuthority.Officer || CanConfigure(user, grid);
    }

    /// <summary>
    /// Whether admin data counts as an admin for a ship: an active admin, or active event staff when the ship counts
    /// them. De-adminned admins count as players.
    /// </summary>
    public static bool CountsAsAdmin(AdminData? data, bool eventStaffAreAdmins)
    {
        return data != null
               && (data.HasFlag(AdminFlags.Admin) || eventStaffAreAdmins && data.HasFlag(AdminFlags.Event));
    }

    /// <summary>
    /// Puts someone on a command list by their card's key, their player, or both, taking them off the other two lists,
    /// since each person has one standing. False when neither is given or nothing changed.
    /// </summary>
    public bool AddPerson(Entity<WGShipCommandComponent> grid, WGShipCommandList list, WGShipAccessKey? key, NetUserId? user, string name)
    {
        if (key == null && user == null)
            return false;

        var target = List(grid.Comp, list);
        if (target.Exists(p => Matches(p, key, user)))
            return false;

        foreach (var other in Enum.GetValues<WGShipCommandList>())
        {
            if (other != list)
                List(grid.Comp, other).RemoveAll(p => Matches(p, key, user));
        }

        target.Add(new WGShipCommandPerson { Key = key, User = user, Name = name });
        Dirty(grid);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{name} was added to the {list} of {ToPrettyString(grid.Owner):grid}");
        PeopleChanged(grid.Owner);
        return true;
    }

    /// <summary>Takes someone off a command list. False when they were not on it.</summary>
    public bool RemovePerson(Entity<WGShipCommandComponent> grid, WGShipCommandList list, WGShipAccessKey? key, NetUserId? user)
    {
        var people = List(grid.Comp, list);
        var removed = people.FindAll(p => Matches(p, key, user));
        if (removed.Count == 0)
            return false;

        people.RemoveAll(removed.Contains);
        Dirty(grid);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{string.Join(", ", removed.ConvertAll(p => p.Name))} removed from the {list} of {ToPrettyString(grid.Owner):grid}");
        PeopleChanged(grid.Owner);
        return true;
    }

    /// <summary>Sets one of the admin powers; see <see cref="WGShipCommandComponent"/>. False when nothing changed.</summary>
    public bool SetAdminsInZones(Entity<WGShipCommandComponent> grid, bool value)
    {
        if (grid.Comp.AdminsInZones == value)
            return false;

        grid.Comp.AdminsInZones = value;
        Dirty(grid);
        PeopleChanged(grid.Owner);
        return true;
    }

    /// <summary>Whether someone is this listed person: their card's key or their player matches.</summary>
    public bool IsPerson(EntityUid user, WGShipCommandPerson person)
    {
        _player.TryGetSessionByEntity(user, out var session);
        WGShipAccessKey? key = _access.TryGetCard(user, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        return Matches(person, key, session?.UserId);
    }

    private static List<WGShipCommandPerson> List(WGShipCommandComponent comp, WGShipCommandList list)
    {
        return list switch
        {
            WGShipCommandList.Officers => comp.Officers,
            WGShipCommandList.Staff => comp.Staff,
            _ => comp.Barred,
        };
    }

    /// <summary>Someone's entry on the ship's command lists, the barred list first as for authority; null when on none.</summary>
    public static WGShipCommandPerson? FindListed(WGShipCommandComponent comp, WGShipAccessKey? key, NetUserId? user,
        out WGShipCommandList list)
    {
        foreach (var candidate in new[] { WGShipCommandList.Barred, WGShipCommandList.Officers, WGShipCommandList.Staff })
        {
            list = candidate;
            if (List(comp, candidate).Find(p => Matches(p, key, user)) is { } person)
                return person;
        }

        list = default;
        return null;
    }

    private static bool Listed(List<WGShipCommandPerson> people, WGShipAccessKey? key, NetUserId? user)
    {
        foreach (var person in people)
        {
            if (Matches(person, key, user))
                return true;
        }

        return false;
    }

    private static bool Matches(WGShipCommandPerson person, WGShipAccessKey? key, NetUserId? user)
    {
        return key != null && person.Key == key || user != null && person.User == user;
    }

    /// <summary>
    /// The record keys of a list's people: the card each was added with, and the card worn now by each listed player
    /// who is in the game.
    /// </summary>
    private void AddKeys(List<WGShipCommandPerson> people, HashSet<StationRecordKey> keys)
    {
        foreach (var person in people)
        {
            if (person.Key is { } key)
                keys.Add(_access.ToRecordKey(key));

            if (person.User is { } user && _player.TryGetSessionById(user, out var session) && TryGetWornKey(session, out var worn))
                keys.Add(worn.Value);
        }
    }

    private bool TryGetWornKey(ICommonSession session, [NotNullWhen(true)] out StationRecordKey? key)
    {
        key = null;
        if (session.AttachedEntity is not { } body
            || !_access.TryGetWornCard(body, out var card)
            || !_access.TryGetKey(card, out var found))
            return false;

        key = _access.ToRecordKey(found);
        return true;
    }
}
