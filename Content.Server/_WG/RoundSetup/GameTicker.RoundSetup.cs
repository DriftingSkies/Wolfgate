using Content.Server._WG.RoundSetup;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    /// <summary>Spawns a validated lobby member through the normal job and mind lifecycle.</summary>
    public bool WGTryRoundSetupSpawn(ICommonSession player, EntityUid station, string job, bool lateJoin)
    {
        if (!_playerGameStatuses.TryGetValue(player.UserId, out var status) || status == PlayerGameStatus.JoinedGame ||
            !_userDb.IsLoadComplete(player) || (lateJoin ? RunLevel != GameRunLevel.InRound || DisallowLateJoin : !_startingRound) ||
            !HasComp<StationSpawningComponent>(station) || !_prototypeManager.TryIndex<JobPrototype>(job, out var prototype) ||
            !_stationJobs.TryGetJobSlot(station, prototype, out var slots) || slots == 0 ||
            !EntityManager.System<WGRoundSetupSystem>().CanSpawnOnStation(player, station))
            return false;
        if (_adminManager.IsAdmin(player) && _cfg.GetCVar(CCVars.AdminDeadminOnJoin))
            _adminManager.DeAdmin(player);
        if (lateJoin)
            MakeJoinGame(player, station, job);
        else
            SpawnPlayer(player, station, job, lateJoin: false);
        return UserHasJoinedGame(player);
    }

    /// <summary>Allocates scenario and ordinary jobs together against the same round-start slot limits.</summary>
    private Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> WGAssignRoundSetupJobs(
        Dictionary<NetUserId, HumanoidCharacterProfile> profiles, List<EntityUid> stations, HashSet<NetUserId> players)
    {
        var choices = EntityManager.System<WGRoundSetupSystem>().GetRoundStartProfiles(profiles);
        var assigned = _stationJobs.AssignJobs(choices, stations);
        _stationJobs.AssignOverflowJobs(ref assigned, players, choices, stations);
        return assigned;
    }

    /// <summary>Keeps the ordinary spawn profile intact while authorizing a member's allocated job.</summary>
    private void WGSpawnAssignedPlayer(ICommonSession player, HumanoidCharacterProfile profile, EntityUid station, string job)
    {
        var setup = EntityManager.System<WGRoundSetupSystem>();
        if (!setup.BeginAssignedSpawn(player, station, job))
            return;
        try
        {
            SpawnPlayer(player, profile, station, job, false);
        }
        finally
        {
            setup.EndAssignedSpawn(player);
        }
    }
}
