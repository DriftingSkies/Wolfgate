using Robust.Shared.Serialization;

namespace Content.Shared._WG.RoundSetup;

/// <summary>An open crew invitation shown as a card to players who belong to no crew.</summary>
[Serializable, NetSerializable]
public sealed class WGCrewPostingEvent(int lobbyId, string crew, string host, WGRoundScenario scenario, WGLobbyAccess access,
    int openings, string message) : EntityEventArgs
{
    public int LobbyId = lobbyId;
    public string Crew = crew;
    public string Host = host;
    public WGRoundScenario Scenario = scenario;
    public WGLobbyAccess Access = access;
    public int Openings = openings;
    public string Message = message;
}

/// <summary>Withdraws a crew's posting cards, after an admin removes it or the crew closes.</summary>
[Serializable, NetSerializable]
public sealed class WGCrewPostingRevokedEvent(int lobbyId) : EntityEventArgs
{
    public int LobbyId = lobbyId;
}
