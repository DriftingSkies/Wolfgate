using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lashes vines onto hostile mobs in sight, knocks them down and drags them a tile closer every interval.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaVineComponent : Component
{
    /// <summary>
    /// Beam drawn from the mob to each vined target.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Beam;

    /// <summary>
    /// Most targets the mob can hold with vines at once.
    /// </summary>
    [DataField]
    public int MaxVines = 4;

    /// <summary>
    /// Tiles within which the mob vines a hostile it can see.
    /// </summary>
    [DataField]
    public float Range = 5f;

    /// <summary>
    /// Time between vining one target and the next.
    /// </summary>
    [DataField]
    public TimeSpan AttachCooldown = TimeSpan.FromSeconds(4.5);

    /// <summary>
    /// Time between pulls on every vined target.
    /// </summary>
    [DataField]
    public TimeSpan PullInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Knockdown applied to a target when a vine attaches.
    /// </summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Popup shown when a vine attaches.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// When no new target can be vined, pulls every vined target in at once and retries after half the cooldown.
    /// </summary>
    [DataField]
    public bool YankAll;

    /// <summary>
    /// Targets currently held by a vine.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Targets = new();

    /// <summary>
    /// When the mob next tries to vine a target.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextAttach;

    /// <summary>
    /// When the vined targets are next pulled.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextPull;
}
