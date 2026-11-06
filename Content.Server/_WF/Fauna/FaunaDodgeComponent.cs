using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets projectiles pass through the mob with a chance equal to its dodge pool, which drains as it dodges.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDodgeComponent : Component
{
    /// <summary>
    /// Current dodge pool. Dodge chance is this over <see cref="MaxPool"/>.
    /// </summary>
    [DataField]
    public float Pool = 100f;

    /// <summary>
    /// Largest the dodge pool can grow.
    /// </summary>
    [DataField]
    public float MaxPool = 100f;

    /// <summary>
    /// Pool regained per second.
    /// </summary>
    [DataField]
    public float Regen = 0.6f;

    /// <summary>
    /// Chance that a dodge costs pool.
    /// </summary>
    [DataField]
    public float UseChance = 0.55f;

    /// <summary>
    /// Pool spent on a dodge that costs pool.
    /// </summary>
    [DataField]
    public float Usage = 8f;

    /// <summary>
    /// Steps the mob one tile aside on each dodge that costs pool.
    /// </summary>
    [DataField]
    public bool SideStep;

    /// <summary>
    /// Played on each dodge.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Popup shown on each dodge.
    /// </summary>
    [DataField]
    public LocId Popup = "fauna-dodge";

    /// <summary>
    /// Whether each projectile seen so far was dodged, since a contact is checked every physics step.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, bool> Seen = new();
}
