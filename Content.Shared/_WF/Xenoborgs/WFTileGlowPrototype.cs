using System.Numerics;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>Makes a floor tile glow: an unlit texture over it and light on it, both breathing between two strengths.</summary>
[Prototype("wfTileGlow")]
public sealed partial class WFTileGlowPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The tile that glows.</summary>
    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile;

    /// <summary>What glows, added unlit over the tile. Laid out like the tile's sprite: one square frame per variant.</summary>
    [DataField(required: true)]
    public ResPath Sprite;

    /// <summary>The glow's colour at the low of a breath.</summary>
    [DataField]
    public Color Dim = Color.Black;

    /// <summary>The glow's colour at the peak of a breath.</summary>
    [DataField]
    public Color Bright = Color.White;

    /// <summary>Light added over the whole tile at the peak; the low keeps <see cref="LightFloor"/> of it.</summary>
    [DataField]
    public Color Light = Color.Black;

    [DataField]
    public float LightFloor = 0.4f;

    /// <summary>Seconds per breath.</summary>
    [DataField]
    public float Period = 4f;

    /// <summary>Phase shift per tile along x and y, in radians, so a floor breathes as a travelling wave.</summary>
    [DataField]
    public Vector2 Wave = new(0.6f, 0.35f);
}
