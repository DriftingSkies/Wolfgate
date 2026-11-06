using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns <see cref="Effect"/> on a pattern of tiles around this entity, then deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaBlastPatternComponent : Component
{
    /// <summary>
    /// Entity spawned on each tile of the pattern.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Effect;

    /// <summary>
    /// Layout of the tiles around the centre.
    /// </summary>
    [DataField]
    public FaunaBlastShape Shape = FaunaBlastShape.Square;

    /// <summary>
    /// Square and ring radius, or the length of each cross arm.
    /// </summary>
    [DataField]
    public int Radius = 1;

    /// <summary>
    /// Delay between each ring of a square, so it grows outward. Zero spawns it all at once.
    /// </summary>
    [DataField]
    public TimeSpan RingDelay = TimeSpan.Zero;

    /// <summary>
    /// Which arms a cross has.
    /// </summary>
    [DataField]
    public FaunaBlastCross Cross = FaunaBlastCross.Cardinal;

    /// <summary>
    /// Chance of a diagonal cross when <see cref="Cross"/> is random.
    /// </summary>
    [DataField]
    public float DiagonalChance = 0.4f;

    /// <summary>
    /// Effect spawned at the centre when the pattern is laid out. A cross uses it when it has both arms.
    /// </summary>
    [DataField]
    public EntProtoId? Telegraph;

    /// <summary>
    /// Effect spawned at the centre of a cross with only cardinal arms.
    /// </summary>
    [DataField]
    public EntProtoId? TelegraphCardinal;

    /// <summary>
    /// Effect spawned at the centre of a cross with only diagonal arms.
    /// </summary>
    [DataField]
    public EntProtoId? TelegraphDiagonal;

    /// <summary>
    /// Tile offsets still to spawn <see cref="Effect"/> on, with when each spawns.
    /// </summary>
    [ViewVariables]
    public List<(TimeSpan At, Vector2i Tile)> Pending = new();
}

/// <summary>
/// Tile layout of a <see cref="FaunaBlastPatternComponent"/>.
/// </summary>
public enum FaunaBlastShape : byte
{
    Square,
    Cross,
    Ring,
}

/// <summary>
/// Which arms a cross pattern has.
/// </summary>
public enum FaunaBlastCross : byte
{
    Cardinal,
    Diagonal,
    All,
    Random,
}
