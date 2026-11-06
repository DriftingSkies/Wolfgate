using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a wind-up, spawns an effect on each tile of a line toward the target, one tile at a time.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLineAttackComponent : Component
{
    /// <summary>
    /// Spawned on each tile of the line as it is drawn.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Effect;

    /// <summary>
    /// Most tiles the line reaches. It stops early at a blocked tile.
    /// </summary>
    [DataField]
    public int Range = 3;

    /// <summary>
    /// Fires only along a cardinal direction, and only when the target stands within half a tile of that line.
    /// </summary>
    [DataField]
    public bool Cardinal;

    /// <summary>
    /// Turns each effect to face along the line, so directional beam states show the matching direction.
    /// </summary>
    [DataField]
    public bool RotateEffects;

    /// <summary>
    /// Time between using the action and the line firing.
    /// </summary>
    [DataField]
    public TimeSpan Windup = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Delay between tiles. Zero spawns the whole line at once.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// Spawned over the whole line <see cref="StrikeDelay"/> after it is drawn, so the first effects can be a
    /// harmless warning.
    /// </summary>
    [DataField]
    public EntProtoId? Strike;

    /// <summary>
    /// Time from the last drawn tile to the <see cref="Strike"/> spawns.
    /// </summary>
    [DataField]
    public TimeSpan StrikeDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Replace the first and last effect of the line, for beam caps.
    /// </summary>
    [DataField]
    public EntProtoId? EffectStart;

    /// <summary>
    /// Replaces <see cref="Effect"/> on the last tile of the line.
    /// </summary>
    [DataField]
    public EntProtoId? EffectEnd;

    /// <summary>
    /// Replace the first and last strike of the line.
    /// </summary>
    [DataField]
    public EntProtoId? StrikeStart;

    /// <summary>
    /// Replaces <see cref="Strike"/> on the last tile of the line.
    /// </summary>
    [DataField]
    public EntProtoId? StrikeEnd;

    /// <summary>
    /// How long the mob keeps its firing look after the line is done.
    /// </summary>
    [DataField]
    public TimeSpan FiringTime;

    /// <summary>
    /// Played when the <see cref="Strike"/> spawns land.
    /// </summary>
    [DataField]
    public SoundSpecifier? StrikeSound;

    /// <summary>
    /// Played when the wind-up starts.
    /// </summary>
    [DataField]
    public SoundSpecifier? WindupSound;

    /// <summary>
    /// Played when the wind-up ends and the line starts.
    /// </summary>
    [DataField]
    public SoundSpecifier? FireSound;

    /// <summary>
    /// Popup shown on the mob when the wind-up starts.
    /// </summary>
    [DataField]
    public LocId? WindupPopup;

    /// <summary>
    /// Shakes the mob this hard during the wind-up. 0 for no shake.
    /// </summary>
    [DataField]
    public float WindupJitter;

    /// <summary>
    /// Entity the line is aimed at when the wind-up ends.
    /// </summary>
    [DataField]
    public EntityUid? Target;

    /// <summary>
    /// When the wind-up ends and the line fires. Null when no wind-up is running.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? FireAt;

    /// <summary>
    /// Tiles of the line still to be drawn, nearest first.
    /// </summary>
    [ViewVariables]
    public Queue<EntityCoordinates> Tiles = new();

    /// <summary>
    /// Direction of the current line, used by <see cref="RotateEffects"/>.
    /// </summary>
    [ViewVariables]
    public Angle LineAngle;

    /// <summary>
    /// Drawn tiles waiting for the <see cref="Strike"/> spawns.
    /// </summary>
    [ViewVariables]
    public List<EntityCoordinates> StrikeTiles = new();

    /// <summary>
    /// When the <see cref="Strike"/> spawns land.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan StrikeAt;

    /// <summary>
    /// When the mob drops its firing look. Zero when not firing.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan IdleAt;

    /// <summary>
    /// Whether the next drawn tile is the first of the line.
    /// </summary>
    [ViewVariables]
    public bool FirstTile;

    /// <summary>
    /// When the next tile of the line is drawn.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextStep;
}
