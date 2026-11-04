using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warheads;

[Serializable, NetSerializable]
public enum WFWarheadUiKey : byte
{
    Key,
}

/// <summary>Everything the warhead control panel shows.</summary>
[Serializable, NetSerializable]
public sealed class WFWarheadBoundUserInterfaceState : BoundUserInterfaceState
{
    public WFWarheadState State;

    /// <summary>When the countdown reaches zero, while counting.</summary>
    public TimeSpan DetonateAt;

    /// <summary>Seconds the countdown starts from.</summary>
    public float Timer;

    public float FinalPhase;

    /// <summary>Loc ID of the yield class shown in the header.</summary>
    public string YieldClass = string.Empty;

    public bool HasTimerFuze;
    public List<float> TimerOptions = new();

    public bool HasTwoPerson;
    public float KeyWindow;

    /// <summary>Name of whoever turned the first key, while the window is open.</summary>
    public string? FirstKeyHolder;
    public TimeSpan FirstKeyTime;

    public bool HasRemote;
    public int RemoteLinks;
    public float SignalTimer;

    public bool HasImpact;

    /// <summary>Rough blast radius in tiles; zero when the payload has no blast.</summary>
    public float BlastRadius;
    public float FlashRange;
    public float EmpRange;
    public float FalloutRadius;
    public int FalloutSources;
    public bool DeletesGrid;

    public string DisarmQuality = string.Empty;
}

/// <summary>Sets the timer, while the safety is on.</summary>
[Serializable, NetSerializable]
public sealed class WFWarheadSetTimerMessage(float seconds) : BoundUserInterfaceMessage
{
    public readonly float Seconds = seconds;
}

/// <summary>Starts the timer fuze's countdown.</summary>
[Serializable, NetSerializable]
public sealed class WFWarheadArmMessage : BoundUserInterfaceMessage;

/// <summary>Turns the user's key.</summary>
[Serializable, NetSerializable]
public sealed class WFWarheadTurnKeyMessage : BoundUserInterfaceMessage;

/// <summary>Puts the safety on or takes it off.</summary>
[Serializable, NetSerializable]
public sealed class WFWarheadSafetyMessage(bool safe) : BoundUserInterfaceMessage
{
    public readonly bool Safe = safe;
}
