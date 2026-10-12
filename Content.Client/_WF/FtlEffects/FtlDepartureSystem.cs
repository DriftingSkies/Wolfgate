using System.Numerics;
using Content.Shared._WF.FtlEffects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.FtlEffects;

/// <summary>Installs the hull-fitted hyperspace departure overlay.</summary>
public sealed partial class FtlDepartureSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IClientGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        var background = new FtlMotionBackgroundOverlay(this);
        _overlays.AddOverlay(background);
        _overlays.AddOverlay(new FtlMotionOverlay(this, background));
        _overlays.AddOverlay(new FtlDepartureOverlay(this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<FtlDepartureOverlay>();
        _overlays.RemoveOverlay<FtlMotionOverlay>();
        _overlays.RemoveOverlay<FtlMotionBackgroundOverlay>();
        base.Shutdown();
    }

    /// <summary>
    /// Time of the server state on screen. Grids are not predicted, so the predicted clock runs ahead of
    /// the moment a ship is seen to leave or arrive.
    /// </summary>
    public TimeSpan Now
    {
        get
        {
            var lead = Math.Max(0L, (long) _timing.CurTick.Value - _timing.LastRealTick.Value);
            return _timing.CurTime - TimeSpan.FromTicks(_timing.TickPeriod.Ticks * lead);
        }
    }

    /// <summary>The crew's camera stays aboard; outside observers see the ship streak past.</summary>
    public float Motion(EntityUid grid, FtlDepartureComponent effect)
    {
        if (Aboard(grid, effect))
            return 0f;
        return FtlDepartureTiming.Motion(Now, effect.Departure, effect.Entered, effect.Arriving);
    }

    /// <summary>Whether the ship has launched while the server has yet to move its grid off this map.</summary>
    public bool Gone(EntityUid grid, FtlDepartureComponent effect)
    {
        return !Aboard(grid, effect) && FtlDepartureTiming.Gone(Now, effect.Departure, effect.Entered, effect.Arriving);
    }

    /// <summary>Where a rushing hull is drawn; docked grids ride the lead ship's stretch.</summary>
    public Matrix3x2 Moved(FtlDepartureComponent effect, MapGridComponent grid, TransformComponent xform, Matrix3x2 matrix,
        float motion)
    {
        if (effect.Lead is { } lead && TryComp(lead, out MapGridComponent? leadGrid) &&
            TryComp(lead, out TransformComponent? leadXform) && leadXform.MapID == xform.MapID)
        {
            return FtlConeGeometry.FollowerTransform(leadGrid.LocalAABB, motion, matrix,
                _transforms.GetWorldMatrix(leadXform));
        }
        return FtlConeGeometry.MotionTransform(grid.LocalAABB, motion) * matrix;
    }

    /// <summary>Skips the background copy unless a rushing or launched hull can touch this viewport.</summary>
    public bool HasMotion(MapId map, Box2 view)
    {
        var query = EntityQueryEnumerator<FtlDepartureComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var grid, out var xform))
        {
            var motion = Motion(uid, effect);
            if (xform.MapID != map || MathF.Abs(motion) < 0.0001f && !Gone(uid, effect))
                continue;
            var matrix = _transforms.GetWorldMatrix(xform);
            var moved = Moved(effect, grid, xform, matrix, motion);
            if (view.Intersects(matrix.TransformBox(grid.LocalAABB.Enlarged(0.5f))) ||
                view.Intersects(moved.TransformBox(grid.LocalAABB.Enlarged(0.5f))))
                return true;
        }
        return false;
    }

    /// <summary>Whether the local player stands on the grid or on another ship of its docked convoy.</summary>
    private bool Aboard(EntityUid grid, FtlDepartureComponent effect)
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform) ||
            xform.GridUid is not { } deck)
            return false;
        var lead = effect.Lead ?? grid;
        return deck == grid || deck == lead || TryComp(deck, out FtlDepartureComponent? own) && own.Lead == lead;
    }
}
