using Content.Server._Goobstation.PairedExtendable;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WG.Augments;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Gives a body the action of each deployable implant in its parts, and folds the implant's item out into that
/// side's hand and back. An implant with several items opens a wheel for its owner to pick one from. An EMP folds
/// the item in and locks the implant until it wears off. An item whose implant keeps its charge comes back out with
/// the battery it went in with, plus what it recharged meanwhile.
/// </summary>
public sealed class WGDeployableImplantSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PairedExtendableSystem _extendable = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subscribe<WGToolArmComponent>();
        Subscribe<WGFingerLighterComponent>();
        Subscribe<WGArmLaserComponent>();
        Subscribe<WGHandShieldComponent>();
        SubscribeNetworkEvent<WGDeployableWheelPickEvent>(OnPick);
    }

    private void Subscribe<T>() where T : WGDeployableImplantComponent
    {
        SubscribeLocalEvent<T, ComponentInit>((Entity<T> ent, ref ComponentInit _) => Grant(ent, ent.Comp));
        SubscribeLocalEvent<T, ComponentShutdown>((Entity<T> ent, ref ComponentShutdown _) => Revoke(ent.Comp));
        // Raised on the part as it joins or leaves a body, and as it is switched on or off.
        SubscribeLocalEvent<T, BodyPartComponentsModifyEvent>((Entity<T> ent, ref BodyPartComponentsModifyEvent args) =>
        {
            if (args.Add)
                Grant(ent, ent.Comp);
            else
                Revoke(ent.Comp);
        });
        SubscribeLocalEvent<T, WGToggleDeployableImplantEvent>((Entity<T> ent, ref WGToggleDeployableImplantEvent args) => Toggle(ent, ent.Comp, args));
        SubscribeLocalEvent<T, EmpPulseEvent>((Entity<T> ent, ref EmpPulseEvent args) =>
        {
            args.Affected = true;
            args.Disabled = true;
            Retract(ent.Comp);
        });
    }

    private void Grant(EntityUid part, WGDeployableImplantComponent implant)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _actions.AddAction(body, ref implant.ActionEntity, implant.Action, part);
    }

    private void Revoke(WGDeployableImplantComponent implant)
    {
        Retract(implant);
        QueueDel(implant.ActionEntity);
        implant.ActionEntity = null;
    }

    private void Retract(WGDeployableImplantComponent implant)
    {
        StoreCharge(implant);
        QueueDel(implant.Deployed);
        implant.Deployed = null;
    }

    /// <summary>Folds a part's implant item away, if it is out.</summary>
    public void Stow(EntityUid part, WGDeployableImplantComponent implant)
    {
        if (!Exists(implant.Deployed))
            return;

        Retract(implant);
        _audio.PlayPvs(implant.RetractSound, part);
    }

    /// <summary>Folds an implant item away if one of the part's deployables put it out, and returns whether one did.</summary>
    public bool TryStowItem(EntityUid part, EntityUid item)
    {
        foreach (var implant in EntityManager.GetComponents<WGDeployableImplantComponent>(part))
        {
            if (implant.Deployed != item)
                continue;

            Stow(part, implant);
            return true;
        }

        return false;
    }

    /// <summary>Whether the part's implant may fold an item out into the body's hand.</summary>
    private bool CanDeploy(EntityUid part, EntityUid body)
    {
        var ev = new WGDeployAttemptEvent(body);
        RaiseLocalEvent(part, ref ev);
        return !ev.Cancelled;
    }

    private void StoreCharge(WGDeployableImplantComponent implant)
    {
        if (implant.KeepCharge && TryComp<BatteryComponent>(implant.Deployed, out var battery))
            implant.StoredCharge = (battery.CurrentCharge, _timing.CurTime);
    }

    private void RestoreCharge(WGDeployableImplantComponent implant)
    {
        if (implant.StoredCharge is not { } stored
            || implant.Deployed is not { } item
            || !TryComp<BatteryComponent>(item, out var battery))
            return;

        var rate = CompOrNull<BatterySelfRechargerComponent>(item)?.AutoRechargeRate ?? 0f;
        _battery.SetCharge(item, stored.Charge + rate * (float) (_timing.CurTime - stored.At).TotalSeconds, battery);
    }

    private void Toggle(EntityUid part, WGDeployableImplantComponent implant, WGToggleDeployableImplantEvent args)
    {
        // Every deployable in the part hears the event; only the one whose action it was answers.
        if (args.Action.Owner != implant.ActionEntity || !Usable(part, out var body, out var side))
            return;

        if (implant.Items.Count > 1)
        {
            // The wheel's pick starts the action's delay, not opening it.
            if (TryComp<ActorComponent>(body, out var actor))
            {
                RaiseNetworkEvent(new WGDeployableWheelOpenEvent
                {
                    Action = GetNetEntity(args.Action.Owner),
                    Items = implant.Items,
                    Deployed = DeployedIndex(implant),
                }, actor.PlayerSession);
            }

            return;
        }

        if (implant.Items.Count == 0 || !Exists(implant.Deployed) && !CanDeploy(part, body))
            return;

        StoreCharge(implant);
        args.Handled = _extendable.ToggleExtendable(body, implant.Items[0], side, out implant.Deployed, implant.Deployed);
        if (!args.Handled)
            return;

        RestoreCharge(implant);
        _audio.PlayPvs(implant.Deployed == null ? implant.RetractSound : implant.DeploySound, part);
    }

    private void OnPick(WGDeployableWheelPickEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user && TryGetEntity(msg.Action, out var action))
            Pick(user, action.Value, msg.Item);
    }

    /// <summary>
    /// Folds out the item a body picked on the wheel of its implant's action, in place of the one that was out, or
    /// folds that one back in if it is the one picked.
    /// </summary>
    public bool Pick(EntityUid user, EntityUid action, int index)
    {
        if (!_actions.TryGetActionData(action, out var data, false)
            || data.AttachedEntity != user
            || data.Container is not { } part
            || !_actions.ValidAction(data)
            || data.CheckConsciousness && !_blocker.CanConsciouslyPerformAction(user)
            || data.CheckCanInteract && !_blocker.CanInteract(user, null))
            return false;

        WGDeployableImplantComponent? implant = null;
        foreach (var candidate in EntityManager.GetComponents<WGDeployableImplantComponent>(part))
        {
            if (candidate.ActionEntity == action)
                implant = candidate;
        }

        if (implant == null
            || index < 0
            || index >= implant.Items.Count
            || !Usable(part, out var body, out var side)
            || body != user)
            return false;

        var stow = DeployedIndex(implant) == index;
        if (!stow && !CanDeploy(part, body))
            return false;

        // Deleted now, not queued, so the hand is free for the next item.
        StoreCharge(implant);
        if (Exists(implant.Deployed))
            Del(implant.Deployed);
        implant.Deployed = null;

        if (!stow && !_extendable.ToggleExtendable(body, implant.Items[index], side, out implant.Deployed))
            return false;

        RestoreCharge(implant);

        _audio.PlayPvs(stow ? implant.RetractSound : implant.DeploySound, part);
        _actions.StartUseDelay(action);
        return true;
    }

    /// <summary>The body a part's implants answer to and the hand they fold into, unless an EMP has them locked.</summary>
    private bool Usable(EntityUid part, out EntityUid body, out HandLocation side)
    {
        body = default;
        side = default;
        if (!TryComp<BodyPartComponent>(part, out var partComp) || partComp.Body is not { } owner)
            return false;

        body = owner;
        if (HasComp<EmpDisabledComponent>(part))
        {
            _popup.PopupEntity(Loc.GetString("wg-augment-deployable-emp"), body, body);
            return false;
        }

        side = partComp.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };
        return true;
    }

    /// <summary>Which of the implant's items is out, if any.</summary>
    private int? DeployedIndex(WGDeployableImplantComponent implant)
    {
        if (!Exists(implant.Deployed) || MetaData(implant.Deployed.Value).EntityPrototype is not { } proto)
            return null;

        var index = implant.Items.FindIndex(item => item.Id == proto.ID);
        return index < 0 ? null : index;
    }
}
