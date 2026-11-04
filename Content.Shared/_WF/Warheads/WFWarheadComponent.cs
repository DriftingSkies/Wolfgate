using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warheads;

/// <summary>
/// A nuclear warhead: its arming state and the payload it sets off. Arming mechanisms are separate components.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFWarheadComponent : Component
{
    [DataField]
    public ProtoId<WFWarheadPayloadPrototype>? Payload;

    /// <summary>A bespoke payload from the admin designer, used instead of <see cref="Payload"/>.</summary>
    [DataField]
    public WFWarheadPayload? CustomPayload;

    [DataField, AutoNetworkedField]
    public WFWarheadState State = WFWarheadState.Safe;

    /// <summary>Seconds the countdown runs once started, including the final phase.</summary>
    [DataField, AutoNetworkedField]
    public float Timer = 120f;

    /// <summary>Last seconds of the countdown, when the alarm sounds and the warhead can no longer be disarmed.</summary>
    [DataField]
    public float FinalPhase = 10f;

    /// <summary>Do-after, in seconds, for a multitool to disarm a counting warhead.</summary>
    [DataField]
    public float DisarmDelay = 30f;

    /// <summary>Tool quality that disarms it.</summary>
    [DataField]
    public string DisarmQuality = "Pulsing";

    /// <summary>When the countdown reaches zero.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan DetonateAt;

    [DataField]
    public bool FinalPhaseStarted;

    /// <summary>Loc ID of the yield class the control panel shows.</summary>
    [DataField]
    public LocId YieldClass = "wf-warhead-class-field";
}

[Serializable, NetSerializable]
public enum WFWarheadState : byte
{
    /// <summary>Safety on.</summary>
    Safe,

    /// <summary>Safety off; waits for a remote or impact trigger.</summary>
    Armed,

    /// <summary>Counting down; can still be disarmed.</summary>
    Countdown,

    /// <summary>Last seconds; cannot be stopped.</summary>
    Final,
}

[Serializable, NetSerializable]
public enum WFWarheadVisuals : byte
{
    State,
}
