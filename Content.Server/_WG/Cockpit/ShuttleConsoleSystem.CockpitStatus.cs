using System.Linq;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WG.Cockpit;
using Content.Shared.Construction.Components;
using Content.Shared.NPC;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private IGameTiming _wgCockpitTiming = default!;

    private float _wgCockpitStatusAccumulator;
    private readonly Dictionary<EntityUid, bool?> _wgCockpitAutopilotStates = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, GameTick>> _wgCockpitStatusViewers = new();

    /// <summary>Queues fresh lamps for each real helm open, including a reopen between status ticks.</summary>
    public void WgCockpitConsoleOpened(EntityUid console, EntityUid actor)
    {
        if (!_wgCockpitStatusViewers.TryGetValue(console, out var viewers))
            _wgCockpitStatusViewers[console] = viewers = new Dictionary<EntityUid, GameTick>();
        viewers[actor] = _wgCockpitTiming.CurTick;
        _wgShieldHelmStates.Remove(console);
        // The cached helm state can predate a drive change made while the helm was closed.
        if (TryComp<ShuttleConsoleComponent>(console, out var helm))
            RefreshWgFtlDrive(console, helm);
    }

    /// <summary>Checks active steering rather than an old destination or the destination selection button.</summary>
    public bool? GetWgCockpitAutopilotStatus(EntityUid? console)
    {
        if (console is not { } uid || !TryComp<HTNComponent>(uid, out var htn) ||
            !TryComp<ShuttleConsoleComponent>(uid, out var helm))
            return null;
        return htn.Enabled && HasComp<ActiveNPCComponent>(uid) &&
            TryComp<ShipSteererComponent>(uid, out var steering) && steering.Status == ShipSteeringStatus.Moving &&
            htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, EntityManager) &&
            !TerminatingOrDeleted(target.EntityId) &&
            TryComp(uid, out TransformComponent? transform) && transform.GridUid is { } grid &&
            (!HasComp<AnchorableComponent>(uid) || transform.Anchored) && this.IsPowered(uid, EntityManager) &&
            HasComp<ShuttleComponent>(grid) && TryComp<PilotedShuttleComponent>(grid, out var piloted) &&
            piloted.InputSources.Contains(uid);
    }

    /// <summary>Refreshes only changed autopilot lamps on open helms, independently of the selected MFD.</summary>
    private void UpdateWgCockpitStatuses(float frameTime)
    {
        _wgCockpitStatusAccumulator += frameTime;
        if (_wgCockpitStatusAccumulator < 0.25f)
            return;
        _wgCockpitStatusAccumulator = 0;
        var tick = _wgCockpitTiming.CurTick;
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;
            open.Add(uid);
            var selected = new ConsoleShuttleEvent { Console = uid };
            RaiseLocalEvent(uid, ref selected);
            var active = GetWgCockpitAutopilotStatus(selected.Console);
            var changed = !_wgCockpitAutopilotStates.TryGetValue(uid, out var previous) || previous != active;
            if (changed)
            {
                _wgCockpitAutopilotStates[uid] = active;
                _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WGCockpitAutopilotUpdateMessage(active));
            }
            if (!_wgCockpitStatusViewers.TryGetValue(uid, out var viewers))
                continue;
            // A client can drop a message sent in the tick its window is created, so wait one tick.
            foreach (var (actor, opened) in viewers.ToArray())
            {
                if (opened >= tick)
                    continue;
                viewers.Remove(actor);
                if (!changed && _ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key, actor))
                    _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WGCockpitAutopilotUpdateMessage(active), actor);
            }
            if (viewers.Count == 0)
                _wgCockpitStatusViewers.Remove(uid);
        }
        foreach (var uid in _wgCockpitAutopilotStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
        {
            _wgCockpitAutopilotStates.Remove(uid);
        }
        foreach (var uid in _wgCockpitStatusViewers.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _wgCockpitStatusViewers.Remove(uid);
    }
}
