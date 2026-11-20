using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.JudgeLine;

/// <summary>The nature of an emergency a vagrant calls the judges to.</summary>
[Serializable, NetSerializable]
public enum WGJudgeLineKind : byte
{
    /// <summary>The caller's ship is being shot at by another ship.</summary>
    UnderFire,

    /// <summary>Hostiles are aboard the caller's ship.</summary>
    Boarded,

    /// <summary>The caller or a crewmate is badly hurt, critical or dead.</summary>
    Medical,

    /// <summary>The caller's ship has no thrust.</summary>
    Stranded,

    /// <summary>The caller is in space with no ship of their own.</summary>
    Adrift,

    /// <summary>The caller is attacked in person.</summary>
    Assault,
}

/// <summary>Where a call stands, as the caller sees it.</summary>
[Serializable, NetSerializable]
public enum WGJudgeLinePhase : byte
{
    None,
    Queued,
    Dispatched,
    OnScene,
    Leaving,
}

/// <summary>What the Judge Line app shows its holder.</summary>
[Serializable, NetSerializable]
public sealed class WGJudgeLineUiState : BoundUserInterfaceState
{
    /// <summary>Why the holder can't call now, or null if they can.</summary>
    public string? Refusal;

    public int Fee;
    public WGJudgeLinePhase Phase;
    public WGJudgeLineKind Kind;

    /// <summary>Seconds until the responder arrives, while dispatched.</summary>
    public int Eta;

    /// <summary>Place in the queue, while queued.</summary>
    public int Queue;

    /// <summary>The latest line from the responder.</summary>
    public string? Status;

    /// <summary>Whether the responder is out of FTL but not yet with the caller.</summary>
    public bool Inbound;
}

/// <summary>Places a call of a kind from the Judge Line app.</summary>
[Serializable, NetSerializable]
public sealed class WGJudgeLineCallMessage(WGJudgeLineKind kind) : CartridgeMessageEvent
{
    public readonly WGJudgeLineKind Kind = kind;
}

/// <summary>Cancels the holder's call from the Judge Line app.</summary>
[Serializable, NetSerializable]
public sealed class WGJudgeLineCancelMessage : CartridgeMessageEvent;
