namespace Content.Server._WF.Warheads.Components;

/// <summary>
/// A grip switch. Once gripped it signals its linked warheads when its holder lets go: drops it, is disarmed, goes down
/// or dies. Easing off the grip takes a do-after.
/// </summary>
[RegisterComponent]
public sealed partial class WFDeadManTriggerComponent : Component
{
    [DataField]
    public string Port = "WFDeadManReleased";

    [DataField]
    public bool Gripped;

    [DataField]
    public EntityUid? Holder;

    /// <summary>Do-after, in seconds, to ease off the grip without setting it off.</summary>
    [DataField]
    public float ReleaseDelay = 3f;
}
