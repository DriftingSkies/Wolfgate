using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Licks the wounds of hurt packmates nearby, once per interval. Packmates are other mobs with this component, or the
/// members of <see cref="Faction"/> when it is set.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaPackHealComponent : Component
{
    /// <summary>
    /// Damage change applied to each hurt packmate per interval. Negative values heal.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Heal = new();

    /// <summary>
    /// Tiles from the mob that packmates are healed within.
    /// </summary>
    [DataField]
    public float Radius = 1.5f;

    /// <summary>
    /// Faction whose members count as packmates. Null means other mobs with this component.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? Faction;

    /// <summary>
    /// Time between heals.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Played on each packmate it heals.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// When the next heal runs.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
