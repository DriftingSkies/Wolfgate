using System.Numerics;
using Content.Shared.Movement.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems;

/// <summary>Reuses a floating mob's push-off check for a few ticks while it stays put.</summary>
public abstract partial class SharedMoverController
{
    /// <summary>Ticks a push-off result is reused for.</summary>
    private const uint WfAroundColliderTicks = 5;

    /// <summary>Distance a mob may drift before its push-off check runs again.</summary>
    private const float WfAroundColliderMove = 0.05f;

    private readonly Dictionary<EntityUid, WfAroundColliderEntry> _wfAroundCollider = new();
    private readonly List<EntityUid> _wfStaleAroundCollider = new();
    private GameTick _wfNextAroundColliderPrune;

    private readonly record struct WfAroundColliderEntry(MapId Map, Vector2 Position, GameTick Tick, bool Touching);

    /// <summary>
    /// The mob's last push-off result if it's recent and the mob hasn't moved, else a fresh one. Predicted ticks
    /// always check fresh, so the client never replays a result the server didn't have.
    /// </summary>
    private bool WfIsAroundCollider(EntityLookupSystem lookupSystem,
        Entity<PhysicsComponent, MobMoverComponent, TransformComponent> entity)
    {
        if (Timing.InPrediction)
            return IsAroundCollider(lookupSystem, entity);

        var xform = entity.Comp3;
        var position = _transform.GetWorldPosition(xform);
        var now = Timing.CurTick;
        if (_wfAroundCollider.TryGetValue(entity.Owner, out var entry) &&
            entry.Map == xform.MapID &&
            now >= entry.Tick && now.Value - entry.Tick.Value < WfAroundColliderTicks &&
            (position - entry.Position).LengthSquared() < WfAroundColliderMove * WfAroundColliderMove)
        {
            return entry.Touching;
        }

        var touching = IsAroundCollider(lookupSystem, entity);
        _wfAroundCollider[entity.Owner] = new WfAroundColliderEntry(xform.MapID, position, now, touching);
        WfPruneAroundCollider(now);
        return touching;
    }

    /// <summary>Drops results no mob has refreshed in a while, such as those of deleted or landed mobs.</summary>
    private void WfPruneAroundCollider(GameTick now)
    {
        if (now < _wfNextAroundColliderPrune)
            return;

        _wfNextAroundColliderPrune = new GameTick(now.Value + 300);
        _wfStaleAroundCollider.Clear();
        foreach (var (uid, entry) in _wfAroundCollider)
        {
            if (entry.Tick > now || now.Value - entry.Tick.Value > WfAroundColliderTicks * 4)
                _wfStaleAroundCollider.Add(uid);
        }

        foreach (var uid in _wfStaleAroundCollider)
        {
            _wfAroundCollider.Remove(uid);
        }
    }
}
