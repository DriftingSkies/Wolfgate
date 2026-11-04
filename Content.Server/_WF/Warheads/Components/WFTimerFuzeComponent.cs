namespace Content.Server._WF.Warheads.Components;

/// <summary>
/// Arms the warhead by hand: set a timer from a list, then a do-after starts the countdown. No code needed.
/// </summary>
[RegisterComponent]
public sealed partial class WFTimerFuzeComponent : Component
{
    /// <summary>Timers, in seconds, offered by the Set timer verbs.</summary>
    [DataField]
    public List<float> Options = new() { 30f, 60f, 120f, 300f, 600f };

    /// <summary>Do-after, in seconds, to start the countdown.</summary>
    [DataField]
    public float ArmDelay = 5f;
}
