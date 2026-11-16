namespace Content.Client._WF.InteractionParticles;

/// <summary>
/// Tracks the cooldown for interaction particles spawned for an entity.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class InteractionParticleTrackerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoPausedField]
    public TimeSpan ExpireTime;
}
