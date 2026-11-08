using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

[Serializable, NetSerializable]
public enum WFPlanetBroadcastUiKey : byte
{
    Key,
}

/// <summary>
/// The planet broadcast console's world, how long until it can send again, the longest message, and whether its
/// outpost is under raid with how long until it can call for help again.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFPlanetBroadcastState(string world, float waitSeconds, int maxLength, bool raided, float distressWaitSeconds)
    : BoundUserInterfaceState
{
    /// <summary>The world it broadcasts to; empty off a world.</summary>
    public readonly string World = world;
    public readonly float WaitSeconds = waitSeconds;
    public readonly int MaxLength = maxLength;
    public readonly bool Raided = raided;
    public readonly float DistressWaitSeconds = distressWaitSeconds;
}

/// <summary>Sends an announcement to everyone on the console's world, with the outpost's position if asked.</summary>
[Serializable, NetSerializable]
public sealed class WFPlanetBroadcastMessage(string text, bool position) : BoundUserInterfaceMessage
{
    public readonly string Text = text;
    public readonly bool Position = position;
}

/// <summary>Calls for help across the sector while the console's outpost is under raid.</summary>
[Serializable, NetSerializable]
public sealed class WFPlanetDistressMessage : BoundUserInterfaceMessage;
