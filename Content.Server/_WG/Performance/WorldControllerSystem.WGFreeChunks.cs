using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Robust.Shared.Configuration;

namespace Content.Server.Worldgen.Systems;

/// <summary>Server cvars for freeing world chunks.</summary>
[CVarDefs]
public sealed class WGChunkFreeCVars
{
    /// <summary>
    /// Seconds a world chunk stays unloaded before its entity is deleted; 0 keeps every chunk for the round, as
    /// upstream does. A chunk that still owns live debris is kept until that debris is gone.
    /// </summary>
    public static readonly CVarDef<float> ChunkFreeSeconds =
        CVarDef.Create("wg.worldgen.chunk_free_seconds", 600f, CVar.SERVERONLY);
}

public sealed partial class WorldControllerSystem
{
    /// <summary>Most chunks freed in one sweep.</summary>
    private const int WGFreePerSweep = 2000;

    private static readonly TimeSpan WGFreeEvery = TimeSpan.FromSeconds(10);

    /// <summary>Unloaded chunks and when each was unloaded.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wgIdleChunks = new();
    private readonly List<EntityUid> _wgFree = new();
    private float _wgChunkFreeSeconds;
    private TimeSpan _wgNextFree;

    /// <summary>Chunks freed this round; for tests and tuning.</summary>
    public int WGChunksFreed { get; private set; }

    private void WGInitializeFreeChunks()
    {
        _wgCfg.OnValueChanged(WGChunkFreeCVars.ChunkFreeSeconds, v => _wgChunkFreeSeconds = v, true);
    }

    private void WGNoteChunkLoaded(EntityUid uid)
    {
        _wgIdleChunks.Remove(uid);
    }

    private void WGNoteChunkUnloaded(EntityUid uid)
    {
        _wgIdleChunks[uid] = _gameTiming.CurTime;
    }

    /// <summary>
    /// Deletes chunks that have been unloaded for long enough; run once the sweep knows what loaders want. Upstream keeps one entity in nullspace for every
    /// chunk anyone ever came near, 25,000 an hour on a busy round; a chunk made again later is the same chunk but
    /// for where its rocks fall, which an unloaded chunk respawns anyway.
    /// </summary>
    private void WGFreeIdleChunks()
    {
        var now = _gameTiming.CurTime;
        if (_wgChunkFreeSeconds <= 0f || now < _wgNextFree || _wgIdleChunks.Count == 0)
            return;

        _wgNextFree = now + WGFreeEvery;
        _wgFree.Clear();
        foreach (var (uid, since) in _wgIdleChunks)
        {
            if (TerminatingOrDeleted(uid) || WGIsLoaded(uid))
            {
                _wgFree.Add(uid);
                continue;
            }

            if ((now - since).TotalSeconds < _wgChunkFreeSeconds || WGOwnsLiveDebris(uid))
                continue;

            // A loader is back for it this sweep: it is about to be loaded, not freed.
            if (TryComp<WorldChunkComponent>(uid, out var chunk) && WGChunkWanted(uid, chunk))
                continue;

            _wgFree.Add(uid);
            if (_wgFree.Count >= WGFreePerSweep)
                break;
        }

        foreach (var uid in _wgFree)
        {
            _wgIdleChunks.Remove(uid);
            if (TerminatingOrDeleted(uid) || WGIsLoaded(uid))
                continue;

            QueueDel(uid);
            WGChunksFreed++;
        }
    }

    /// <summary>
    /// Whether a loader wants a loaded chunk this sweep. A chunk whose map is gone is wanted by nobody and is deleted:
    /// upstream indexed the sweep by the chunk's map and threw on every later sweep once a world map had been deleted.
    /// </summary>
    private bool WGChunkWanted(EntityUid uid, WorldChunkComponent chunk)
    {
        if (_chunksToLoad.TryGetValue(chunk.Map, out var wanted))
            return wanted.ContainsKey(chunk.Coordinates);

        QueueDel(uid);
        return false;
    }

    /// <summary>
    /// Whether a chunk is loaded. One the sweep has just unloaded still has its component until the tick ends, shut
    /// down; that one is not.
    /// </summary>
    private bool WGIsLoaded(EntityUid uid)
    {
        return TryComp<LoadedChunkComponent>(uid, out var loaded) && loaded.LifeStage <= ComponentLifeStage.Running;
    }

    /// <summary>Whether debris the chunk placed is still out there and reports back to it.</summary>
    private bool WGOwnsLiveDebris(EntityUid uid)
    {
        if (!TryComp<DebrisFeaturePlacerControllerComponent>(uid, out var placer))
            return false;

        foreach (var debris in placer.OwnedDebris.Values)
        {
            if (debris is { } owned && !TerminatingOrDeleted(owned))
                return true;
        }

        return false;
    }
}
