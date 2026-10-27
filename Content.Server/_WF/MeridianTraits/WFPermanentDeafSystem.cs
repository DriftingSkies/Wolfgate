using Content.Shared._WF.RMCCore.Deafness;
using Robust.Shared.Timing;

namespace Content.Server._WF.MeridianTraits;

/// <summary>
/// Puts deafness back on permanently deaf entities; knockouts and expiring deaf statuses remove it outright.
/// </summary>
public sealed partial class WFPermanentDeafSystem : EntitySystem
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

        var query = EntityQueryEnumerator<WFPermanentDeafComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            EnsureComp<DeafComponent>(uid);
        }
    }
}
