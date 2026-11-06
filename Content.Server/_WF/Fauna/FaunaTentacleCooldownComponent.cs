namespace Content.Server._WF.Fauna;

/// <summary>
/// Links a mob's NPCUseActionOnTarget tentacle action to hits and visuals: each hit taken cuts its cooldown, and
/// <see cref="Content.Shared._WF.Fauna.FaunaTentacleVisuals.Ready"/> is set while it is off cooldown.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaTentacleCooldownComponent : Component
{
    /// <summary>
    /// Taken off the remaining cooldown per hit.
    /// </summary>
    [DataField]
    public TimeSpan HitCut = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the action was off cooldown at the last check.
    /// </summary>
    [ViewVariables]
    public bool Ready;
}
