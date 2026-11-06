using Robust.Shared.Timing;

namespace Content.Shared._WF.Performance;

/// <summary>
/// The <see cref="WFTickSpread"/> split kept in buckets, so a system walks its whole set once a period instead of
/// every tick to find the few entities due. Entities added mid-period join at the next fill.
/// </summary>
public sealed class WFTickBuckets
{
    private List<EntityUid>[] _buckets = Array.Empty<List<EntityUid>>();
    private int _slot;

    /// <summary>
    /// Moves to this tick's bucket. True at the start of a period, or when the period changed: the caller then
    /// <see cref="Add"/>s its whole set again.
    /// </summary>
    public bool Begin(GameTick tick, int period)
    {
        period = Math.Max(1, period);
        _slot = (int) (tick.Value % (uint) period);
        if (_buckets.Length == period && _slot != 0)
            return false;

        if (_buckets.Length != period)
        {
            _buckets = new List<EntityUid>[period];
            for (var i = 0; i < period; i++)
            {
                _buckets[i] = new List<EntityUid>();
            }
        }
        else
        {
            foreach (var bucket in _buckets)
            {
                bucket.Clear();
            }
        }

        return true;
    }

    /// <summary>Files an entity under its tick of the period.</summary>
    public void Add(EntityUid uid)
    {
        _buckets[(int) ((uint) uid.Id % (uint) _buckets.Length)].Add(uid);
    }

    /// <summary>The entities due this tick. May hold deleted entities; check before use.</summary>
    public List<EntityUid> Due => _buckets[_slot];
}
