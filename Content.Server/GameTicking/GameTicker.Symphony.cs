using System.Collections.Generic;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

// WOLFGATE(Symphony): the lobby background the SSymphony panel picks, held over every round until it is cleared.
public sealed partial class GameTicker
{
    /// <summary>The background the panel set, or null to pick one at random each round as upstream does.</summary>
    [ViewVariables]
    public string? LobbyBackgroundOverride { get; private set; }

    /// <summary>The backgrounds the prototypes offer, as RandomizeLobbyBackground picks from them.</summary>
    public IReadOnlyList<ResPath> LobbyBackgroundChoices => _lobbyBackgrounds ?? (IReadOnlyList<ResPath>) [];

    /// <summary>
    /// Show this background in the lobby from now on, or go back to a random one with null. Everyone is sent the
    /// new lobby status at once when <paramref name="announce"/> is set, so the lobby changes without a rejoin.
    /// </summary>
    public void SetLobbyBackgroundOverride(string? path, bool announce)
    {
        LobbyBackgroundOverride = path;
        if (path != null)
            LobbyBackground = path;
        else
            RandomizeLobbyBackground();
        if (announce)
            SendStatusToAll();
    }
}
