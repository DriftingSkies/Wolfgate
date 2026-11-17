namespace Content.Server._WG.Weapons.Ranged.Components;

/// <summary>
/// Tracks when a fast gun may next play its networked gunshot sound.
/// </summary>
[RegisterComponent]
public sealed partial class WGGunshotAudioThrottleComponent : Component
{
    /// <summary>When the next networked gunshot sound may play.</summary>
    [ViewVariables]
    public TimeSpan Next;
}
