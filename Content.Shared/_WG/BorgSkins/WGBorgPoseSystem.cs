using Content.Shared.Actions;
using Content.Shared.Movement.Events;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.BorgSkins;

/// <summary>Gives a borg the pose actions its skin supports and tracks the pose it holds. Poses are cosmetic.</summary>
public sealed partial class WGBorgPoseSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private static readonly Dictionary<WGBorgPose, EntProtoId> PoseActions = new()
    {
        { WGBorgPose.Rest, "WGActionBorgPoseRest" },
        { WGBorgPose.Sit, "WGActionBorgPoseSit" },
        { WGBorgPose.BellyUp, "WGActionBorgPoseBellyUp" },
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, WGBorgSubtypeSelectedEvent>(OnSubtypeSelected);
        SubscribeLocalEvent<WGBorgPoseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGBorgPoseComponent, WGBorgPoseActionEvent>(OnPoseAction);
        SubscribeLocalEvent<WGBorgPoseComponent, MoveInputEvent>(OnMoveInput);
    }

    private void OnSubtypeSelected(Entity<BorgSwitchableSubtypeComponent> ent, ref WGBorgSubtypeSelectedEvent args)
    {
        if (_net.IsClient)
            return;

        RemComp<WGBorgPoseComponent>(ent);

        if (!_prototype.TryIndex<WGBorgSkinPrototype>(args.Subtype.Id, out var skin) || skin.Poses.Count == 0)
            return;

        var pose = AddComp<WGBorgPoseComponent>(ent);
        foreach (var offered in skin.Poses)
        {
            if (PoseActions.TryGetValue(offered, out var action) && _actions.AddAction(ent, action) is { } added)
                pose.Actions.Add(added);
        }
    }

    private void OnShutdown(Entity<WGBorgPoseComponent> ent, ref ComponentShutdown args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            _actions.RemoveAction(ent.Owner, action);
            if (_net.IsServer)
                QueueDel(action);
        }
    }

    private void OnPoseAction(Entity<WGBorgPoseComponent> ent, ref WGBorgPoseActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetPose(ent, ent.Comp.Pose == args.Pose ? WGBorgPose.None : args.Pose);
    }

    private void OnMoveInput(Entity<WGBorgPoseComponent> ent, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement)
            SetPose(ent, WGBorgPose.None);
    }

    private void SetPose(Entity<WGBorgPoseComponent> ent, WGBorgPose pose)
    {
        if (ent.Comp.Pose == pose)
            return;

        ent.Comp.Pose = pose;
        Dirty(ent);
    }
}
