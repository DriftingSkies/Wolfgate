using Content.Shared._WG.ShipFacing;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipFacing;

/// <summary>
/// Keeps the point each pilot holding the facing key wants their ship to face. The turning itself happens with the
/// rest of the helm's input, in <c>MoverController.ShipFacing</c>.
/// </summary>
public sealed partial class WGShipFacingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a point holds without being resent. The client resends well inside this.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(0.75);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShipFacingTargetEvent>(OnTarget);
    }

    private void OnTarget(WGShipFacingTargetEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        if (msg.Target is not { } target
            || !float.IsFinite(target.Position.X)
            || !float.IsFinite(target.Position.Y)
            || !TryComp<PilotComponent>(user, out var pilot)
            || pilot.Console is not { } console)
        {
            RemCompDeferred<WGShipFacingComponent>(user);
            return;
        }

        SetTarget(user, console, target);
    }

    /// <summary>Points a pilot's ship at a world point until it lapses or is cleared.</summary>
    public void SetTarget(EntityUid pilot, EntityUid console, MapCoordinates target)
    {
        var facing = EnsureComp<WGShipFacingComponent>(pilot);
        facing.Target = target;
        facing.Console = console;
        facing.Expires = _timing.CurTime + Lifetime;
    }

    /// <summary>The point a pilot wants their ship to face, if they are still holding the key at the same helm.</summary>
    public bool TryGetTarget(Entity<PilotComponent> pilot, out MapCoordinates target)
    {
        target = MapCoordinates.Nullspace;
        if (!TryComp<WGShipFacingComponent>(pilot, out var facing)
            || pilot.Comp.Console != facing.Console
            || _timing.CurTime > facing.Expires)
            return false;

        target = facing.Target;
        return true;
    }
}
