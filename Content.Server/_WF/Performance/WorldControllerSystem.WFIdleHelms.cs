using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components;
using Content.Shared._WF.CCVar;
using Content.Shared.Ghost;
using Robust.Shared.Configuration;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    private bool _wfIdleHelmsSkip;
    private readonly HashSet<EntityUid> _wfActiveGrids = new();
    private readonly Stack<EntityUid> _wfPending = new();
    private TimeSpan _wfNextCountLog;
    private int _wfSkippedHelms;

    private void WFInitializeIdleHelms()
    {
        _wfCfg.OnValueChanged(WFIdleHelmCVars.IdleHelmsSkipWorldgen, v => _wfIdleHelmsSkip = v, true);
    }

    /// <summary>Collects grids with a player aboard or a piloted console, plus everything docked to them.</summary>
    private void WFCollectActiveGrids()
    {
        _wfActiveGrids.Clear();
        _wfSkippedHelms = 0;
        if (!_wfIdleHelmsSkip)
            return;

        _wfPending.Clear();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && !HasComp<GhostComponent>(uid) && _wfActiveGrids.Add(grid))
                _wfPending.Push(grid);
        }

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out var console, out var xform))
        {
            if (console.SubscribedPilots.Count > 0 && xform.GridUid is { } grid && _wfActiveGrids.Add(grid))
                _wfPending.Push(grid);
        }

        while (_wfPending.TryPop(out var current))
        {
            if (!TryComp<JointComponent>(current, out var joints))
                continue;

            foreach (var joint in joints.GetJoints.Values)
            {
                var other = joint.BodyAUid == current ? joint.BodyBUid : joint.BodyAUid;
                if (Transform(other).GridUid is { } otherGrid && _wfActiveGrids.Add(otherGrid))
                    _wfPending.Push(otherGrid);
            }
        }
    }

    /// <summary>True for a shuttle console on a grid nobody is on or piloting, which loads nothing.</summary>
    private bool WFIdleHelm(EntityUid uid, TransformComponent xform)
    {
        if (!_wfIdleHelmsSkip || !HasComp<ShuttleConsoleComponent>(uid))
            return false;

        if (xform.GridUid is not { } grid || _wfActiveGrids.Contains(grid))
            return false;

        _wfSkippedHelms++;
        return true;
    }

    /// <summary>Logs how many chunks are loaded and how many idle helms were skipped, at most every 10 s.</summary>
    private void WFLogChunkCount()
    {
        var now = _gameTiming.CurTime;
        if (now < _wfNextCountLog)
            return;

        _wfNextCountLog = now + TimeSpan.FromSeconds(10);
        _sawmill.Debug($"{EntityManager.Count<LoadedChunkComponent>()} loaded chunks, {_wfSkippedHelms} idle helms skipped.");
    }
}
