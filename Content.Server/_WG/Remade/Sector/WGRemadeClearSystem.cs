using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Shared._WG.Remade.Dread;
using Content.Shared._WG.Remade.Signals;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Remade.Sector;

/// <summary>
/// Wipes the Remade from the round: every stone, shard, Remade, growth, corruption tile, Signal and stone sound, and tells every
/// client to drop the bed and the score. Clients are also told when the last stone goes by any means.
/// </summary>
public sealed class WGRemadeClearSystem : EntitySystem
{
    private const string RemadeAudio = "/Audio/_WG/Remade/";

    /// <summary>Tells every client to drop the bed and the score once a terminating stone was the last one.</summary>
    public void LastStoneGone(EntityUid stone)
    {
        var query = EntityQueryEnumerator<WGMonolithComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (uid != stone && !TerminatingOrDeleted(uid))
                return;
        }

        RaiseNetworkEvent(new WGRemadeClearEvent(), Filter.Broadcast());
    }

    /// <summary>Deletes everything of the Remade in the round, at once, so call it between ticks. Returns how many entities went.</summary>
    public int Clear()
    {
        var doomed = new HashSet<EntityUid>();
        Collect<WGMonolithComponent>(doomed);
        Collect<WGMonolithShardComponent>(doomed);
        Collect<WGRemadeComponent>(doomed);
        Collect<WGGrowthComponent>(doomed);
        Collect<WGCorruptionComponent>(doomed);
        Collect<WGSignalComponent>(doomed);

        var sounds = EntityQueryEnumerator<AudioComponent>();
        while (sounds.MoveNext(out var uid, out var audio))
        {
            // Read into a local first: the access analyzer counts a call on the field as more than a read.
            var file = audio.FileName;
            if (file.StartsWith(RemadeAudio, StringComparison.Ordinal))
                doomed.Add(uid);
        }

        // At once, not queued: the spreader runs once more before a queue is flushed and would grow tiles nobody collected.
        foreach (var uid in doomed)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        var stressed = EntityQueryEnumerator<WGRemadeStressComponent>();
        while (stressed.MoveNext(out var uid, out _))
        {
            RemCompDeferred<WGRemadeStressComponent>(uid);
        }

        RaiseNetworkEvent(new WGRemadeClearEvent(), Filter.Broadcast());
        return doomed.Count;
    }

    private void Collect<T>(HashSet<EntityUid> into) where T : IComponent
    {
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out _))
        {
            into.Add(uid);
        }
    }
}
