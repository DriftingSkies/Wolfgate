using Content.Shared._WG.Mime;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Components;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._WG.Mime;

/// <summary>Grants the mime's actions and summons or puts away their invisible box and finger gun.</summary>
public sealed partial class MimeSummonSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MimeAbilitiesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MimeAbilitiesComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<MimeSummonActionComponent, MapInitEvent>(OnActionMapInit);
        SubscribeLocalEvent<MimeSummonActionComponent, MimeSummonActionEvent>(OnSummonAction);

        SubscribeLocalEvent<InvisibleBoxComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<InvisibleBoxComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnMapInit(Entity<MimeAbilitiesComponent> ent, ref MapInitEvent args)
    {
        foreach (var proto in ent.Comp.Actions)
        {
            EntityUid? action = null;
            if (_actions.AddAction(ent.Owner, ref action, proto))
                ent.Comp.Granted.Add(action.Value);
        }
    }

    private void OnShutdown(Entity<MimeAbilitiesComponent> ent, ref ComponentShutdown args)
    {
        foreach (var action in ent.Comp.Granted)
        {
            if (!TerminatingOrDeleted(ent)
                && TryComp<MimeSummonActionComponent>(action, out var summon)
                && summon.Item is { } item
                && _hands.IsHolding(ent.Owner, item))
                Retract((action, summon), ent.Owner, item);

            _actions.RemoveAction(ent.Owner, action);
        }
    }

    private void OnActionMapInit(Entity<MimeSummonActionComponent> ent, ref MapInitEvent args)
    {
        _containers.EnsureContainer<ContainerSlot>(ent.Owner, MimeSummonActionComponent.ContainerId);

        if (TrySpawnInContainer(ent.Comp.ItemId, ent.Owner, MimeSummonActionComponent.ContainerId, out var item))
            ent.Comp.Item = item;
    }

    private void OnSummonAction(Entity<MimeSummonActionComponent> ent, ref MimeSummonActionEvent args)
    {
        if (ent.Comp.Item is not { } item || TerminatingOrDeleted(item))
        {
            if (!TrySpawnInContainer(ent.Comp.ItemId, ent.Owner, MimeSummonActionComponent.ContainerId, out var spawned))
                return;

            item = spawned.Value;
            ent.Comp.Item = item;
        }

        if (_hands.IsHolding(args.Performer, item))
        {
            Retract(ent, args.Performer, item);
            args.Handled = true;
            return;
        }

        args.Handled = Summon(ent, args.Performer, item);
    }

    private bool Summon(Entity<MimeSummonActionComponent> ent, EntityUid user, EntityUid item)
    {
        // Marking it undroppable before knowing the pickup worked would trap it in the action forever.
        if (!_hands.TryPickupAnyHand(user, item, checkActionBlocker: false))
        {
            _popup.PopupEntity(Loc.GetString("mime-summon-hands-full"), user, user);
            return false;
        }

        EnsureComp<UnremoveableComponent>(item).DeleteOnDrop = false;
        Announce(user, ent.Comp.SummonMessage);
        return true;
    }

    private void Retract(Entity<MimeSummonActionComponent> ent, EntityUid user, EntityUid item)
    {
        RemComp<UnremoveableComponent>(item);
        _containers.Insert(item, _containers.GetContainer(ent.Owner, MimeSummonActionComponent.ContainerId));
        Announce(user, ent.Comp.PutAwayMessage);
    }

    private void OnInserted(Entity<InvisibleBoxComponent> ent, ref EntInsertedIntoContainerMessage args)
        => AnnounceContents(ent, "mime-invisible-box-insert", args.Entity);

    private void OnRemoved(Entity<InvisibleBoxComponent> ent, ref EntRemovedFromContainerMessage args)
        => AnnounceContents(ent, "mime-invisible-box-remove", args.Entity);

    private void AnnounceContents(EntityUid box, string message, EntityUid item)
    {
        var holder = Transform(box).ParentUid;
        if (!_hands.IsHolding(holder, box))
            return;

        _popup.PopupEntity(
            Loc.GetString($"{message}-others", ("user", Identity.Entity(holder, EntityManager)), ("item", item)),
            holder, Filter.PvsExcept(holder), true);
    }

    private void Announce(EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString($"{message}-self"), user, user);
        _popup.PopupEntity(
            Loc.GetString($"{message}-others", ("user", Identity.Entity(user, EntityManager))),
            user, Filter.PvsExcept(user), true);
    }
}
