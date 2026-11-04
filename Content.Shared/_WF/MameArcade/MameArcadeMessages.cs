using Robust.Shared.Serialization;

namespace Content.Shared._WF.MameArcade;

[Serializable, NetSerializable]
public enum MameArcadeUiKey : byte
{
    Key,
}

/// <summary>Limits the server enforces on what clients send.</summary>
public static class MameArcadeProtocol
{
    public const int MaxGameName = 32;
    public const int MaxFingerprint = 64;

    /// <summary>Most frames of input one message may carry.</summary>
    public const int MaxInputsPerMessage = 32;

    /// <summary>
    /// Input frames plus messages one seat may send a second. A player sends about 60 of each; more than this is a
    /// flood, and the server drops it.
    /// </summary>
    public const int MaxInputCostPerSecond = 240;

    /// <summary>No checksum in this message.</summary>
    public const int NoChecksum = -1;

    /// <summary>
    /// Largest sound or picture packet. A typical keyframe is about 30 KiB; this fits even an incompressible CPS2
    /// keyframe (384x224 at 2 bytes a pixel), so no real screen is ever too big to send.
    /// </summary>
    public const int MaxStreamPacket = 192 * 1024;

    /// <summary>Most sound a cabinet may relay, in bytes per second; the game sends about 12 KiB.</summary>
    public const int MaxAudioBytesPerSecond = 16 * 1024;

    /// <summary>How far away other players hear a cabinet.</summary>
    public const float HearRange = 10f;
}

/// <summary>The cabinet's 2-player lobby and whose game it shows, sent to everyone with it open.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeBuiState(
    NetEntity? host,
    string hostName,
    string game,
    string fingerprint,
    NetEntity? guest,
    string guestName,
    MameArcadeShowing showing) : BoundUserInterfaceState
{
    public readonly NetEntity? Host = host;
    public readonly string HostName = hostName;
    public readonly string Game = game;
    public readonly string Fingerprint = fingerprint;
    public readonly NetEntity? Guest = guest;
    public readonly string GuestName = guestName;
    public readonly MameArcadeShowing Showing = showing;
}

/// <summary>
/// The game on the cabinet's screen and speaker. Its player streams the sound to people nearby, and the picture
/// while anyone watches, up to <see cref="VideoRate"/> bytes a second.
/// </summary>
[Serializable, NetSerializable]
public sealed class MameArcadeShowing(
    NetEntity? player,
    string playerName,
    string game,
    int watchers,
    int joins,
    int maxWatchers,
    int videoRate)
{
    public readonly NetEntity? Player = player;
    public readonly string PlayerName = playerName;
    public readonly string Game = game;
    public readonly int Watchers = watchers;

    /// <summary>Counts up whenever someone starts watching, who needs a keyframe to start from.</summary>
    public readonly int Joins = joins;

    public readonly int MaxWatchers = maxWatchers;

    /// <summary>0 when the server turned spectating off.</summary>
    public readonly int VideoRate = videoRate;
}

/// <summary>Offers a 2-player game. The fingerprint says which ROMs and BIOS the host's copy uses.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeHostMessage(string game, string fingerprint) : BoundUserInterfaceMessage
{
    public readonly string Game = game;
    public readonly string Fingerprint = fingerprint;
}

[Serializable, NetSerializable]
public sealed class MameArcadeJoinMessage(string fingerprint) : BoundUserInterfaceMessage
{
    public readonly string Fingerprint = fingerprint;
}

/// <summary>Started a 1-player game; it shows on the cabinet unless another game already does.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeSoloMessage(string game) : BoundUserInterfaceMessage
{
    public readonly string Game = game;
}

/// <summary>Starts or stops watching the game on the cabinet's screen.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeWatchMessage(bool watching) : BoundUserInterfaceMessage
{
    public readonly bool Watching = watching;
}

/// <summary>Cancels a lobby, or stops or leaves a game.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeLeaveMessage : BoundUserInterfaceMessage;

/// <summary>Both players start the game from power-on. Seat 0 is player 1.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeStartMessage(int seat, int delay, string partnerName) : BoundUserInterfaceMessage
{
    public readonly int Seat = seat;
    public readonly int Delay = delay;
    public readonly string PartnerName = partnerName;
}

/// <summary>The other player left; play on alone.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadePartnerLeftMessage : BoundUserInterfaceMessage;
