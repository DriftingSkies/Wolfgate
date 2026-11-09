using Content.Shared._WF.Outposts;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>Lands trade deliveries: once a descent runs out, its product takes its place with a thud and a puff of dust.</summary>
public sealed partial class WFTradeDropSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;

    private readonly List<(EntityUid Uid, WFTradeDropComponent Drop)> _landing = new();

    /// <summary>Starts a descent of <paramref name="product"/> onto <paramref name="coords"/>; returns the descending entity.</summary>
    public EntityUid StartDrop(EntProtoId descent, EntProtoId product, EntityCoordinates coords)
    {
        var uid = Spawn(descent, coords);
        var drop = EnsureComp<WFTradeDropComponent>(uid);
        drop.Product = product;
        drop.Start = _timing.CurTime;
        Dirty(uid, drop);
        return uid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        _landing.Clear();
        var query = EntityQueryEnumerator<WFTradeDropComponent>();
        while (query.MoveNext(out var uid, out var drop))
        {
            if (now >= drop.Start + drop.Duration)
                _landing.Add((uid, drop));
        }

        foreach (var (uid, drop) in _landing)
        {
            var coords = Transform(uid).Coordinates;
            Spawn(drop.Product, coords);
            Spawn(drop.LandEffect, coords);
            _audio.PlayPvs(drop.LandSound, coords);
            QueueDel(uid);
        }
    }
}
