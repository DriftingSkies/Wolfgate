using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.GameHub;

/// <summary>Transfer stream carrying game data: a header, then the file.</summary>
public static class WGGameHubProtocol
{
    public const string TransferKey = "WGGameData";
    public const int MaxHeaderBytes = 1024;

    /// <summary>Longest sound lump name a client may report.</summary>
    public const int MaxNoiseName = 8;

    /// <summary>How close a player must be to the cabinet or PDA they play on.</summary>
    public const float PlayRange = 3f;

    /// <summary>How far away other players hear a game.</summary>
    public const float HearRange = 10f;
}

public enum WGGameDataMessage : byte
{
    /// <summary>The client asks for a game's size and version.</summary>
    ManifestRequest,

    /// <summary>The server's answer, with <see cref="MsgWGGameData.Size"/> and <see cref="MsgWGGameData.Version"/>.</summary>
    Manifest,

    /// <summary>The client asks the server to stream a game's data to it.</summary>
    DownloadRequest,

    /// <summary>The server couldn't read or send the data.</summary>
    DownloadFailed,
}

/// <summary>
/// Game data requests and answers. A net message rather than an entity event, so a client waiting in the join queue,
/// which gets no game states and so dispatches no entity events, can still fetch a game.
/// </summary>
public sealed class MsgWGGameData : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public WGGameDataMessage Kind;
    public string Game = string.Empty;
    public long Size;
    public string Version = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Kind = (WGGameDataMessage) buffer.ReadByte();
        Game = buffer.ReadString();
        Size = buffer.ReadInt64();
        Version = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Kind);
        buffer.Write(Game);
        buffer.Write(Size);
        buffer.Write(Version);
    }
}

/// <summary>The player started or changed their game's music on a cabinet or PDA. Null stops broadcasting.</summary>
[Serializable, NetSerializable]
public sealed class WGGameBroadcastEvent(NetEntity source, string game, string? track, bool loop, bool stopped) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string? Track = track;
    public readonly bool Loop = loop;
    public readonly bool Stopped = stopped;
}

/// <summary>A sound the player's game made, for the server to pass on to people nearby.</summary>
[Serializable, NetSerializable]
public sealed class WGGameNoiseEvent(NetEntity source, string game, string sound, byte volume) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string Sound = sound;
    public readonly byte Volume = volume;
}

/// <summary>Another player's game made a sound near you.</summary>
[Serializable, NetSerializable]
public sealed class WGGameNoiseHeardEvent(NetEntity source, string game, string sound, byte volume) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string Sound = sound;
    public readonly byte Volume = volume;
}

/// <summary>The player finished a level. Client-reported, so the server caps what it can earn.</summary>
[Serializable, NetSerializable]
public sealed class WGGameLevelCompleteEvent(
    NetEntity source,
    string game,
    int level,
    int killPercent,
    int itemPercent,
    int secretPercent,
    int seconds) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly int Level = level;
    public readonly int KillPercent = killPercent;
    public readonly int ItemPercent = itemPercent;
    public readonly int SecretPercent = secretPercent;
    public readonly int Seconds = seconds;
}

[Serializable, NetSerializable]
public sealed class WGGameLeaderboardRequestEvent(string game) : EntityEventArgs
{
    public readonly string Game = game;
}

[Serializable, NetSerializable]
public sealed class WGGameLeaderboardEvent(string game, List<WGGameLeaderboardEntry> entries) : EntityEventArgs
{
    public readonly string Game = game;
    public readonly List<WGGameLeaderboardEntry> Entries = entries;
}
