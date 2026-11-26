using System.Numerics;
using Content.Shared._WG.CodaSong;
using Content.Shared.Damage;
using Robust.Server.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.CodaSong;

/// <summary>
/// Song sources on the server: a fragment's heart sings more wrongly the closer it is to breaking, and twice a second
/// every player is told which source has them and how near it is, so it is heard from its whole range.
/// </summary>
public sealed partial class WGSongSourceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSongSourceComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(Entity<WGSongSourceComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.TurnedAt <= 0f)
            return;

        var turned = Math.Clamp(args.Damageable.TotalDamage.Float() / ent.Comp.TurnedAt, 0f, 1f);
        if (MathF.Abs(turned - ent.Comp.Turned) < 0.02f)
            return;

        ent.Comp.Turned = turned;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TimeSpan.FromSeconds(0.5);
        var sources = new List<(WGSongSourceComponent Song, Robust.Shared.Map.MapId Map, Vector2 At)>();
        var query = EntityQueryEnumerator<WGSongSourceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var song, out var xform))
        {
            if (!song.Silent)
                sources.Add((song, xform.MapID, _transform.GetWorldPosition(xform)));
        }

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } mob)
                continue;

            var xform = Transform(mob);
            var here = _transform.GetWorldPosition(xform);
            WGSongSourceComponent? best = null;
            var near = 0f;
            foreach (var (song, map, at) in sources)
            {
                var distance = Vector2.Distance(at, here);
                if (map != xform.MapID || distance > song.Range)
                    continue;

                var closeness = 0.25f + 0.75f * (1f - distance / song.Range);
                if (closeness > near)
                {
                    near = closeness;
                    best = song;
                }
            }

            if (best == null && !HasComp<WGSongListenerComponent>(mob))
                continue;

            var listener = EnsureComp<WGSongListenerComponent>(mob);
            var source = best?.Source;
            var turned = best?.Turned ?? 0f;
            if (listener.Source == source && MathF.Abs(listener.Near - near) < 0.03f && MathF.Abs(listener.Turned - turned) < 0.02f)
                continue;

            listener.Source = source;
            listener.Near = near;
            listener.Turned = turned;
            Dirty(mob, listener);
        }
    }
}
