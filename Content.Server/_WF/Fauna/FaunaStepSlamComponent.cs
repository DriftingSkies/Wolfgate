using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// When the mob steps onto a new tile, it hurts and throws every mob near it, then waits out a cooldown.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaStepSlamComponent : Component
{
    /// <summary>
    /// Tiles from the mob that a slam reaches.
    /// </summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// Range while the mob is enraged.
    /// </summary>
    [DataField]
    public float EnragedRange = 2.5f;

    /// <summary>
    /// Dealt to each mob the slam hits.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Tiles each hit mob is thrown away from the mob.
    /// </summary>
    [DataField]
    public float Throw = 8f;

    /// <summary>
    /// Time after a slam before the next one.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Played at the mob when it slams.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Shown to each mob the slam hits.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// When the mob can slam again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextSlam;

    /// <summary>
    /// Tile the mob stood on at the last check, so a slam fires only on a new tile.
    /// </summary>
    [ViewVariables]
    public Vector2i? LastTile;
}
