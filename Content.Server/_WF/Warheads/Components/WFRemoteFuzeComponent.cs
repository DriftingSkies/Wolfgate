namespace Content.Server._WF.Warheads.Components;

/// <summary>
/// Once its safety is off, a signal on the Detonate port starts a short countdown. Pairs with a remote signaller
/// or a dead-man trigger.
/// </summary>
[RegisterComponent]
public sealed partial class WFRemoteFuzeComponent : Component
{
    [DataField]
    public string Port = "WFWarheadDetonate";

    /// <summary>Do-after, in seconds, to take the safety off or put it back on.</summary>
    [DataField]
    public float SafetyDelay = 5f;

    /// <summary>Countdown, in seconds, a signal starts.</summary>
    [DataField]
    public float SignalTimer = 10f;
}
