using Robust.Shared.Timing;

namespace Content.Shared.Pinpointer;

public sealed partial class NavMapComponent
{
    /// <summary>
    /// Server: tick each chunk was removed, so a delta names only the removals a client hasn't seen. Listing every chunk
    /// instead cost about 30 KB per update on a planet map.
    /// </summary>
    [ViewVariables]
    public Dictionary<Vector2i, GameTick> WGRemovedChunks = new();

    /// <summary>
    /// Server: newest removal dropped from <see cref="WGRemovedChunks"/>. A client that hasn't seen past it needs a full
    /// state, since no delta can name what it missed.
    /// </summary>
    [ViewVariables]
    public GameTick WGRemovedTrimmedTo;
}
