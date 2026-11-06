using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Plays a cry and a popup when the mob's NPC picks a new living target.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaAggroCryComponent : Component
{
    /// <summary>
    /// Played when the mob picks a new target.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Popup shown when the mob picks a new target.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Minimum time between cries.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Target the mob last cried at.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastTarget;

    /// <summary>
    /// When the mob may cry again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextCry;
}
