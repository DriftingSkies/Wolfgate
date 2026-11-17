using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components;
using Content.Shared._WG.CCVar;
using Content.Shared.Ghost;
using Robust.Shared.Configuration;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    private bool _wgIdleHelmsSkip;
    private readonly HashSet<EntityUid> _wgActiveGrids = new();
    private readonly Stack<EntityUid> _wgPending = new();
    private TimeSpan _wgNextCountLog;
    private int _wgSkippedHelms;

    private void WGInitializeIdleHelms()
    {
        _wgCfg.OnValueChanged(WGIdleHelmCVars.IdleHelmsSkipWorldgen, v => _wgIdleHelmsSkip = v, true);
    }

    /// <summary>Collects grids with a player aboard or a piloted console, plus everything docked to them.</summary>
    private void WGCollectActiveGrids()
    {
        _wgActiveGrids.Clear();
        _wgSkippedHelms = 0;
        if (!_wgIdleHelmsSkip)
            return;

        _wgPending.Clear();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && !HasComp<GhostComponent>(uid) && _wgActiveGrids.Add(grid))
                _wgPending.Push(grid);
        }

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out var console, out var xform))
        {
            if (console.SubscribedPilots.Count > 0 && xform.GridUid is { } grid && _wgActiveGrids.Add(grid))
                _wgPending.Push(grid);
        }

        while (_wgPending.TryPop(out var current))
        {
            if (!TryComp<JointComponent>(current, out var joints))
                continue;

            foreach (var joint in joints.GetJoints.Values)
            {
                var other = joint.BodyAUid == current ? joint.BodyBUid : joint.BodyAUid;
                if (Transform(other).GridUid is { } otherGrid && _wgActiveGrids.Add(otherGrid))
                    _wgPending.Push(otherGrid);
            }
        }
    }

    /// <summary>True for a shuttle console on a grid nobody is on or piloting, which loads nothing.</summary>
    private bool WGIdleHelm(EntityUid uid, TransformComponent xform)
    {
        if (!_wgIdleHelmsSkip || !HasComp<ShuttleConsoleComponent>(uid))
            return false;

        if (xform.GridUid is not { } grid || _wgActiveGrids.Contains(grid))
            return false;

        _wgSkippedHelms++;
        return true;
    }

    /// <summary>Logs how many chunks are loaded and how many idle helms were skipped, at most every 10 s.</summary>
    private void WGLogChunkCount()
    {
        var now = _gameTiming.CurTime;
        if (now < _wgNextCountLog)
            return;

        _wgNextCountLog = now + TimeSpan.FromSeconds(10);
        _sawmill.Debug($"{EntityManager.Count<LoadedChunkComponent>()} loaded chunks, {_wgSkippedHelms} idle helms skipped.");
    }
}
