using System.Collections.Generic;
using System.Linq;
using Content.Shared.Audio.Events;
using Robust.Shared.Player;

namespace Content.Server.Audio;

// WOLFGATE(Symphony): the lobby playlist the SSymphony panel picks, played instead of the shuffled collection.
public sealed partial class ContentAudioSystem
{
    /// <summary>The playlist the panel set, in its order, or null to shuffle the collection each round.</summary>
    [ViewVariables]
    public string[]? LobbyPlaylistOverride { get; private set; }

    /// <summary>Whether the panel's playlist is shuffled into a new order each round rather than played as set.</summary>
    [ViewVariables]
    public bool LobbyPlaylistShuffled { get; private set; }

    /// <summary>What the lobby plays now, first track first.</summary>
    public IReadOnlyList<string> LobbyPlaylist => _lobbyPlaylist ?? [];

    /// <summary>Every track the lobby collection offers.</summary>
    public IReadOnlyList<string> LobbyCollection => _lobbyMusicCollection.PickFiles.Select(x => x.ToString()).ToList();

    /// <summary>
    /// Play this playlist in the lobby from now on, or go back to the shuffled collection with null. The players
    /// named in <paramref name="tell"/> hear it at once; anyone else is sent it when they next reach the lobby.
    /// </summary>
    public void SetLobbyPlaylistOverride(string[]? playlist, IEnumerable<ICommonSession>? tell, bool shuffle = false)
    {
        LobbyPlaylistOverride = playlist is { Length: > 0 } ? playlist : null;
        LobbyPlaylistShuffled = LobbyPlaylistOverride != null && shuffle;
        _lobbyPlaylist = ShuffleLobbyPlaylist();
        if (tell == null)
            return;
        var sessions = tell.ToList();
        if (sessions.Count > 0)
            RaiseNetworkEvent(new LobbyPlaylistChangedEvent(_lobbyPlaylist), Filter.Empty().AddPlayers(sessions));
    }
}
