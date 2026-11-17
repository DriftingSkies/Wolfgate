using Content.Shared._WG.RMCCore.Deafness;
using Robust.Shared.Timing;

namespace Content.Server._WG.MeridianTraits;

/// <summary>
/// Puts deafness back on permanently deaf entities; knockouts and expiring deaf statuses remove it outright.
/// </summary>
public sealed partial class WGPermanentDeafSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<WGPermanentDeafComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            EnsureComp<DeafComponent>(uid);
        }
    }
}
