using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map;

namespace Content.Shared._WF.Vehicles.Compat;

/// <summary>Teleports an entity and whatever it pulls, restarting the pull so the joint is not carried across maps.</summary>
public sealed partial class VehiclePullingSystem : EntitySystem
{
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Teleports <paramref name="user"/> and its pulled entity to <paramref name="teleport"/> and resumes the pull.</summary>
    public void HandlePulling(EntityUid user, MapCoordinates teleport)
    {
        if (TryComp(user, out PullableComponent? otherPullable) &&
            otherPullable.Puller != null)
        {
            _pulling.TryStopPull(user, otherPullable, otherPullable.Puller.Value);
        }

        if (TryComp(user, out PullerComponent? puller) &&
            TryComp(puller.Pulling, out PullableComponent? pullable))
        {
            if (TryComp(puller.Pulling, out PullerComponent? otherPullingPuller) &&
                TryComp(otherPullingPuller.Pulling, out PullableComponent? otherPullingPullable))
            {
                _pulling.TryStopPull(otherPullingPuller.Pulling.Value, otherPullingPullable, puller.Pulling);
            }

            var pulling = puller.Pulling.Value;
            _pulling.TryStopPull(pulling, pullable, user);
            _transform.SetMapCoordinates(user, teleport);
            _transform.SetMapCoordinates(pulling, teleport);
            _pulling.TryStartPull(user, pulling);
        }
        else
        {
            _transform.SetMapCoordinates(user, teleport);
        }
    }
}
