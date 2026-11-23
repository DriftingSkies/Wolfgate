using Content.Shared.Gravity;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WG.Xenonids.Movement;

/// <summary>
/// Ends a xeno's throw-driven ability (charge, lunge, tumble, headbutt) where it would have landed when it's weightless:
/// without this it keeps its speed and slides off into space, and nothing raises the landing its ability waits on.
/// </summary>
public sealed class XenoWeightlessThrowSystem : EntitySystem
{
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnStopThrow(EntityUid uid, XenoComponent comp, StopThrowEvent args)
    {
        if (!_gravity.IsWeightless(uid) || !TryComp<PhysicsComponent>(uid, out var physics))
            return;

        _physics.SetLinearVelocity(uid, System.Numerics.Vector2.Zero, body: physics);
        var land = new LandEvent(args.User, false);
        RaiseLocalEvent(uid, ref land);
    }
}
