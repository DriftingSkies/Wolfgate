using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Plays a pain sound or spawns something at the mob when it takes damage, at most once per <see cref="Cooldown"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaHurtComponent : Component
{
    /// <summary>
    /// Played at the mob when it takes damage.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Spawned at the mob when it takes damage.
    /// </summary>
    [DataField]
    public EntProtoId? Spawn;

    /// <summary>
    /// Least time between two reactions.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When the mob can react again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextHurt;
}
