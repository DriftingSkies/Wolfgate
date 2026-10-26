using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

// WOLFGATE(Symphony): the lobby background the SSymphony panel picks, held over every round until it is cleared.
public sealed partial class GameTicker
{
    /// <summary>The background the panel set, or null to pick one at random each round as upstream does.</summary>
    [ViewVariables]
    public string? LobbyBackgroundOverride { get; private set; }

    /// <summary>Backgrounds the panel set to be picked from at random each round, or empty for none.</summary>
    [ViewVariables]
    public IReadOnlyList<string> LobbyBackgroundPool { get; private set; } = [];

    /// <summary>The backgrounds the prototypes offer, as RandomizeLobbyBackground picks from them.</summary>
    public IReadOnlyList<ResPath> LobbyBackgroundChoices => _lobbyBackgrounds ?? (IReadOnlyList<ResPath>) [];

    /// <summary>
    /// Show this background in the lobby from now on, or go back to a random one with null. Everyone is sent the
    /// new lobby status at once when <paramref name="announce"/> is set, so the lobby changes without a rejoin.
    /// </summary>
    public void SetLobbyBackgroundOverride(string? path, bool announce)
    {
        LobbyBackgroundOverride = path;
        LobbyBackgroundPool = [];
        if (path != null)
            LobbyBackground = path;
        else
            RandomizeLobbyBackground();
        if (announce)
            SendStatusToAll();
    }

    /// <summary>
    /// Pick a background from this pool at random each round from now on, starting now. An empty pool goes back to the
    /// prototypes' own random pick.
    /// </summary>
    public void SetLobbyBackgroundPool(IReadOnlyList<string> pool, bool announce)
    {
        LobbyBackgroundOverride = null;
        LobbyBackgroundPool = pool.Distinct().ToList();
        RandomizeLobbyBackground();
        if (announce)
            SendStatusToAll();
    }

    /// <summary>A pick from the pool that is not the one just shown, while the pool has another to offer.</summary>
    private string? PickFromPool()
    {
        if (LobbyBackgroundPool.Count == 0)
            return null;
        var others = LobbyBackgroundPool.Where(p => p != LobbyBackground).ToList();
        return _robustRandom.Pick(others.Count > 0 ? others : LobbyBackgroundPool.ToList());
    }
}
