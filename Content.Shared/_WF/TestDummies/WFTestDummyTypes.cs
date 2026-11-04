using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.TestDummies;

/// <summary>Who gets asked. Every set flag must hold for a player to be asked.</summary>
[Flags]
public enum WFTestDummyAudience : byte
{
    None = 0,

    /// <summary>Holds a deed to a ship that still exists, or is registered as an owner of one.</summary>
    OwnsShip = 1 << 0,

    /// <summary>In sector space: not on a planet, in FTL, on an expedition or inside a vehicle.</summary>
    InSpace = 1 << 1,

    /// <summary>Playing a body, not a ghost or in the lobby.</summary>
    InBody = 1 << 2,

    /// <summary>Leaves out players who hold any admin flag.</summary>
    SkipAdmins = 1 << 3,

    Default = OwnsShip | InSpace | InBody | SkipAdmins,
}

/// <summary>What a player said to the request.</summary>
public enum WFTestDummyAnswer : byte
{
    Pending,
    OptedIn,
    OptedOut,
}

/// <summary>Everything the admin filled in: what is being tested and who to ask.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyRequest
{
    public string Description = string.Empty;
    public WFTestDummyAudience Audience = WFTestDummyAudience.Default;

    /// <summary>Seconds the prompt stays up; zero keeps it until answered.</summary>
    public float Duration = 120f;
}

/// <summary>One asked player on the admin's list.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyEntry
{
    public NetUserId User;
    public string PlayerName = string.Empty;
    public string CharacterName = string.Empty;
    public string ShipName = string.Empty;
    public WFTestDummyAnswer Answer;
    public bool Connected = true;
}

/// <summary>The prompt shown to a player.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyPromptEvent(int callId, string description, float duration) : EntityEventArgs
{
    public readonly int CallId = callId;
    public readonly string Description = description;
    public readonly float Duration = duration;
}

/// <summary>A player's answer.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyAnswerEvent(int callId, bool optedIn) : EntityEventArgs
{
    public readonly int CallId = callId;
    public readonly bool OptedIn = optedIn;
}

/// <summary>The call is over; the client takes the prompt down.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyClosedEvent(int callId) : EntityEventArgs
{
    public readonly int CallId = callId;
}

/// <summary>The admin window's view: the current call, if any, and everyone asked.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyEuiState : EuiStateBase
{
    public bool Active;
    public string Description = string.Empty;
    public WFTestDummyAudience Audience;
    public List<WFTestDummyEntry> Entries = new();
    public string? Status;
    public bool Error;
}

/// <summary>Sends the request to the matching players.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummySendMessage(WFTestDummyRequest request) : EuiMessageBase
{
    public readonly WFTestDummyRequest Request = request;
}

/// <summary>Ends the current call: unanswered prompts close and the list is kept.</summary>
[Serializable, NetSerializable]
public sealed class WFTestDummyEndMessage : EuiMessageBase;

/// <summary>Console command names.</summary>
public static class WFTestDummyCommands
{
    public const string Open = "testdummies";
}

/// <summary>Limits shared by the window and the server's checks.</summary>
public static class WFTestDummyLimits
{
    public const int MaxDescription = 300;
    public const float MaxDuration = 1800f;
}
