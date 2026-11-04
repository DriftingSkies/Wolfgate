using System.Diagnostics;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Waypoints;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    [Dependency] private WFCrewSleepSystem _wfCrewSleep = default!;
    [Dependency] private WFWaypointSystem _wfWaypoints = default!;

    // Window over which one sleep/wake pass is spread (30 tps x 2 s).
    private const int SleepPassTicks = 60;
    private const float SleepStatsLogInterval = 10f;

    private readonly List<EntityUid> _sleepQueue = new();
    private readonly Dictionary<MapId, List<Vector2>> _sleepPlayers = new();
    private int _sleepIndex;
    private int _sleepBatch;
    private double _sleepPassMs;
    private int _sleepPassCount;
    private float _sleepLogTimer;

    /// <summary>Snapshots the HTN NPCs and the player positions; <see cref="WfStepSleepPass"/> then walks them over the next ticks.</summary>
    private void WfStartSleepPass()
    {
        var sw = Stopwatch.StartNew();

        foreach (var list in _sleepPlayers.Values)
            list.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } playerEnt
                || HasComp<GhostComponent>(playerEnt)
                || TryComp<MobStateComponent>(playerEnt, out var state) && state.CurrentState != MobState.Alive)
                continue;

            var coords = _transform.ToMapCoordinates(WfPlayerPresence(playerEnt), logError: false);
            if (coords.MapId == MapId.Nullspace)
                continue;

            if (!_sleepPlayers.TryGetValue(coords.MapId, out var list))
            {
                list = new List<Vector2>();
                _sleepPlayers[coords.MapId] = list;
            }

            list.Add(coords.Position);
        }

        _sleepQueue.Clear();
        var query = EntityQueryEnumerator<HTNComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _sleepQueue.Add(uid);
        }

        _sleepIndex = 0;
        _sleepBatch = Math.Max(1, (_sleepQueue.Count + SleepPassTicks - 1) / SleepPassTicks);
        _sleepPassCount = _sleepQueue.Count;
        _sleepPassMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Processes the next slice of the snapshot taken by <see cref="WfStartSleepPass"/>.</summary>
    private void WfStepSleepPass(float frameTime)
    {
        _sleepLogTimer += frameTime;

        if (_sleepIndex >= _sleepQueue.Count)
            return;

        var sw = Stopwatch.StartNew();
        var end = Math.Min(_sleepQueue.Count, _sleepIndex + _sleepBatch);
        for (; _sleepIndex < end; _sleepIndex++)
        {
            WfCheckSleep(_sleepQueue[_sleepIndex]);
        }

        _sleepPassMs += sw.Elapsed.TotalMilliseconds;

        if (_sleepIndex >= _sleepQueue.Count && _sleepLogTimer >= SleepStatsLogInterval)
        {
            _sleepLogTimer = 0f;
            Log.Debug($"NPC sleep pass: {_sleepPassCount} NPCs, {_sleepPassMs:F2} ms total");
        }
    }

    private void WfCheckSleep(EntityUid npcUid)
    {
        if (!TryComp<HTNComponent>(npcUid, out var htn) || !TryComp(npcUid, out TransformComponent? npcTransform))
            return;

        if (HasComp<ActorComponent>(npcUid) ||
            (TryComp<MindContainerComponent>(npcUid, out var mindContainer) && mindContainer.HasMind))
            return;

        if (_mobState.IsIncapacitated(npcUid))
            return;

        var hasNearbyPlayer = _wfCrewSleep.StaysAwake(npcUid);
        hasNearbyPlayer |= _wfWaypoints.AutopilotEngaged(npcUid, htn);

        var minDistance = htn.SleepPlayerCheckRangeOverride ?? _playerPauseDistance;
        if (htn.SleepMaxGridSpeed is { } ms
            && TryComp<PhysicsComponent>(npcTransform.GridUid, out var gridBody)
            && gridBody.LinearVelocity.Length() > ms)
            hasNearbyPlayer = true;

        if (!hasNearbyPlayer)
        {
            var npcCoords = _transform.ToMapCoordinates(WfPlayerPresence(npcUid), logError: false);
            if (_sleepPlayers.TryGetValue(npcCoords.MapId, out var players))
            {
                var limit = minDistance * minDistance;
                foreach (var pos in players)
                {
                    if ((pos - npcCoords.Position).LengthSquared() <= limit)
                    {
                        hasNearbyPlayer = true;
                        break;
                    }
                }
            }
        }

        var isAwake = IsAwake(npcUid, htn);
        if (!hasNearbyPlayer)
        {
            if (isAwake)
                SleepNPC(npcUid, htn);
        }
        else if (!isAwake)
        {
            WakeNPC(npcUid, htn);
        }
    }
}
