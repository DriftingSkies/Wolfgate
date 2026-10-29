using Content.Server.Worldgen.Prototypes;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems.GC;

public sealed partial class GCQueueSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>When each queued entity joined its queue, for queues with a minimum age.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wfQueuedAt = new();

    private void WFInitializeMinAge()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _wfQueuedAt.Clear());
    }

    private void WFOnQueued(EntityUid uid, GCQueuePrototype proto)
    {
        if (proto.WFMinAge > TimeSpan.Zero)
            _wfQueuedAt[uid] = _wfTiming.CurTime;
    }

    /// <summary>
    /// Whether an entity already waits in an age-protected queue. Queuing it again would restart nothing, as the first
    /// entry still leaves first, so it keeps its place and its first time.
    /// </summary>
    private bool WFAlreadyQueued(EntityUid uid, GCQueuePrototype proto)
    {
        return proto.WFMinAge > TimeSpan.Zero && _wfQueuedAt.ContainsKey(uid);
    }

    /// <summary>
    /// Whether an age-protected queue past its depth may still take an entity. It can hold up to twice its depth,
    /// and while over its depth its oldest entries go regardless of age, through the usual per-tick time limit.
    /// </summary>
    private static bool WFCanOverflow(Queue<EntityUid> queue, GCQueuePrototype proto)
    {
        return proto.WFMinAge > TimeSpan.Zero && queue.Count < proto.Depth * 2;
    }

    /// <summary>
    /// Whether the queue's oldest entity is too young to delete yet. Queues are first in, first out, so everything
    /// behind it is younger still. A queue past its depth deletes regardless.
    /// </summary>
    private bool WFTooYoung(Queue<EntityUid> queue, GCQueuePrototype proto)
    {
        return proto.WFMinAge > TimeSpan.Zero &&
               queue.Count <= proto.Depth &&
               queue.TryPeek(out var oldest) &&
               _wfQueuedAt.TryGetValue(oldest, out var at) &&
               _wfTiming.CurTime - at < proto.WFMinAge;
    }

    private void WFOnDequeued(EntityUid uid)
    {
        _wfQueuedAt.Remove(uid);
    }
}
