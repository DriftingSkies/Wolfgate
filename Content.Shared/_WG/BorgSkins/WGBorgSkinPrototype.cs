using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.BorgSkins;

/// <summary>Visual extras for the borg subtype with the same ID: where an oversized sprite sits and whether it animates.</summary>
[Prototype("wgBorgSkin")]
public sealed partial class WGBorgSkinPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Offset of the chassis layers, in tiles. A 32x64 sprite needs (0, 0.5) to stand on its tile.</summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>The body, eye or lamp states are animated and must stay in step with each other.</summary>
    [DataField]
    public bool Animated;

    /// <summary>The RSI has a <c>_moving</c> variant of each chassis state, shown while the borg moves.</summary>
    [DataField]
    public bool Walks;

    /// <summary>The voluntary poses the RSI has a state for, each offered to the borg as an action.</summary>
    [DataField]
    public List<WGBorgPose> Poses = new();
}
