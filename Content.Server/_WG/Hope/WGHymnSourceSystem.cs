using System.Numerics;
using Content.Shared._WG.Hope;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WG.Hope;

/// <summary>Twice a second, tells every player which hymn source has them and how near it is.</summary>
public sealed class WGHymnSourceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private TimeSpan _next;
    private readonly List<(WGHymnSourceComponent Hymn, MapId Map, Vector2 At)> _sources = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TimeSpan.FromSeconds(0.5);
        Gather();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } mob)
                Hear(mob, gathered: true);
        }
    }

    /// <summary>Works out which source a mob hears and how near, and tells its listener if that changed.</summary>
    public void Hear(EntityUid mob, bool gathered = false)
    {
        if (!gathered)
            Gather();

        var xform = Transform(mob);
        var here = _transform.GetWorldPosition(xform);
        WGHymnSourceComponent? best = null;
        var near = 0f;
        var nearest = float.MaxValue;
        foreach (var (hymn, map, at) in _sources)
        {
            var distance = Vector2.Distance(at, here);
            if (map != xform.MapID || distance > hymn.Range || distance >= nearest)
                continue;

            nearest = distance;
            near = 0.25f + 0.75f * (1f - distance / hymn.Range);
            best = hymn;
        }

        if (best == null && !HasComp<WGHymnListenerComponent>(mob))
            return;

        var listener = EnsureComp<WGHymnListenerComponent>(mob);
        var source = best?.Source;
        if (listener.Source == source && MathF.Abs(listener.Near - near) < 0.03f)
            return;

        listener.Source = source;
        listener.Near = near;
        listener.Gap = best?.Gap ?? Vector2.Zero;
        Dirty(mob, listener);
    }

    private void Gather()
    {
        _sources.Clear();
        var query = EntityQueryEnumerator<WGHymnSourceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var hymn, out var xform))
        {
            if (!hymn.Silent && hymn.Range > 0f)
                _sources.Add((hymn, xform.MapID, _transform.GetWorldPosition(xform)));
        }
    }
}
