using System.Linq;
using Content.Server._NF.Station.Components;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Companies;
using Content.Server._WF.Traders;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Companies;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipAdmin;
using Content.Shared._WF.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipAdmin;

public sealed partial class WFShipAdminSystem
{
    [Dependency] private AdminVesselSpawnSystem _vesselSpawn = default!;
    [Dependency] private Content.Server.Shuttles.Systems.ShuttleConsoleLockSystem _consoleLocks = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IdCardOwnerSystem _cardOwner = default!;
    [Dependency] private Content.Server._NF.Shipyard.Systems.ShipOwnershipSystem _shipOwnership = default!;
    [Dependency] private WFCompanySystem _companies = default!;
    [Dependency] private WFShipAccessSystem _accessRules = default!;
    [Dependency] private Content.Server.Station.Systems.StationSystem _station = default!;

    /// <summary>
    /// Hands a ship to a player. Every earlier owner loses it: deeds come off their cards and their accounts are
    /// unregistered. The new owner gets the deed on their ID card when they carry one free of deeds, and their
    /// account is registered either way. The ship's lock, mode, allow list and door rules stay as they were unless
    /// an option says otherwise.
    /// </summary>
    /// <returns>False when the transfer was refused; <paramref name="result"/> says what happened either way.</returns>
    public bool Transfer(ICommonSession admin, EntityUid grid, WFShipAdminTransferMessage options, out string result)
    {
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wf-ship-admin-result-not-ship");
            return false;
        }

        if (!_player.TryGetSessionById(options.User, out var target) || !IsOnline(options.User))
        {
            result = Loc.GetString("wf-ship-admin-result-offline");
            return false;
        }

        if (HasComp<WFCompanyHangarShipComponent>(grid))
        {
            if (!options.DetachHangar || !Allowed(admin, WFShipAdminAction.DetachHangar))
            {
                result = Loc.GetString("wf-ship-admin-result-hangar");
                return false;
            }

            RemComp<WFCompanyHangarShipComponent>(grid);
        }

        var notes = new List<string>();
        var previous = OwnerAccounts(grid);
        var previousName = OwnerName(grid);
        var body = target.AttachedEntity is { Valid: true } attached && !HasComp<GhostComponent>(attached) ? attached : (EntityUid?) null;
        var ownerName = body != null ? Name(body.Value).Trim() : target.Name;

        // The old deeds come off first: the new owner may be one of the old ones, whose card is free again after this.
        var fresh = !HasComp<ShuttleDeedComponent>(grid) && !HasComp<ShipOwnershipComponent>(grid) && !HasComp<WFShipAccessComponent>(grid);
        var revoked = _shipyard.RevokeDeeds(grid);
        if (!_vesselSpawn.TryGetDeedCard(target, out var card, out var cardError))
            notes.Add(Loc.GetString("wf-ship-admin-note-account-only", ("reason", Loc.GetString(cardError, ("name", target.Name)))));

        // The ID they carry names their faction even when it holds another ship's deed and can't take this one.
        EntityUid? carried = body != null && _accessRules.TryGetCard(body.Value, out var worn) ? worn : null;

