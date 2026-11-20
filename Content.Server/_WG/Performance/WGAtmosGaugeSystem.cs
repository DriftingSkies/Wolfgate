using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Performance;

/// <summary>
/// Every few seconds, works out which grids have a player near and has atmos report its load on them and on the rest.
/// The split sizes a slower atmos rate for grids nobody is near.
/// </summary>
public sealed class WGAtmosGaugeSystem : EntitySystem
{
    /// <summary>Seconds between samples.</summary>
    private const float Interval = 5f;

    /// <summary>How close, in metres, a player has to be for a grid to count as attended.</summary>
    private const float AttendedRange = 64f;

    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;

    private readonly HashSet<EntityUid> _attended = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;

        _accumulator -= Interval;
        _attended.Clear();
        var reach = new Vector2(AttendedRange, AttendedRange);
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == MapId.Nullspace || _ghostQuery.HasComp(uid))
                continue;

            if (xform.GridUid is { } aboard)
                _attended.Add(aboard);

            var position = _transform.GetWorldPosition(xform);
            _grids.Clear();
            _map.FindGridsIntersecting(xform.MapID, new Box2(position - reach, position + reach), ref _grids);
            foreach (var grid in _grids)
            {
                _attended.Add(grid.Owner);
            }
        }

        _atmos.WGSampleLoad(_attended);
    }
}
