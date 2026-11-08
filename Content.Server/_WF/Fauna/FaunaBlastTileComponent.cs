using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a delay, hurts every living mob on this tile except blast casters.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaBlastTileComponent : Component
{
    /// <summary>
    /// Time between the tile spawning and its strike.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(0.6);

    /// <summary>
    /// Dealt to each mob on the tile when it strikes.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Played on each mob the strike hits.
    /// </summary>
    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>
    /// When the tile strikes.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan StrikeAt;

    /// <summary>
    /// Set once the tile has struck, so it strikes only once.
    /// </summary>
    [ViewVariables]
    public bool Struck;
}
