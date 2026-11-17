using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.NetTraffic;

/// <summary>
/// An explosion's overlay and sound in one message, instead of a visuals entity and audio entities sent to everyone on
/// the map. The client builds the overlay entity itself and steps it one iteration a tick.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGExplosionEvent : EntityEventArgs
{
    public MapCoordinates Epicenter;
    public string ExplosionType = string.Empty;
    public List<float> Intensity = new();
    public Dictionary<int, List<Vector2i>>? SpaceTiles;
    public Dictionary<NetEntity, Dictionary<int, List<Vector2i>>> Tiles = new();
    public Matrix3x2 SpaceMatrix;
    public ushort SpaceTileSize;

    /// <summary>Seconds the finished overlay stays.</summary>
    public float Persistence;

    /// <summary>The sound for this player: the near one at the epicentre, or the far one heard everywhere; null for none.</summary>
    public SoundSpecifier? Sound;
    public bool FarSound;
    public NetCoordinates SoundAt;
}
