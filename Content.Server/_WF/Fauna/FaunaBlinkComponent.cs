using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets the mob teleport to a free tile at a set distance from its target. It can also teleport on its own while
/// idle, away from melee hits, or right after it shouts.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaBlinkComponent : Component
{
    /// <summary>
    /// Tiles between the target and where the mob lands.
    /// </summary>
    [DataField]
    public int Distance = 3;

    /// <summary>
    /// Played when the mob teleports.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    /// <summary>
    /// Spawned where the mob leaves and where it lands.
    /// </summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// Fades the mob out where it leaves and back in where it lands.
    /// </summary>
    [DataField]
    public bool Fade;

    /// <summary>
    /// Time between random teleports while the mob has no target. Null turns them off.
    /// </summary>
    [DataField]
    public TimeSpan? IdleInterval;

    /// <summary>
    /// Chance to teleport away from whoever hits the mob in melee.
    /// </summary>
    [DataField]
    public float HitChance;

    /// <summary>
    /// Teleports next to the target each time the mob's shout action fires.
    /// </summary>
    [DataField]
    public bool AfterShout;

    /// <summary>
    /// When the next idle teleport happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextIdle;
}
