using Robust.Shared.Player;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    /// <summary>
    /// Sends a player back to the lobby without a respawn log or respawn reset, for leaving a mapping session.
    /// </summary>
    public void WFMappingReturnToLobby(ICommonSession session)
    {
        _mind.WipeMind(session);
        PlayerJoinLobby(session);
    }
}
