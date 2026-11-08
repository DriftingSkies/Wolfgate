using Content.Server._WF.Caverns;
using Content.Server._WF.Outposts.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Foundations: the cavern under an outpost's claim loads as foundation rock nothing mines, and the ground there never
/// opens onto it, by a mouth, a hole, stairs or a shaft. Cavern that was loaded or changed before the claim, such as a
/// tunnel someone is in, keeps what it has until it unloads.
/// </summary>
public sealed partial class WFOutpostSystem
{
    private static readonly TimeSpan FoundationInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextFoundation;

    private void InitializeFoundations()
    {
        SubscribeLocalEvent<WFCavernOpenAttemptEvent>(OnCavernOpenAttempt);
    }

    private void OnCavernOpenAttempt(ref WFCavernOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (!IsLanded(uid, outpost) || outpost.Ground != args.Ground)
                continue;

            var (min, max) = GetClaimZone((uid, outpost, grid));
            if (args.Max.X < min.X || args.Max.Y < min.Y || args.Min.X > max.X || args.Min.Y > max.Y)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    private void UpdateFoundations()
    {
        if (_timing.CurTime < _nextFoundation)
            return;

        _nextFoundation = _timing.CurTime + FoundationInterval;
        RefreshFoundations();
    }

    /// <summary>Brings every cavern's foundation up to date with the claims of the outposts standing over it.</summary>
    public void RefreshFoundations()
    {
        var zones = new Dictionary<EntityUid, List<Box2i>>();
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (!IsLanded(uid, outpost) || !TryComp<WFCavernGroundComponent>(outpost.Ground, out var caverns)
                || TerminatingOrDeleted(caverns.Cavern))
                continue;

            var (min, max) = GetClaimZone((uid, outpost, grid));
            zones.GetOrNew(caverns.Cavern).Add(new Box2i(min, max + Vector2i.One));
        }

        var stale = EntityQueryEnumerator<WFOutpostFoundationComponent>();
        while (stale.MoveNext(out var cavern, out var foundation))
        {
            if (!zones.ContainsKey(cavern))
                foundation.Zones.Clear();
        }

        foreach (var (cavern, list) in zones)
        {
            EnsureComp<WFOutpostFoundationComponent>(cavern).Zones = list;
        }
    }

    /// <summary>Whether an outpost stands on its ground, not staged or on its way down.</summary>
    private bool IsLanded(EntityUid uid, WFOutpostComponent outpost)
    {
        return !TerminatingOrDeleted(uid) && Transform(uid).MapUid == outpost.Ground;
    }
}
