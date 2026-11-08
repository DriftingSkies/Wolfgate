using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    /// <summary>The shuttle console's access tab: lock, add, remove, builder flags, door rules and codes.</summary>
    private void InitializeConsole()
    {
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, SubscribeAccessTab);
    }

    /// <summary>
    /// Subscribes a console's UI to the Access tab's messages, for the grid the console stands on: the shuttle
    /// console's here, and any other console that shows the tab (the outpost console) from its own system.
    /// </summary>
    public void SubscribeAccessTab<T>(BoundUserInterfaceRegisterExt.Subscriber<T> subs) where T : IComponent
    {
        subs.Event<WFShipAccessSetModeMessage>((uid, _, args) => OnSetMode(uid, ref args));
        subs.Event<WFShipAccessSetLockedMessage>((uid, _, args) => OnSetLocked(uid, ref args));
        subs.Event<WFShipAccessAddPlayerMessage>((uid, _, args) => OnAddPlayer(uid, ref args));
        subs.Event<WFShipAccessRemoveMessage>((uid, _, args) => OnRemove(uid, ref args));
        subs.Event<WFShipAccessSetBuilderMessage>((uid, _, args) => OnSetBuilder(uid, ref args));
        subs.Event<WFShipAccessSetDoorRuleMessage>((uid, _, args) => OnSetDoorRule(uid, ref args));
        subs.Event<WFShipAccessSetAllDoorRulesMessage>((uid, _, args) => OnSetAllDoorRules(uid, ref args));
        subs.Event<WFShipAccessSetDoorPlayerMessage>((uid, _, args) => OnSetDoorPlayer(uid, ref args));
        subs.Event<WFShipAccessRequestCodesMessage>((uid, _, args) => OnRequestCodes(uid, ref args));
        subs.Event<WFShipAccessSetShipCodeMessage>((uid, _, args) => OnSetShipCode(uid, ref args));
        subs.Event<WFShipAccessSetDoorCodeMessage>((uid, _, args) => OnSetDoorCode(uid, ref args));
    }

    private void OnSetMode(EntityUid console, ref WFShipAccessSetModeMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship))
            return;
        if (args.Mode == WFShipAccessMode.Crew && !TryLinkCrew(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-crew");
            return;
        }
        if (args.Mode == WFShipAccessMode.Company && !TryLinkCompany(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-company");
            return;
        }
        if (!SetMode(ship, args.Mode))
            Popup(console, args.Actor, "ship-access-mode-unavailable");
    }

    private void OnSetLocked(EntityUid console, ref WFShipAccessSetLockedMessage args)
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

    private void OnRemove(EntityUid console, ref WFShipAccessRemoveMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            RemoveEntry(ship, args.Key);
    }

    private void OnSetBuilder(EntityUid console, ref WFShipAccessSetBuilderMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship))
            SetBuilder(ship, args.Key, args.Builder);
    }

    private void OnAddPlayer(EntityUid console, ref WFShipAccessAddPlayerMessage args)
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

        if (!_transform.InRange(Transform(console).Coordinates, Transform(target.Value).Coordinates, WFShipAccessSystem.AddRange))
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

        _popup.PopupEntity(Loc.GetString("ship-access-added", ("name", Name(target.Value))), console, actor);
    }

    private void AddHeldCard(EntityUid console, EntityUid actor, Entity<WFShipAccessComponent> ship, EntityUid card)
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

        _popup.PopupEntity(Loc.GetString("ship-access-added", ("name", name)), console, actor);
    }

    /// <summary>
    /// The ship behind a console when the actor owns it: carries its deed, or is a player it is registered to. A ship
    /// bought before this module existed gets its component, and its readers, on the first edit; anyone else gets
    /// the not-owner popup.
    /// </summary>
    private bool TryGetEditableShip(EntityUid console, EntityUid actor, out Entity<WFShipAccessComponent> ship)
    {
        ship = default;
        if (Transform(console).GridUid is not { } grid)
            return false;

        if (!_access.IsOwner(actor, grid))
        {
            Popup(console, actor, "ship-access-not-owner");
            return false;
        }

        ship = EnsureShip(grid);
        return true;
    }

    /// <summary>The component for a ship bought before ship access existed, keeping whatever the lock verb last set.</summary>
    private Entity<WFShipAccessComponent> EnsureShip(EntityUid grid)
    {
        if (TryComp<WFShipAccessComponent>(grid, out var comp))
            return (grid, comp);

        comp = EnsureComp<WFShipAccessComponent>(grid);
        comp.Mode = _access.IsFactionGrid(grid, out _) ? WFShipAccessMode.Faction : WFShipAccessMode.Private;
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

    private void Popup(EntityUid console, EntityUid actor, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), console, actor);
    }
}