        if (fresh && card != null)
        {
            // Nobody ever owned it, so the whole purchase path is right: station, records, locks and access setup.
            VesselPrototype? vessel = null;
            if (TryComp<VesselComponent>(grid, out var design) && !string.IsNullOrEmpty(design.VesselId.Id))
                _proto.TryIndex(design.VesselId, out vessel);

            // A purchase puts the card's faction on the hull; here the faction option decides. It is settled first,
            // because the purchase sets ship access up from the hull's faction: Faction mode and the door lists.
            var factionBefore = CompOrNull<CompanyComponent>(grid)?.CompanyName;
            if (options.Faction != WFShipAdminFaction.Keep)
                SetFaction(grid, PickFaction(carried, options.Faction));

            if (!_shipyard.TryAssignDeed(grid, card.Value, target, vessel, Name(grid), cardFaction: false))
            {
                SetFaction(grid, factionBefore);
                result = Loc.GetString("wf-ship-admin-result-transfer-failed");
                return false;
            }

            _shipAccess.AddOwnerUser(grid, options.User);
        }
        else
        {
            var access = _shipAccess.EnsureAccess(grid);
            _shipAccess.ClearOwners(access);
            if (options.ClearAllowList)
                _shipAccess.ClearAllowList(access);

            _shipyard.SetShipOwnerName(grid, ownerName);
            if (card != null)
            {
                _shipyard.GrantDeed(grid, card.Value, ownerName);
                if (body != null)
                    _cardOwner.Stamp(card.Value, body.Value);

                _shipyard.CopyOwnerRecord(grid, card.Value, body, target);
            }

            _shipOwnership.RegisterShipOwnership(grid, target);
            _shipAccess.SetOwnerName(access, ownerName);
            _shipAccess.AddOwnerUser(grid, options.User);

            ApplyCompany(grid, access, card, carried, options);
            if (access.Comp.CrewId != null)
            {
                notes.Add(Loc.GetString("wf-ship-admin-note-crew", ("crew", access.Comp.CrewName ?? string.Empty)));
                if (options.LeaveCrewMode && access.Comp.Mode == WFShipAccessMode.Crew)
                    _shipAccess.SetMode(access, WFShipAccessMode.Private);
            }

            if (options.KeepPrevious)
            {
                foreach (var old in revoked)
                {
                    if (HasComp<IdCardComponent>(old))
                        _shipAccess.TryAddCard(access, old, CardName(old), Loc.GetString("wf-ship-admin-label-previous-owner"));
                }
            }

            if (_shipAccess.OwnersChanged(access))
                notes.Add(Loc.GetString("wf-ship-admin-note-unlocked"));
        }

        Audit(admin, LogImpact.High, grid,
            $"transferred from '{previousName}' ({string.Join(", ", previous.Select(Username))}) to '{ownerName}' ({target.Name}); deed card: {card != null}, allow list cleared: {options.ClearAllowList}, previous owners kept as crew: {options.KeepPrevious}");

        if (options.Notify)
        {
            previous.Remove(options.User);
            NoteUntold(notes, Tell(previous, Loc.GetString("wf-ship-admin-notice-lost", ("ship", Name(grid)))));
            Tell(new[] { options.User }, Loc.GetString("wf-ship-admin-notice-gained", ("ship", Name(grid))));
        }

