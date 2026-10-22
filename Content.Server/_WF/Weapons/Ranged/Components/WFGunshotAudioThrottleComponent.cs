namespace Content.Server._WF.Weapons.Ranged.Components;

/// <summary>
/// Tracks when a fast gun may next play its networked gunshot sound.
/// </summary>
[RegisterComponent]
public sealed partial class WFGunshotAudioThrottleComponent : Component
{
    /// <summary>When the next networked gunshot sound may play.</summary>
    [ViewVariables]
    public TimeSpan Next;
}
