namespace Content.Server.Decals;

public sealed partial class DecalSystem
{
    /// <summary>Ticks between decal sends to players; chunks dirtied in between wait and go in one message.</summary>
    public const int WgDecalFlushTicks = 3;

    /// <summary>Chunks dirtied since the last send, per grid.</summary>
    private readonly Dictionary<NetEntity, HashSet<Vector2i>> _wgPendingChunks = new();

    private int _wgDecalTicks;

    /// <summary>
    /// Moves this tick's dirty chunks into the pending set, and on a send tick moves every pending chunk back into
    /// <c>_dirtyChunks</c>. Players are still updated every tick, so chunks entering or leaving view go at once and a
    /// grid that left a client's view is never written to after it has gone there.
    /// </summary>
    private void WgGatherDirtyChunks()
    {
        foreach (var (grid, chunks) in _dirtyChunks)
        {
            if (_wgPendingChunks.TryGetValue(grid, out var pending))
                pending.UnionWith(chunks);
            else
                _wgPendingChunks[grid] = chunks;
        }

        _dirtyChunks.Clear();

        if (++_wgDecalTicks < WgDecalFlushTicks)
            return;

        _wgDecalTicks = 0;
        foreach (var (grid, chunks) in _wgPendingChunks)
        {
            _dirtyChunks[grid] = chunks;
        }

        _wgPendingChunks.Clear();
    }
}
