using System.Linq;
using Content.Shared._WG.JoinQueue;
using Robust.Shared.Network;

namespace Content.Server._WG.JoinQueue;

/// <summary>
/// The order of the join queue. A player who disconnects keeps their place for a while, and the wait is guessed from
/// how often slots have opened lately.
/// </summary>
public sealed class JoinQueueLine
{
    /// <summary>Openings the wait estimate looks back over.</summary>
    private const int AdmitSamples = 10;

    /// <summary>Openings older than this say nothing about the current pace.</summary>
    private static readonly TimeSpan AdmitWindow = TimeSpan.FromMinutes(20);

    private readonly List<Entry> _entries = new();
    /// <summary>Recent openings, each with how many players it let in at once.</summary>
    private readonly List<(TimeSpan At, int Slots)> _admits = new();

    /// <summary>Everyone holding a place, connected or not.</summary>
    public int Count => _entries.Count;

    /// <summary>Queued players who are connected.</summary>
    public int Present => _entries.Count(entry => entry.DroppedAt == null);

    public bool Contains(NetUserId user) => Find(user) != null;

    /// <summary>Whether the player is queued and connected.</summary>
    public bool IsPresent(NetUserId user) => Find(user) is { DroppedAt: null };

    /// <summary>The connected players in order.</summary>
    public IEnumerable<NetUserId> PresentUsers => _entries.Where(entry => entry.DroppedAt == null).Select(entry => entry.User);

    /// <summary>Puts a player at the back, or back in the place they held.</summary>
    public void Join(NetUserId user, TimeSpan now)
    {
        if (Find(user) is { } entry)
            entry.DroppedAt = null;
        else
            _entries.Add(new Entry(user, now));
    }

    /// <summary>Marks a queued player as disconnected; their place is held until <see cref="Prune"/> drops it.</summary>
    public void Drop(NetUserId user, TimeSpan now)
    {
        if (Find(user) is { } entry)
            entry.DroppedAt ??= now;
    }

    public void Remove(NetUserId user)
    {
        _entries.RemoveAll(entry => entry.User == user);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>Takes the first connected player out of the queue for a slot that opened at <paramref name="now"/>.</summary>
    public NetUserId? TakeNext(TimeSpan now)
    {
        var next = _entries.FirstOrDefault(entry => entry.DroppedAt == null);
        if (next == null)
            return null;

        _entries.Remove(next);
        // Several let in at once are one opening, not several instant ones.
        if (_admits.Count > 0 && _admits[^1].At == now)
            _admits[^1] = (now, _admits[^1].Slots + 1);
        else
            _admits.Add((now, 1));
        if (_admits.Count > AdmitSamples)
            _admits.RemoveAt(0);

        return next.User;
    }

    /// <summary>Drops the places of players gone longer than <paramref name="hold"/>. True if any were dropped.</summary>
    public bool Prune(TimeSpan now, TimeSpan hold)
    {
        return _entries.RemoveAll(entry => entry.DroppedAt is { } dropped && now - dropped > hold) > 0;
    }

    /// <summary>Place in the queue, 1 being next, or 0 for a player who isn't queued.</summary>
    public int PositionOf(NetUserId user)
    {
        return _entries.FindIndex(entry => entry.User == user) + 1;
    }

    /// <summary>How long the player has held their place.</summary>
    public TimeSpan WaitedBy(NetUserId user, TimeSpan now)
    {
        return Find(user) is { } entry ? now - entry.JoinedAt : TimeSpan.Zero;
    }

    /// <summary>
    /// Rough seconds until the player at <paramref name="position"/> gets in, from the pace of recent openings, or
    /// <see cref="MsgJoinQueueUpdate.NoEstimate"/> with fewer than two to go on.
    /// </summary>
    public int EstimateSeconds(int position, TimeSpan now)
    {
        _admits.RemoveAll(admit => now - admit.At > AdmitWindow);
        if (position <= 0 || _admits.Count < 2)
            return MsgJoinQueueUpdate.NoEstimate;

        // Seconds per slot: the slots after the first opening over the time they took.
        var slots = 0;
        for (var i = 1; i < _admits.Count; i++)
        {
            slots += _admits[i].Slots;
        }

        var pace = (_admits[^1].At - _admits[0].At).TotalSeconds / slots;
        // The next opening is already partly waited for.
        var sinceLast = (now - _admits[^1].At).TotalSeconds;
        return (int) Math.Max(0, pace * position - Math.Min(sinceLast, pace));
    }

    private Entry? Find(NetUserId user) => _entries.FirstOrDefault(entry => entry.User == user);

    private sealed class Entry(NetUserId user, TimeSpan joinedAt)
    {
        public readonly NetUserId User = user;
        public readonly TimeSpan JoinedAt = joinedAt;

        /// <summary>When the player disconnected; null while connected.</summary>
        public TimeSpan? DroppedAt;
    }
}
