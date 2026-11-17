using Content.Server.Atmos.EntitySystems;
using Content.Shared.IgnitionSource;

namespace Content.Server.IgnitionSource;
public sealed partial class IgnitionSourceSystem : SharedIgnitionSourceSystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // WOLFGATE(Performance) START: walk a copy of the lit list; a fire this starts can light another source
        // var query = EntityQueryEnumerator<IgnitionSourceComponent, TransformComponent>();
        // while (query.MoveNext(out var uid, out var comp, out var xform))
        _wgLitWalk.Clear();
        _wgLitWalk.AddRange(WGLit);
        foreach (var uid in _wgLitWalk)
        {
            if (Paused(uid) || !TryComp(uid, out IgnitionSourceComponent? comp) || !TryComp(uid, out TransformComponent? xform))
                continue;
            // WOLFGATE END

            if (!comp.Ignited)
                continue;

            if (xform.GridUid is { } gridUid)
            {
                var position = _transform.GetGridOrMapTilePosition(uid, xform);
                // TODO: Should this be happening every single tick?
                _atmosphere.HotspotExpose(gridUid, position, comp.Temperature, 50, uid, true);
            }
        }
    }
}
