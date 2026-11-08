using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Anyone who grabs or shoves the mob gets its melee hit and is thrown away.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaGrabRetortComponent : Component
{
    /// <summary>
    /// Least distance the grabber is thrown, in tiles.
    /// </summary>
    [DataField]
    public float MinThrow = 3f;

    /// <summary>
    /// Most distance the grabber is thrown, in tiles.
    /// </summary>
    [DataField]
    public float MaxThrow = 6f;

    /// <summary>
    /// Shown at the mob when it hits back.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Played at the mob when it hits back.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Dealt instead of the mob's melee damage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// How long the grabber is knocked down. Null for no knockdown.
    /// </summary>
    [DataField]
    public TimeSpan? Knockdown;
}
