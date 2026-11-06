using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a wind-up, damages and knocks down every mob in a cone in front of this one.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaSlamComponent : Component
{
    /// <summary>
    /// Dealt to each mob in the cone.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Damage dealt to anchored structures in the cone, such as walls and tables.
    /// </summary>
    [DataField]
    public DamageSpecifier? StructureDamage;

    /// <summary>
    /// Length of the cone, in tiles.
    /// </summary>
    [DataField]
    public float Range = 3f;

    /// <summary>
    /// Full width of the cone, in degrees.
    /// </summary>
    [DataField]
    public float Arc = 90f;

    /// <summary>
    /// Wind-up between using the action and the slam.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long each hit mob is knocked down.
    /// </summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Played at the mob when the slam lands.
    /// </summary>
    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>
    /// Tiles each hit mob is thrown away from the slammer. Zero throws nothing.
    /// </summary>
    [DataField]
    public float Throw;

    /// <summary>
    /// Marker spawned on every tile of the cone during the wind-up.
    /// </summary>
    [DataField]
    public EntProtoId? Telegraph;

    /// <summary>
    /// Visual effect spawned on every tile of the cone when the slam lands.
    /// </summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// Animated emote the mob plays when it starts the wind-up.
    /// </summary>
    [DataField]
    public ProtoId<EmotePrototype>? Emote;

    /// <summary>
    /// When the slam lands. Null when no wind-up is running.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? SlamAt;

    /// <summary>
    /// Direction the cone faces, aimed at the target when the wind-up starts.
    /// </summary>
    [ViewVariables]
    public Angle Direction;
}
