using Content.Shared._WG.JudgeLine;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._WG.JudgeLine;

/// <summary>The Judge Line PDA app.</summary>
[RegisterComponent]
public sealed partial class WGJudgeLineCartridgeComponent : Component;

/// <summary>On someone with a call open: who has hurt them since, so the judges know whom to stop.</summary>
[RegisterComponent]
public sealed partial class WGJudgeLineCallerComponent : Component
{
    /// <summary>Whoever hurt the caller, and when last.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Assailants = new();

    [ViewVariables]
    public TimeSpan? LastHurt;
}

/// <summary>One open call on the Judge Line.</summary>
public sealed class WGJudgeLineCall
{
    public int Id;
    public EntityUid Caller;
    public NetUserId? User;

    /// <summary>What the caller said was wrong.</summary>
    public WGJudgeLineKind Chosen;

    /// <summary>What the judges are dealing with: the chosen kind, or the real one they found instead.</summary>
    public WGJudgeLineKind Kind;

    public WGJudgeLinePhase Phase = WGJudgeLinePhase.Queued;

    /// <summary>The caller's own ship, if they called from one.</summary>
    public EntityUid? Grid;

    /// <summary>The caller's last known position.</summary>
    public MapCoordinates Position;

    public int Fee;
    public TimeSpan Placed;

    /// <summary>When the responder drops out of FTL, while dispatched.</summary>
    public TimeSpan DueAt;

    /// <summary>Whether any emergency was real when the call was made.</summary>
    public bool RealAtCall;

    public EntityUid? Encounter;
    public TimeSpan? Arrived;

    /// <summary>When the emergency was last seen still going on.</summary>
    public TimeSpan LastTrouble;

    /// <summary>When the responder docked with the ship it was sent to.</summary>
    public TimeSpan? Docked;

    /// <summary>When the responder heads off, once the emergency is over.</summary>
    public TimeSpan? LeaveAt;

    /// <summary>Guards sent across, and the post each came from.</summary>
    public readonly Dictionary<EntityUid, EntityCoordinates?> Posted = new();

    /// <summary>The station a picked-up caller is flown to.</summary>
    public EntityUid? Station;

    /// <summary>Stations that turned the responder away this trip.</summary>
    public readonly HashSet<EntityUid> Refused = new();

    /// <summary>Whether a picked-up caller has come aboard the responder.</summary>
    public bool Aboard;

    /// <summary>Whether the relief crate has been handed over.</summary>
    public bool Relief;

    /// <summary>Whether the judges have filled the stranded ship's plant themselves.</summary>
    public bool Refuelled;

    /// <summary>Whether the judges ran out of time on the emergency rather than seeing it through.</summary>
    public bool GaveUp;

    /// <summary>The grid the responder's orders send it to, or null when it was sent to pick the caller up.</summary>
    public EntityUid? OrderedTo;

    /// <summary>Where the responder was sent to pick the caller up.</summary>
    public System.Numerics.Vector2 Waypoint;

    /// <summary>The latest line from the responder, shown in the app.</summary>
    public string? Status;
}
