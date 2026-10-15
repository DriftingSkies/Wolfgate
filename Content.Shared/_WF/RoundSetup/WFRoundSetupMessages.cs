using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>The crew starting conditions; Normal is reserved and cannot create a scenario lobby.</summary>
[Serializable, NetSerializable]
public enum WFRoundScenario : byte { Normal, Crashlanded, Ship }

/// <summary>How a player can enter a scenario lobby.</summary>
[Serializable, NetSerializable]
public enum WFLobbyAccess : byte { Public, Requests, Closed, Code, Invite }

/// <summary>Authenticated operations on the round setup service.</summary>
[Serializable, NetSerializable]
public enum WFRoundSetupAction : byte
{
    Refresh, Create, Configure, Join, Leave, AcceptRequest, RejectRequest,
    Invite, AcceptInvite, DeclineInvite, SelectJob, SetReady, Launch, RegenerateCode, Kick, TransferHost,
    SelectVessel, UpsertRole, RemoveRole, SelectRole,
}

/// <summary>A request whose actor is always taken from its network session.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupRequest : EntityEventArgs
{
    public WFRoundSetupAction Action;
    public int LobbyId;
    public WFRoundScenario Scenario;
    public WFLobbyAccess Access = WFLobbyAccess.Requests;
    public bool AllowLateJoin;
    public bool Ready;
    public NetUserId? Target;
    public string? Code;
    public string? Job;
    public NetEntity? Station;
    public string? Vessel;
    public int? RoleId;
    public string? RoleName;
    public int Capacity = 1;

    public WFRoundSetupRequest(WFRoundSetupAction action) { Action = action; }
}

/// <summary>A player's public identity in the setup interface.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupPlayer(NetUserId UserId, string Name, int? RoleId = null);

/// <summary>A member's own job and readiness, with no character profile data.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupMember(NetUserId UserId, string Name, string? Job, NetEntity? Station, bool Ready, bool Spawned, bool Connected, int? RoleId = null);

/// <summary>A job choice offered by the server for this player's scenario.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupJob(string Id, NetEntity? Station, string StationName);

/// <summary>A named crew role with server-checked capacity and eligibility for the recipient.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupRole(int Id, string Name, string Job, NetEntity? Station, int Capacity, int Occupied, bool CanSelect);

/// <summary>A ship offered for purchase when the host launches their crew.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupVessel(string Id, string Name, string Description, int Price);

/// <summary>A sanitized player-lobby listing; codes and requests are sent only to its host.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupLobby
{
    public int Id;
    public NetUserId Host;
    public string HostName = string.Empty;
    public WFRoundScenario Scenario;
    public WFLobbyAccess Access;
    public bool AllowLateJoin;
    public bool Started;
    public int MemberCount;
    public WFRoundSetupMember[] Members = Array.Empty<WFRoundSetupMember>();
    public WFRoundSetupPlayer[] Requests = Array.Empty<WFRoundSetupPlayer>();
    public string? Code;
    public string? Vessel;
    public WFRoundSetupRole[] Roles = Array.Empty<WFRoundSetupRole>();
}

/// <summary>A per-recipient snapshot of the round setup browser and their membership.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupState : EntityEventArgs
{
    public bool InRound;
    public bool CanJoin;
    public bool CrashlandedAvailable;
    public int? OwnLobby;
    public WFRoundSetupLobby[] Lobbies = Array.Empty<WFRoundSetupLobby>();
    public WFRoundSetupLobby[] Invitations = Array.Empty<WFRoundSetupLobby>();
    public WFRoundSetupPlayer[] InviteTargets = Array.Empty<WFRoundSetupPlayer>();
    public WFRoundSetupJob[] Jobs = Array.Empty<WFRoundSetupJob>();
    public WFRoundSetupVessel[] Vessels = Array.Empty<WFRoundSetupVessel>();
    public int? HostBalance;
    public string? Message;
}
