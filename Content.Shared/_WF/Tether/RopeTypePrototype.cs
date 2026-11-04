using Robust.Shared.Utility;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Tether;

/// <summary>
/// Physical and visual parameters shared by every rope of one kind.
/// Stiffness is per metre of rope: the spring constant is <c>Stiffness / Length</c>.
/// </summary>
[Prototype]
public sealed partial class RopeTypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localisation id shown when examining an attach point.</summary>
    [DataField]
    public LocId Name = "rope-name-generic";

    /// <summary>Spring constant per metre, in N/m/m. Divided by the rest length to get the rope's k.</summary>
    [DataField]
    public float Stiffness = 4000f;

    /// <summary>Fraction of critical damping applied to separating motion only.</summary>
    [DataField]
    public float DampingRatio = 0.7f;

    /// <summary>Fraction past the rest length where the inextensible hard limit sits.</summary>
    [DataField]
    public float MaxStretch = 0.1f;

    /// <summary>Tension in newtons that snaps the rope. 0 is unbreakable.</summary>
    [DataField]
    public float BreakForce;

    /// <summary>Longest rest length this rope may be set to, in metres.</summary>
    [DataField]
    public float MaxLength = 20f;

    /// <summary>
    /// False for power cords: no joint and no forces, but the rope still snaps once stretched
    /// past the hard limit.
    /// </summary>
    [DataField]
    public bool LoadBearing = true;

    /// <summary>Slack colour.</summary>
    [DataField]
    public Color Color = Color.FromHex("#9c8460");

    /// <summary>Colour the rope lerps to as strain approaches the hard limit.</summary>
    [DataField]
    public Color TautColor = Color.FromHex("#e8d7a8");

    /// <summary>Drawn width in metres.</summary>
    [DataField]
    public float Width = 0.1f;

    /// <summary>Drawn instead of a plain strip, once per segment along the rope; u runs across it, v along it.</summary>
    [DataField]
    public ResPath? SegmentTexture;

    /// <summary>Width at end B as a share of the width at end A, so a tentacle narrows to its tip.</summary>
    [DataField]
    public float Taper = 1f;

    /// <summary>Verlet chain resolution. The point count is clamped to [8, 96].</summary>
    [DataField]
    public float SegmentsPerMetre = 3f;

    /// <summary>A repeating strand texture for the core, multiplied by the rope's colour. Null draws a flat strip.</summary>
    [DataField]
    public ResPath? Texture;

    /// <summary>Metres of rope one repeat of <see cref="Texture"/> covers.</summary>
    [DataField]
    public float TextureLength = 0.8f;

    /// <summary>How hard the drawn rope resists folding, from 0 (free to curl) to 1. Stiff cable bends in long curves.</summary>
    [DataField]
    public float BendStiffness;

    /// <summary>Colour of the sparks that run along the rope. Null has none.</summary>
    [DataField]
    public Color? SparkColor;

    /// <summary>Metres of rope to each spark.</summary>
    [DataField]
    public float SparkSpacing = 10f;

    /// <summary>Stack refunded when the rope is untied. Null refunds nothing.</summary>
    [DataField]
    public ProtoId<StackPrototype>? StackType;

    [DataField]
    public SoundSpecifier? BreakSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");
}
