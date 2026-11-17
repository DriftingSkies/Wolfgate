using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Companies;
using Content.Server._WG.ShipAccess;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Companies;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipAdmin;
using Content.Shared._WG.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;

    /// <summary>The most rows one list holds; the All grids filter can pass it on a busy round.</summary>
    public const int MaxRows = 500;

    /// <summary>The most occupants the detail names; the count is always exact.</summary>
    public const int MaxOccupants = 50;

    /// <summary>Whether a grid is a ship someone can own: it has a deed, an owner, ship access or a vessel design.</summary>
    public bool IsPlayerShip(EntityUid grid)
    {
        return HasComp<ShuttleDeedComponent>(grid)
               || HasComp<ShipOwnershipComponent>(grid)
               || HasComp<WGShipAccessComponent>(grid)
               || HasComp<VesselComponent>(grid);
    }

    /// <summary>The rows for a filter, by name.</summary>
    public List<WGShipAdminRow> BuildList(WGShipAdminFilter filter, out bool truncated)
    {
        truncated = false;
        var rows = new List<WGShipAdminRow>();
        var aboard = CountAboard();
        var holders = DeedHolders();

        var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out _, out var xform))
        {
            if (filter == WGShipAdminFilter.PlayerShips && !IsPlayerShip(uid))
                continue;

            if (rows.Count >= MaxRows)
            {
                truncated = true;
                break;
            }

            holders.TryGetValue(uid, out var cards);
            rows.Add(new WGShipAdminRow
            {
                Grid = GetNetEntity(uid),
                Name = Name(uid),
                Design = Design(uid),
                Owner = OwnerName(uid),
                OwnerState = OwnerState(uid, cards),
                Map = xform.MapUid is { } map ? Name(map) : string.Empty,
                Occupants = aboard.GetValueOrDefault(uid),
                Flags = RowFlags(uid),
                MapId = xform.MapID,
                Position = _transform.GetWorldPosition(xform),
            });
        }

        rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return rows;
    }

    /// <summary>Everything the panel shows about one ship; null when it is gone.</summary>
    public WGShipAdminDetail? BuildDetail(EntityUid grid, int? value = null, WGShipAdminDamage? damage = null)
    {
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return null;

        var xform = Transform(grid);
        var detail = new WGShipAdminDetail
        {
            Grid = GetNetEntity(grid),
            Name = Name(grid),
            Design = Design(grid),
            Map = xform.MapUid is { } map ? Name(map) : string.Empty,
            MapId = xform.MapID,
            Position = _transform.GetWorldPosition(xform),
            Mass = TryComp<PhysicsComponent>(grid, out var physics) ? physics.Mass : 0f,
            Speed = physics?.LinearVelocity.Length() ?? 0f,
            FtlLock = CompOrNull<FTLLockComponent>(grid)?.Enabled ?? false,
            Ftl = CompOrNull<FTLComponent>(grid)?.State.ToString() ?? string.Empty,
            Flags = RowFlags(grid),
            Value = value,
            Damage = damage,
        };

        if (TryGetFix(grid, out var done, out var total))
        {
            detail.FixDone = done;
            detail.FixTotal = total;
        }

        if (TryComp<IFFComponent>(grid, out var iff))
        {
            detail.IffColor = _shuttle.GetIFFColor(grid, component: iff).ToHex();
            detail.IffHidden = (iff.Flags & IFFFlags.Hide) != 0;
            detail.IffLabelHidden = (iff.Flags & IFFFlags.HideLabel) != 0;
        }

        detail.Protect = Protections(grid);
        detail.Refits = Refits(grid);

        // Whole percents, so a shield that is ticking back up doesn't resend the detail more than it must.
        if (_shields.TryGetWolfgateShieldStrength(grid, out var strength))
            detail.ShieldPercent = (int) MathF.Round(strength * 100f);

        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is not { } other || Transform(other).GridUid is not { } otherGrid)
                continue;

            var name = Name(otherGrid);
            if (!detail.DockedTo.Contains(name))
                detail.DockedTo.Add(name);
        }

        if (TryComp<ShuttleDeedComponent>(grid, out var deed) && !string.IsNullOrEmpty(deed.ShuttleOwner))
            detail.RecordOwner = deed.ShuttleOwner;

        if (TryComp<ShipOwnershipComponent>(grid, out var ownership))
        {
            detail.Registered = Username(ownership.OwnerUserId);
            detail.RegisteredOnline = IsOnline(ownership.OwnerUserId);
        }

        if (DeedHolders().TryGetValue(grid, out var cards))
        {
            foreach (var card in cards)
            {
                var owner = new WGShipAdminOwner
                {
                    Kind = HasComp<IdCardComponent>(card) ? WGShipAdminOwnerKind.DeedCard : WGShipAdminOwnerKind.Voucher,
                    Name = CardName(card),
                    Card = GetNetEntity(card),
                };

                if (TryComp<IdCardOwnerComponent>(card, out var stamp))
                {
                    owner.User = stamp.UserId;
                    owner.Username = Username(stamp.UserId);
                    owner.Online = IsOnline(stamp.UserId);
                }

                detail.Owners.Add(owner);
            }
        }

        if (TryComp<WGShipAccessComponent>(grid, out var access))
        {
            detail.HasAccess = true;
            detail.Mode = access.Mode;
            detail.Locked = access.Locked;
            detail.CanLock = _shipAccess.CanLock((grid, access));
            detail.AllowList.AddRange(access.AllowList);
            detail.Crew = access.CrewName ?? string.Empty;
            detail.Company = access.CompanyName ?? string.Empty;
            if (string.IsNullOrEmpty(detail.RecordOwner))
                detail.RecordOwner = access.OwnerName;

            foreach (var user in access.OwnerUsers)
            {
                var username = Username(user);
                detail.Owners.Add(new WGShipAdminOwner
                {
                    Kind = WGShipAdminOwnerKind.Account,
                    Name = username,
                    User = user,
                    Username = username,
                    Online = IsOnline(user),
                });
            }
        }

        if (TryComp<WGCompanyShipComponent>(grid, out var companyShip) && string.IsNullOrEmpty(detail.Company))
            detail.Company = companyShip.Name;

        if (TryComp<CompanyComponent>(grid, out var faction) && faction.CompanyName != "None")
            detail.Faction = faction.CompanyName;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } body || HasComp<GhostComponent>(body) || Transform(body).GridUid != grid)
                continue;

            detail.OccupantCount++;
            if (detail.Occupants.Count < MaxOccupants)
                detail.Occupants.Add(new WGShipAdminOccupant { Name = Name(body), Username = session.Name });
        }

        return detail;
    }

    /// <summary>What the ship would appraise for at a shipyard, before the sale rate and taxes.</summary>
    public int Appraise(EntityUid grid)
    {
        return _shipyard.GetShipAppraisal(grid);
    }

    /// <summary>Players in a living body on each grid.</summary>
    private Dictionary<EntityUid, int> CountAboard()
    {
        var aboard = new Dictionary<EntityUid, int>();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } body || HasComp<GhostComponent>(body) || Transform(body).GridUid is not { } grid)
                continue;

            aboard[grid] = aboard.GetValueOrDefault(grid) + 1;
        }

        return aboard;
    }

    /// <summary>The cards and vouchers that hold each ship's deed.</summary>
    private Dictionary<EntityUid, List<EntityUid>> DeedHolders()
    {
        var holders = new Dictionary<EntityUid, List<EntityUid>>();
        var deeds = AllEntityQuery<ShuttleDeedComponent>();
        while (deeds.MoveNext(out var uid, out var deed))
        {
            if (deed.ShuttleUid is not { } ship || ship == uid)
                continue;

            if (!holders.TryGetValue(ship, out var cards))
            {
                cards = new List<EntityUid>();
                holders[ship] = cards;
            }

            cards.Add(uid);
        }

        return holders;
    }

    private string Design(EntityUid grid)
    {
        if (!TryComp<VesselComponent>(grid, out var vessel) || string.IsNullOrEmpty(vessel.VesselId.Id))
            return string.Empty;

        return _proto.TryIndex(vessel.VesselId, out VesselPrototype? design) ? design.Name : vessel.VesselId.Id;
    }

    private string OwnerName(EntityUid grid)
    {
        if (TryComp<WGShipAccessComponent>(grid, out var access) && !string.IsNullOrEmpty(access.OwnerName))
            return access.OwnerName;

        return TryComp<ShuttleDeedComponent>(grid, out var deed) && !string.IsNullOrEmpty(deed.ShuttleOwner) ? deed.ShuttleOwner : string.Empty;
    }

    private WGShipAdminOwnerState OwnerState(EntityUid grid, List<EntityUid>? cards)
    {
        var owned = cards is { Count: > 0 };
        if (TryComp<ShipOwnershipComponent>(grid, out var ownership))
        {
            owned = true;
            if (IsOnline(ownership.OwnerUserId))
                return WGShipAdminOwnerState.Online;
        }

        if (TryComp<WGShipAccessComponent>(grid, out var access))
        {
            foreach (var user in access.OwnerUsers)
            {
                owned = true;
                if (IsOnline(user))
                    return WGShipAdminOwnerState.Online;
            }
        }

        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (TryComp<IdCardOwnerComponent>(card, out var stamp) && IsOnline(stamp.UserId))
                    return WGShipAdminOwnerState.Online;
            }
        }

        return owned ? WGShipAdminOwnerState.Offline : WGShipAdminOwnerState.None;
    }

    private WGShipAdminRowFlags RowFlags(EntityUid grid)
    {
        var flags = WGShipAdminRowFlags.None;
        if (TryComp<WGShipAccessComponent>(grid, out var access))
        {
            if (access.Locked)
                flags |= WGShipAdminRowFlags.Locked;

            if (access.CrewId != null)
                flags |= WGShipAdminRowFlags.CrewLinked;
        }

        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith == null)
                continue;

            flags |= WGShipAdminRowFlags.Docked;
            break;
        }

        if (HasComp<FTLComponent>(grid))
            flags |= WGShipAdminRowFlags.InFtl;

        if (HasComp<ShipRepairDataComponent>(grid))
            flags |= WGShipAdminRowFlags.Snapshot;

        if (HasComp<WGCompanyShipComponent>(grid))
            flags |= WGShipAdminRowFlags.Company;

        if (HasComp<WGCompanyHangarShipComponent>(grid))
            flags |= WGShipAdminRowFlags.Hangar;

        if (_fixes.ContainsKey(grid))
            flags |= WGShipAdminRowFlags.Fixing;

        return flags;
    }

    private string CardName(EntityUid card)
    {
        if (TryComp<IdCardOwnerComponent>(card, out var stamp) && !string.IsNullOrEmpty(stamp.CharacterName))
            return stamp.CharacterName;

        return TryComp<IdCardComponent>(card, out var id) && !string.IsNullOrEmpty(id.FullName) ? id.FullName : Name(card);
    }

    private string Username(NetUserId user)
    {
        return _player.TryGetPlayerData(user, out var data) ? data.UserName : user.ToString();
    }

    private bool IsOnline(NetUserId user)
    {
        return _player.TryGetSessionById(user, out var session) && session.Status != SessionStatus.Disconnected;
    }
}
