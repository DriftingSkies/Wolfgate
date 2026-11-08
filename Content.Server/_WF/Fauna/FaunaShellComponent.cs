using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A hard shell that an explosion flips over, stunning the mob and using <see cref="FlippedModifierSet"/> for a while.
/// A retractable shell opens to <see cref="OpenModifierSet"/> each time the mob attacks.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaShellComponent : Component
{
    /// <summary>
    /// Used while the shell is closed and upright.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<DamageModifierSetPrototype> ShellModifierSet;

    /// <summary>
    /// Used while the mob is flipped over.
    /// </summary>
    [DataField]
    public ProtoId<DamageModifierSetPrototype>? FlippedModifierSet;

    /// <summary>
    /// How long a flip stuns the mob before it rights itself.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Whether an explosion flips the mob.
    /// </summary>
    [DataField]
    public bool FlipOnExplode = true;

    /// <summary>
    /// Used while the shell is open after an attack. Null means it never opens.
    /// </summary>
    [DataField]
    public ProtoId<DamageModifierSetPrototype>? OpenModifierSet;

    /// <summary>
    /// How long the shell stays open after each attack.
    /// </summary>
    [DataField]
    public TimeSpan OpenDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether the shell is open.
    /// </summary>
    [DataField]
    public bool Open;

    /// <summary>
    /// When the open shell closes.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan CloseAt;

    /// <summary>
    /// Whether the mob is flipped over.
    /// </summary>
    [DataField]
    public bool Flipped;

    /// <summary>
    /// When the flipped mob rights itself.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan RightAt;
}