        result = Loc.GetString("wf-ship-admin-result-transferred",
            ("ship", Name(grid)),
            ("from", previousName.Length > 0 ? previousName : Loc.GetString("wf-ship-admin-unowned")),
            ("to", ownerName));
        result = WithNotes(result, notes);
        return true;
    }

    /// <summary>Registers one more player's account as an owner. They keep the ship in any body; no deed is handed out.</summary>
    public bool AddOwner(ICommonSession admin, EntityUid grid, NetUserId user, bool notify, out string result)
    {
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wf-ship-admin-result-not-ship");
            return false;
        }

        var access = _shipAccess.EnsureAccess(grid);
        if (!_shipAccess.AddOwnerUser(grid, user))
        {
            result = Loc.GetString("wf-ship-admin-result-already-owner", ("name", Username(user)));
            return false;
        }

        var notes = new List<string>();
        _shipAccess.OwnersChanged(access);
        Audit(admin, LogImpact.Medium, grid, $"registered {Username(user)} as a co-owner");
        if (notify)
            NoteUntold(notes, Tell(new[] { user }, Loc.GetString("wf-ship-admin-notice-gained", ("ship", Name(grid)))));

        result = WithNotes(Loc.GetString("wf-ship-admin-result-owner-added", ("name", Username(user)), ("ship", Name(grid))), notes);
        return true;
    }

    /// <summary>
    /// Takes one owner off a ship: an account, or a card or voucher holding the deed. The last owner can't be
    /// removed this way; that is a release.
    /// </summary>
    public bool RemoveOwner(ICommonSession admin, EntityUid grid, NetUserId? user, EntityUid? card, bool notify, out string result)
    {
        if (!TryComp<WFShipAccessComponent>(grid, out var comp))
            comp = _shipAccess.EnsureAccess(grid).Comp;

        var access = new Entity<WFShipAccessComponent>(grid, comp);
        var holders = _shipyard.DeedHolders(grid);
        var remaining = holders.Count + comp.OwnerUsers.Count - 1;
        var isAccount = card == null && user is { } account && comp.OwnerUsers.Contains(account);
        var isCard = card is { } held && holders.Contains(held);
        if (!isAccount && !isCard)
        {
            result = Loc.GetString("wf-ship-admin-result-not-owner");
            return false;
        }

        if (remaining <= 0)
        {
            result = Loc.GetString("wf-ship-admin-result-last-owner");
            return false;
        }

        string removedName;
        NetUserId? removed;
        if (isCard)
        {
            removedName = CardName(card!.Value);
            removed = CompOrNull<IdCardOwnerComponent>(card.Value)?.UserId;
            _shipyard.RevokeDeed(card.Value, grid);
        }
        else
        {
            removedName = Username(user!.Value);
            removed = user;
            _shipAccess.RemoveOwnerUser(access, user.Value);
        }

        // The shipyard's own record names one account; it can't keep naming someone who no longer owns the ship.
        if (removed != null && TryComp<ShipOwnershipComponent>(grid, out var ownership) && ownership.OwnerUserId == removed
            && !OwnerAccounts(grid, false).Contains(removed.Value))
        {
            ICommonSession? heir = null;
            foreach (var other in OwnerAccounts(grid, false))
            {
                if (IsOnline(other) && _player.TryGetSessionById(other, out heir))
                    break;
            }

            if (heir != null)
                _shipOwnership.RegisterShipOwnership(grid, heir);
            else
                RemComp<ShipOwnershipComponent>(grid);
        }

        var notes = new List<string>();
        if (_shipAccess.OwnersChanged(access))
            notes.Add(Loc.GetString("wf-ship-admin-note-unlocked"));

        Audit(admin, LogImpact.Medium, grid, $"removed owner '{removedName}'");
        if (notify && removed != null)
            NoteUntold(notes, Tell(new[] { removed.Value }, Loc.GetString("wf-ship-admin-notice-lost", ("ship", Name(grid)))));

        result = WithNotes(Loc.GetString("wf-ship-admin-result-owner-removed", ("name", removedName), ("ship", Name(grid))), notes);
        return true;
    }

    /// <summary>
    /// Leaves a ship with no owner: every deed and account comes off and its doors unlock. Its helm is locked to
    /// the deed, so only staff fly it until it is transferred to someone.
    /// </summary>
    public bool Release(ICommonSession admin, EntityUid grid, bool notify, out string result)
    {
        var previous = OwnerAccounts(grid);
        var previousName = OwnerName(grid);
        if (previous.Count == 0 && _shipyard.DeedHolders(grid).Count == 0 && !HasComp<ShuttleDeedComponent>(grid))
        {
            result = Loc.GetString("wf-ship-admin-result-no-owner");
            return false;
        }

        _shipyard.RevokeDeeds(grid);
        RemComp<ShipOwnershipComponent>(grid);
        var unclaimed = Loc.GetString("wf-ship-admin-unclaimed");
        if (HasComp<ShuttleDeedComponent>(grid))
            _shipyard.SetShipOwnerName(grid, unclaimed);

        if (TryComp<WFShipAccessComponent>(grid, out var comp))
        {
            var access = new Entity<WFShipAccessComponent>(grid, comp);
            _shipAccess.ClearOwners(access);
            _shipAccess.SetOwnerName(access, unclaimed);
            _shipAccess.SetLocked(access, false);
        }

        // The owner may have left the helm unlocked to fly, and nobody is left to lock it again.
        _consoleLocks.WfLockHelm(grid);

        var notes = new List<string>();
        Audit(admin, LogImpact.High, grid, $"released from '{previousName}' ({string.Join(", ", previous.Select(Username))}); it has no owner now");
        if (notify)
            NoteUntold(notes, Tell(previous, Loc.GetString("wf-ship-admin-notice-lost", ("ship", Name(grid)))));

        result = WithNotes(Loc.GetString("wf-ship-admin-result-released", ("ship", Name(grid))), notes);
        return true;
    }

    /// <summary>
    /// Whether a grid can be given an owner. Every grid is a shuttle to the engine, so this only rules out a planet's
    /// own surface and a station's grid; handing a wreck or an encounter ship to a player is what the panel is for.
    /// A ship's own late-join station doesn't count as a station here.
    /// </summary>
    public bool CanBeOwned(EntityUid grid)
    {
        if (HasComp<MapComponent>(grid))
            return false;

        if (IsPlayerShip(grid) || _station.GetOwningStation(grid) is not { } station)
            return true;

        return CompOrNull<ExtraShuttleInformationComponent>(station)?.Vessel != null;
    }

    /// <summary>Every account that owns the ship: the shipyard's, the registered ones and those the deed cards were issued to.</summary>
    public HashSet<NetUserId> OwnerAccounts(EntityUid grid, bool includeShipyard = true)
    {
        var users = new HashSet<NetUserId>();
        if (includeShipyard && TryComp<ShipOwnershipComponent>(grid, out var ownership))
            users.Add(ownership.OwnerUserId);

        if (TryComp<WFShipAccessComponent>(grid, out var access))
            users.UnionWith(access.OwnerUsers);

        foreach (var card in _shipyard.DeedHolders(grid))
        {
            if (TryComp<IdCardOwnerComponent>(card, out var stamp))
                users.Add(stamp.UserId);
        }

        return users;
    }

    /// <summary>
    /// Settles the player company and hull faction of a ship that changed hands. <paramref name="card"/> is the ID
    /// that took the deed, if one did; <paramref name="carried"/> is the ID the new owner carries either way.
    /// </summary>
    private void ApplyCompany(EntityUid grid, Entity<WFShipAccessComponent> access, EntityUid? card, EntityUid? carried, WFShipAdminTransferMessage options)
    {
        if (card != null && HasComp<WFCompanyIdComponent>(card))
        {
            _companies.MarkBoughtShip(grid, card.Value);
        }
        else if (options.DropCompany)
        {
            RemComp<WFCompanyShipComponent>(grid);
            _shipAccess.SetCompany(access, null, null, Array.Empty<WFShipAccessKey>());
        }

        if (options.Faction != WFShipAdminFaction.Keep)
            SetFaction(grid, PickFaction(carried, options.Faction));

        if (access.Comp.Mode == WFShipAccessMode.Faction && !_accessRules.IsFactionGrid(grid, out _))
            _shipAccess.SetMode(access, WFShipAccessMode.Private);
    }

    /// <summary>The faction a transfer puts on the hull when it isn't kept: the one on the new owner's ID, or none.</summary>
    private ProtoId<CompanyPrototype>? PickFaction(EntityUid? id, WFShipAdminFaction option)
    {
        if (option != WFShipAdminFaction.FromCard || !TryComp<IdCardComponent>(id, out var idCard) || string.IsNullOrEmpty(idCard.CompanyName.Id))
            return null;

        return idCard.CompanyName;
    }

    /// <summary>Sets the hull's faction, or takes it off.</summary>
    private void SetFaction(EntityUid grid, ProtoId<CompanyPrototype>? faction)
    {
        if (faction is not { } name)
        {
            RemComp<CompanyComponent>(grid);
            return;
        }

        var hull = EnsureComp<CompanyComponent>(grid);
        hull.CompanyName = name;
        Dirty(grid, hull);
    }

    /// <summary>Sends a chat line to each account that is connected, and returns the names of those that are not.</summary>
    private List<string> Tell(IEnumerable<NetUserId> users, string message)
    {
        var untold = new List<string>();
        foreach (var user in users)
        {
            if (IsOnline(user) && _player.TryGetSessionById(user, out var session))
                _chat.DispatchServerMessage(session, message);
            else
                untold.Add(Username(user));
        }

        return untold;
    }

    private void NoteUntold(List<string> notes, List<string> untold)
    {
        if (untold.Count > 0)
            notes.Add(Loc.GetString("wf-ship-admin-note-untold", ("names", string.Join(", ", untold))));
    }

    private static string WithNotes(string result, List<string> notes)
    {
        return notes.Count == 0 ? result : $"{result} {string.Join(' ', notes)}";
    }
}
