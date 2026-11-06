using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A shell that takes all damage meant for the mob until it breaks, then drops its loot.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaBreakableShellComponent : Component
{
    /// <summary>
    /// Damage the shell absorbs before it breaks. It cracks at half.
    /// </summary>
    [DataField]
    public float MaxHealth = 60f;

    /// <summary>
    /// Share of a projectile's damage the shell takes.
    /// </summary>
    [DataField]
    public float ProjectileFactor = 0.25f;

    /// <summary>
    /// Spawned when the shell breaks, or on death if it never did.
    /// </summary>
    [DataField]
    public List<EntProtoId> Loot = new();

    /// <summary>
    /// Played when the shell breaks.
    /// </summary>
    [DataField]
    public SoundSpecifier? BreakSound = new SoundCollectionSpecifier("GlassBreak");

    /// <summary>
    /// Damage the shell has absorbed so far.
    /// </summary>
    [DataField]
    public float Absorbed;

    /// <summary>
    /// Whether the shell has broken.
    /// </summary>
    [DataField]
    public bool Broken;
}
