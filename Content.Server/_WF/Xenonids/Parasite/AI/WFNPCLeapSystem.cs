using Content.Server.DoAfter;
using Content.Server.Interaction;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.Parasite.AI;

/// <summary>Performs the leap action for NPC xenos that carry <see cref="WFNPCLeapComponent"/>, aiming it at the target and cancelling it when the target moves off the line. Ported from RMC-14's NPCLeapSystem.</summary>
public sealed class WFNPCLeapSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFNPCLeapComponent, ComponentShutdown>(OnShutdown);
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    private void OnShutdown(Entity<WFNPCLeapComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.CurrentDoAfter != null)
            _doAfter.Cancel(ent.Comp.CurrentDoAfter);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WFNPCLeapComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Status is WFLeapStatus.Unspecified or WFLeapStatus.Finished)
                continue;

            if (!_xformQuery.TryGetComponent(comp.Target, out var targetXform) || targetXform.MapID != xform.MapID)
            {
                comp.Status = WFLeapStatus.TargetUnreachable;
                continue;
            }

            if (!TryComp<DoAfterComponent>(uid, out var after))
            {
                comp.Status = WFLeapStatus.Unspecified;
                continue;
            }

            if (comp.CurrentDoAfter != null)
            {
                var status = _doAfter.GetStatus(comp.CurrentDoAfter.Value, after);
                comp.Status = status switch
                {
                    DoAfterStatus.Running => WFLeapStatus.Normal,
                    DoAfterStatus.Finished => WFLeapStatus.Finished,
                    _ => WFLeapStatus.Unspecified,
                };

                if (comp.Status != WFLeapStatus.Normal)
                {
                    comp.CurrentDoAfter = null;
                    continue;
                }

                var worldPos = _transform.GetMoverCoordinates(uid);
                var targetPos = _transform.GetMoverCoordinates(comp.Target);
                if (!worldPos.TryDistance(EntityManager, targetPos, out var range))
                {
                    comp.Status = WFLeapStatus.Unspecified;
                    continue;
                }

                if (!_interaction.InRangeUnobstructed(uid, comp.Target, range, comp.Mask))
                {
                    _doAfter.Cancel(comp.CurrentDoAfter.Value);
                    comp.CurrentDoAfter = null;
                    comp.Status = WFLeapStatus.TargetOutOfRange;
                    continue;
                }

                var targetDir = (targetPos.Position - worldPos.Position).Normalized();
                var destDir = (comp.Destination.Position - worldPos.Position).Normalized();
                var angle = Angle.ShortestDistance(new Angle(targetDir), new Angle(destDir));
                if (angle > Angle.FromDegrees(comp.MaxAngleDegrees))
                {
                    _doAfter.Cancel(comp.CurrentDoAfter.Value);
                    comp.CurrentDoAfter = null;
                    comp.Status = WFLeapStatus.TargetBadAngle;
                }

                continue;
            }

            // read the dictionary into a local first: the access analyzer treats a call on the field as execute
            var xenoActions = CompOrNull<XenoComponent>(uid)?.Actions;
            if (xenoActions == null ||
                !xenoActions.TryGetValue(comp.ActionId, out var actionId) ||
                !TryComp(actionId, out WorldTargetActionComponent? action) ||
                !_actions.ValidAction(action))
            {
                comp.Status = WFLeapStatus.Unspecified;
                continue;
            }

            var origin = _transform.GetMoverCoordinates(uid);
            var target = _transform.GetMoverCoordinates(comp.Target);
            var offset = (target.Position - origin.Position).Normalized() * comp.LeapDistance;
            comp.Destination = origin.WithPosition(origin.Position + offset);

            var actionEvent = _rmcActions.GetEvent(actionId);
            if (actionEvent == null)
            {
                comp.Status = WFLeapStatus.Unspecified;
                continue;
            }

            actionEvent.Performer = uid;
            if (actionEvent is WorldTargetActionEvent worldTarget)
                worldTarget.Target = comp.Destination;

            var nextId = after.NextId;
            _actions.PerformAction(uid, null, actionId, action, actionEvent, _timing.CurTime);

            // the id did not move: the action was cancelled before it started a do-after
            if (nextId == after.NextId)
                comp.CurrentDoAfter = null;
            else if (after.DoAfters.TryGetValue(nextId, out var doAfter))
                comp.CurrentDoAfter = doAfter.Id;
        }
    }
}
