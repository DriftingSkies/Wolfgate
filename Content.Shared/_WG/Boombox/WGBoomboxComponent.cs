using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Boombox;

/// <summary>
/// A portable speaker that plays pasted links out of itself, one after another from its own playlist.
/// Clients get short labels only; the links stay on the server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGBoomboxComponent : Component
{
    /// <summary>Most links that may wait behind the playing track, and most a playlist link adds.</summary>
    public const int MaxQueued = 10;

    /// <summary>The playing track: its title once known, otherwise its shortened link.</summary>
    [ViewVariables, AutoNetworkedField]
    public WGBoomboxEntry? Current;

    /// <summary>The waiting links in play order.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<WGBoomboxEntry> Queue = new();

    /// <summary>A finished track goes back to the end of the queue.</summary>
    [DataField, AutoNetworkedField]
    public bool Repeat;

    /// <summary>A playlist link is being read.</summary>
    [ViewVariables, AutoNetworkedField]
    public bool Reading;

    [DataField]
    public int MaxUrlLength = 512;

    /// <summary>Server only. When a track may next be started from quiet.</summary>
    [ViewVariables]
    public TimeSpan NextRequest;

    /// <summary>Server only. When the queue may next be skipped, since every skip starts a fresh fetch.</summary>
    [ViewVariables]
    public TimeSpan NextSkip;

    /// <summary>Server only. The link behind <see cref="Current"/>.</summary>
    [ViewVariables]
    public WGBoomboxLink? Playing;

    /// <summary>Server only. The waiting links; <see cref="Queue"/> is built from it.</summary>
    [ViewVariables]
    public List<WGBoomboxLink> Links = new();
}

/// <summary>A track or waiting link as clients see it.</summary>
[Serializable, NetSerializable]
public readonly record struct WGBoomboxEntry(int Id, string Label, int Seconds);

/// <summary>Server only. A link waiting its turn.</summary>
public sealed class WGBoomboxLink
{
    public int Id;
    public string Url = string.Empty;
    public string Label = string.Empty;
    public int Seconds;
    public string Requester = string.Empty;
    public ICommonSession? Session;
}

[Serializable, NetSerializable]
public enum WGBoomboxVisuals : byte
{
    Playing,
}

[Serializable, NetSerializable]
public enum WGBoomboxUiKey : byte
{
    Key,
}

/// <summary>Play a link now if quiet, otherwise queue it. A playlist link adds its playable tracks.</summary>
[Serializable, NetSerializable]
public sealed class WGBoomboxAddMessage(string url) : BoundUserInterfaceMessage
{
    public readonly string Url = url;
}

[Serializable, NetSerializable]
public sealed class WGBoomboxSkipMessage : BoundUserInterfaceMessage;

/// <summary>Stops the track and empties the queue.</summary>
[Serializable, NetSerializable]
public sealed class WGBoomboxStopMessage : BoundUserInterfaceMessage;

/// <summary>Empties the queue only.</summary>
[Serializable, NetSerializable]
public sealed class WGBoomboxClearMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGBoomboxShuffleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGBoomboxRepeatMessage(bool repeat) : BoundUserInterfaceMessage
{
    public readonly bool Repeat = repeat;
}

[Serializable, NetSerializable]
public sealed class WGBoomboxRemoveMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

/// <summary>Moves a waiting link one place earlier.</summary>
[Serializable, NetSerializable]
public sealed class WGBoomboxMoveUpMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

/// <summary>Volume as a fraction of full, 0 to 1.</summary>
[Serializable, NetSerializable]
public sealed class WGBoomboxVolumeMessage(float volume) : BoundUserInterfaceMessage
{
    public readonly float Volume = volume;
}
