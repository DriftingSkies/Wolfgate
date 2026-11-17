using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Administration;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAccess;

public sealed partial class WGShipAccessServerSystem
{
    [Dependency] private IAdminManager _adminManager = default!;

    /// <summary>The shuttle console's access tab: lock, add, remove, builder flags, door rules and codes.</summary>
    private void InitializeConsole()
    {
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WGShipAccessSetModeMessage>(OnSetMode);
            subs.Event<WGShipAccessSetLockedMessage>(OnSetLocked);
            subs.Event<WGShipAccessAddPlayerMessage>(OnAddPlayer);
            subs.Event<WGShipAccessRemoveMessage>(OnRemove);
            subs.Event<WGShipAccessSetBuilderMessage>(OnSetBuilder);
            subs.Event<WGShipAccessSetDoorRuleMessage>(OnSetDoorRule);
            subs.Event<WGShipAccessSetAllDoorRulesMessage>(OnSetAllDoorRules);
            subs.Event<WGShipAccessSetDoorPlayerMessage>(OnSetDoorPlayer);
            subs.Event<WGShipAccessRequestCodesMessage>(OnRequestCodes);
            subs.Event<WGShipAccessSetShipCodeMessage>(OnSetShipCode);
            subs.Event<WGShipAccessSetDoorCodeMessage>(OnSetDoorCode);
        });
    }

    private void OnSetMode(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetModeMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship))
            return;

        // Public is for staff to set. Anyone who manages the ship may switch away from it, but only staff to it.
        if (args.Mode == WGShipAccessMode.Public && ship.Comp.Mode != WGShipAccessMode.Public && !IsStaff(args.Actor))
        {
            Popup(console, args.Actor, "ship-access-mode-unavailable");
            return;
        }

        // Someone who manages the ship without owning it, an admin or an officer, must not bind it to a crew or
        // company of their own: they may only switch to one it is already linked to.
        var owner = _access.IsOwner(args.Actor, ship);
        if (args.Mode == WGShipAccessMode.Crew && !(owner ? TryLinkCrew(ship, args.Actor) : ship.Comp.CrewId != null))
        {
            Popup(console, args.Actor, "ship-access-no-crew");
            return;
        }
        if (args.Mode == WGShipAccessMode.Company && !(owner ? TryLinkCompany(ship, args.Actor) : ship.Comp.CompanyId != null))
        {
            Popup(console, args.Actor, "ship-access-no-company");
            return;
        }
        if (!SetMode(ship, args.Mode))
            Popup(console, args.Actor, "ship-access-mode-unavailable");
    }

    /// <summary>Whether a body is played by an active admin with the staff flag.</summary>
    private bool IsStaff(EntityUid actor)
    {
        return TryComp<ActorComponent>(actor, out var played) && _adminManager.HasAdminFlag(played.PlayerSession, AdminFlags.Admin);
    }

    private void OnSetLocked(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetLockedMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship))
            return;

        if (args.Locked && !CanLock(ship))
        {
            Popup(console, args.Actor, "ship-access-no-owner-key");
            return;
        }

        SetLocked(ship, args.Locked);
    }

    private void OnRemove(Entity<ShuttleConsoleComponent> console, ref WGShipAccessRemoveMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            RemoveEntry(ship, args.Key);
    }

    private void OnSetBuilder(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetBuilderMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            SetBuilder(ship, args.Key, args.Builder);
    }

    private void OnAddPlayer(Entity<ShuttleConsoleComponent> console, ref WGShipAccessAddPlayerMessage args)
    {
        var actor = args.Actor;
        if (!TryGetEditableShip(console, actor, out var ship) || !TryGetEntity(args.Target, out var target))
            return;

        // An ID card in the owner's own hands is added as it is; its owner need not be there.
        if (_access.TryGetHeldCard(actor, target.Value, out var held))
        {
            AddHeldCard(console, actor, ship, held);
            return;
        }

        if (!_transform.InRange(Transform(console.Owner).Coordinates, Transform(target.Value).Coordinates, WGShipAccessSystem.AddRange))
        {
            Popup(console, actor, "ship-access-add-out-of-range");
            return;
        }

        if (!_access.TryGetCard(target.Value, out var card))
        {
            Popup(console, actor, "ship-access-add-no-card");
            return;
        }

        if (!_access.TryGetKey(card, out _))
        {
            Popup(console, actor, "ship-access-add-no-record");
            return;
        }

        if (!TryAddPerson(ship, target.Value))
        {
            Popup(console, actor, "ship-access-add-already");
            return;
        }

        _popup.PopupEntity(Loc.GetString("ship-access-added", ("name", Name(target.Value))), console.Owner, actor);
    }

    private void AddHeldCard(Entity<ShuttleConsoleComponent> console, EntityUid actor, Entity<WGShipAccessComponent> ship, EntityUid card)
    {
        if (!_access.TryGetKey(card, out _))
        {
            Popup(console, actor, "ship-access-add-card-no-record");
            return;
        }

        var name = _access.CardHolderName(card);
        if (!TryAddCard(ship, card, name))
        {
            Popup(console, actor, "ship-access-add-already");
            return;
        }

        _popup.PopupEntity(Loc.GetString("ship-access-added", ("name", name)), console.Owner, actor);
    }

    /// <summary>
    /// The ship behind a console when the actor owns it: carries its deed, or is a player it is registered to. A ship
    /// bought before this module existed gets its component, and its readers, on the first edit; anyone else gets
    /// the not-owner popup.
    /// </summary>
    private bool TryGetEditableShip(Entity<ShuttleConsoleComponent> console, EntityUid actor, out Entity<WGShipAccessComponent> ship)
    {
        ship = default;
        if (Transform(console.Owner).GridUid is not { } grid)
            return false;

        if (!IsOwner(actor, grid))
        {
            Popup(console, actor, "ship-access-not-owner");
            return false;
        }

        ship = EnsureShip(grid);
        return true;
    }

    /// <summary>The component for a ship bought before ship access existed, keeping whatever the lock verb last set.</summary>
    private Entity<WGShipAccessComponent> EnsureShip(EntityUid grid)
    {
        if (TryComp<WGShipAccessComponent>(grid, out var comp))
            return (grid, comp);

        comp = EnsureComp<WGShipAccessComponent>(grid);
        comp.Mode = _access.IsFactionGrid(grid, out _) ? WGShipAccessMode.Faction : WGShipAccessMode.Private;
        comp.Locked = AnyReaderEnabled(grid);
        Dirty(grid, comp);
        MarkShipStorage(grid);
        RefreshShip((grid, comp));
        return (grid, comp);
    }

    private bool AnyReaderEnabled(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<ShipAccessReaderComponent>(child, out var reader) && reader.Enabled)
                return true;
        }

        return false;
    }

    private void Popup(Entity<ShuttleConsoleComponent> console, EntityUid actor, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), console.Owner, actor);
    }
}
