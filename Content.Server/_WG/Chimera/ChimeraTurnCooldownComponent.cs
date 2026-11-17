namespace Content.Server._WG.Chimera;

/// <summary>
/// A body that just fell out of a dead fleshbeast. Letoferol can't turn it again until <see cref="ReadyAt"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(DropOriginalBodyOnDeathSystem))]
public sealed partial class ChimeraTurnCooldownComponent : Component
{
    /// <summary>When Letoferol can turn the body again.</summary>
    [DataField, AutoPausedField]
    public TimeSpan ReadyAt;
}
