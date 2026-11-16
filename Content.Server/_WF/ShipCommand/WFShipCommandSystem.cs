using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.StationRecords;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Owner control for large admin vessels: who stands where with the ship (<see cref="WFShipAuthority"/>), its
/// command lists, and its zones, whose switches give the zone's doors their access through ShipAccess.
/// </summary>
public sealed partial class WFShipCommandSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeZones();
        InitializeCrew();
        InitializeWires();
        SubscribeLocalEvent<WFShipCommandComponent, WFShipManagerEvent>(OnShipManager);
    }

    /// <summary>Whoever configures the ship, admins included, also manages its access tab and console locks.</summary>
    private void OnShipManager(Entity<WFShipCommandComponent> grid, ref WFShipManagerEvent args)
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
    public WFShipAuthority GetAuthority(EntityUid user, EntityUid grid)
    {
        return GetAuthority(user, grid, out _);
    }

    /// <summary>
    /// Where someone stands with a ship, and in <paramref name="listed"/> where the command lists alone put them (guest
    /// on none). An admin falls back to their listed standing wherever the owner turned that admin power off.
    /// </summary>
    public WFShipAuthority GetAuthority(EntityUid user, EntityUid grid, out WFShipAuthority listed)
    {
        listed = WFShipAuthority.Guest;
        if (_access.IsOwner(user, grid))
            return WFShipAuthority.Owner;

        if (!TryComp<WFShipCommandComponent>(grid, out var command))
            return WFShipAuthority.Guest;

        _player.TryGetSessionByEntity(user, out var session);
        WFShipAccessKey? key = _access.TryGetCard(user, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        var userId = session?.UserId;
        if (Listed(command.Barred, key, userId))
            listed = WFShipAuthority.Barred;
        else if (Listed(command.Officers, key, userId))
            listed = WFShipAuthority.Officer;
        else if (Listed(command.Staff, key, userId))
            listed = WFShipAuthority.Staff;

        return session != null && CountsAsAdmin(_admin.GetAdminData(session), command.EventStaffAreAdmins)
            ? WFShipAuthority.Admin
            : listed;
    }

    /// <summary>Changes the ship's settings: the owner, and admins unless the owner turned that off.</summary>
    public bool CanConfigure(EntityUid user, EntityUid grid)
    {
        return GetAuthority(user, grid) switch
        {
            WFShipAuthority.Owner => true,
            WFShipAuthority.Admin => Comp<WFShipCommandComponent>(grid).AdminsAtConsole,
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
        return listed == WFShipAuthority.Officer || CanConfigure(user, grid);
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
    public bool AddPerson(Entity<WFShipCommandComponent> grid, WFShipCommandList list, WFShipAccessKey? key, NetUserId? user, string name)
    {
        if (key == null && user == null)
            return false;

        var target = List(grid.Comp, list);
        if (target.Exists(p => Matches(p, key, user)))
            return false;

        foreach (var other in Enum.GetValues<WFShipCommandList>())
        {
            if (other != list)
                List(grid.Comp, other).RemoveAll(p => Matches(p, key, user));
        }

        target.Add(new WFShipCommandPerson { Key = key, User = user, Name = name });
        Dirty(grid);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{name} was added to the {list} of {ToPrettyString(grid.Owner):grid}");
        PeopleChanged(grid.Owner);
        return true;
    }

    /// <summary>Takes someone off a command list. False when they were not on it.</summary>
    public bool RemovePerson(Entity<WFShipCommandComponent> grid, WFShipCommandList list, WFShipAccessKey? key, NetUserId? user)
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

    /// <summary>Sets one of the admin powers; see <see cref="WFShipCommandComponent"/>. False when nothing changed.</summary>
    public bool SetAdminsInZones(Entity<WFShipCommandComponent> grid, bool value)
    {
        if (grid.Comp.AdminsInZones == value)
            return false;

        grid.Comp.AdminsInZones = value;
        Dirty(grid);
        PeopleChanged(grid.Owner);
        return true;
    }

    /// <summary>Whether someone is this listed person: their card's key or their player matches.</summary>
    public bool IsPerson(EntityUid user, WFShipCommandPerson person)
    {
        _player.TryGetSessionByEntity(user, out var session);
        WFShipAccessKey? key = _access.TryGetCard(user, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        return Matches(person, key, session?.UserId);
    }

    private static List<WFShipCommandPerson> List(WFShipCommandComponent comp, WFShipCommandList list)
    {
        return list switch
        {
            WFShipCommandList.Officers => comp.Officers,
            WFShipCommandList.Staff => comp.Staff,
            _ => comp.Barred,
        };
    }

    /// <summary>Someone's entry on the ship's command lists, the barred list first as for authority; null when on none.</summary>
    public static WFShipCommandPerson? FindListed(WFShipCommandComponent comp, WFShipAccessKey? key, NetUserId? user,
        out WFShipCommandList list)
    {
        foreach (var candidate in new[] { WFShipCommandList.Barred, WFShipCommandList.Officers, WFShipCommandList.Staff })
        {
            list = candidate;
            if (List(comp, candidate).Find(p => Matches(p, key, user)) is { } person)
                return person;
        }

        list = default;
        return null;
    }

    private static bool Listed(List<WFShipCommandPerson> people, WFShipAccessKey? key, NetUserId? user)
    {
        foreach (var person in people)
        {
            if (Matches(person, key, user))
                return true;
        }

        return false;
    }

    private static bool Matches(WFShipCommandPerson person, WFShipAccessKey? key, NetUserId? user)
    {
        return key != null && person.Key == key || user != null && person.User == user;
    }

    /// <summary>
    /// The record keys of a list's people: the card each was added with, and the card worn now by each listed player
    /// who is in the game.
    /// </summary>
    private void AddKeys(List<WFShipCommandPerson> people, HashSet<StationRecordKey> keys)
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
