using Content.Server._WG.Performance;
using Content.Shared._WG.Performance;
using System.Numerics;
using Content.Shared._Mono.Radar;
using Content.Server.GameTicking;

using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem
{
    [Dependency] private IGameTiming _wgTiming = default!;

    /// <summary>An enabled blip and where it is this tick.</summary>
    public readonly record struct WGBlip(EntityUid Uid, RadarBlipComponent Blip, TransformComponent Xform, PhysicsComponent Physics, Vector2 Position);

    /// <summary>A blip in a square, with its world position as of tick At.</summary>
    private record struct WGEntry(EntityUid Uid, RadarBlipComponent Blip, TransformComponent Xform, PhysicsComponent Physics, Vector2 Position, GameTick At);

    /// <summary>Side of a square, in metres. Most blips are seen from several of these away.</summary>
    private const float WGCellSize = 1024f;

    /// <summary>
    /// Fastest a blip is assumed to move, in metres a second (the fastest shell is 1100, plus the ship firing it).
    /// Squares are reached this much further per second since the sort, so a blip that left its square isn't missed.
    /// </summary>
    private const float WGMaxBlipSpeed = 2500f;

    /// <summary>The blips in one square of a map at the last sort, and the furthest away any of them is seen from.</summary>
    private sealed class WGCell
    {
        public MapId Map;
        public Box2 Bounds;
        public float MaxRange;
        public readonly List<WGEntry> Blips = new();
    }

    private readonly Dictionary<(MapId, Vector2i), WGCell> _wgCellIndex = new();
    private readonly List<WGCell> _wgCells = new();
    private int _wgCellsUsed;
    private readonly List<WGBlip> _wgInReach = new();
    private readonly List<WGBlip> _wgOnMap = new();
    private readonly List<Entity<RadarBlipComponent>> _wgNew = new();
    private readonly List<Vector2> _wgSourcePositions = new();
    private GameTick _wgBlipsTick = GameTick.Zero;

    /// <summary>Blips put in the report being built, so missile arcs go only with missiles the radar is sent.</summary>
    private readonly HashSet<EntityUid> _wgReported = new();

    /// <summary>Whether any part of a hitscan line passes within <paramref name="range"/> of a source.</summary>
    public static bool WGLineInReach(Vector2 start, Vector2 end, IReadOnlyList<Vector2> sources, float range)
    {
        var squared = range * range;
        var line = end - start;
        var length = line.LengthSquared();
        foreach (var source in sources)
        {
            var along = length > 0f ? Math.Clamp(Vector2.Dot(source - start, line) / length, 0f, 1f) : 0f;
            if ((start + line * along - source).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Sorts every blip into squares every <see cref="WGRadarCVars.RebuildTicks"/> ticks. The squares only say roughly
    /// where to look: readers re-check each blip and take its position live (<see cref="WGTryLive"/>).
    /// </summary>
    private void WGRefreshBlips()
    {
        if (WGIndexFresh())
        {
            WGFileNew();
            return;
        }

        _wgNew.Clear();
        _wgBlipsTick = _wgTiming.CurTick;
        _wgCellIndex.Clear();
        for (var i = 0; i < _wgCellsUsed; i++)
        {
            _wgCells[i].Blips.Clear();
        }

        _wgCellsUsed = 0;

        // Disabled blips are kept, so one switched on shows up without waiting for the next sort. One not yet started
        // is left to WGOnBlipStartup, so it isn't filed twice.
        var query = EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var blip, out var xform, out var physics))
        {
            if (blip.LifeStage >= ComponentLifeStage.Starting)
                WGFile(uid, blip, xform, physics);
        }
    }

    private int WGRebuildTicks()
    {
        return Math.Clamp(_wgCfg.GetCVar(WGRadarCVars.RebuildTicks), 1, 30);
    }

    /// <summary>Puts a blip in the square it is in now.</summary>
    private void WGFile(EntityUid uid, RadarBlipComponent blip, TransformComponent xform, PhysicsComponent physics)
    {
        var position = _xform.GetWorldPosition(xform);
        var square = new Vector2i((int) MathF.Floor(position.X / WGCellSize), (int) MathF.Floor(position.Y / WGCellSize));
        if (!_wgCellIndex.TryGetValue((xform.MapID, square), out var cell))
        {
            if (_wgCellsUsed == _wgCells.Count)
                _wgCells.Add(new WGCell());

            cell = _wgCells[_wgCellsUsed++];
            cell.Map = xform.MapID;
            var corner = new Vector2(square.X, square.Y) * WGCellSize;
            cell.Bounds = new Box2(corner, corner + new Vector2(WGCellSize, WGCellSize));
            cell.MaxRange = 0f;
            _wgCellIndex[(xform.MapID, square)] = cell;
        }

        cell.MaxRange = MathF.Max(cell.MaxRange, blip.MaxDistance);
        cell.Blips.Add(new WGEntry(uid, blip, xform, physics, position, _wgTiming.CurTick));
    }

    /// <summary>
    /// Queues a new blip to be filed by the next reader, so a fresh shell isn't left off radar until the next sort.
    /// Filing waits for the reader because whoever added the blip may still be setting its range.
    /// </summary>
    private void WGOnBlipStartup(Entity<RadarBlipComponent> ent, ref ComponentStartup args)
    {
        // A stale index is rebuilt by the next reader anyway; queueing for it would only pile up entries.
        if (WGIndexFresh())
            _wgNew.Add(ent);
    }

    /// <summary>Files the blips started since the last read, unless removed again.</summary>
    private void WGFileNew()
    {
        foreach (var (uid, blip) in _wgNew)
        {
            if (!blip.Deleted && TryComp(uid, out PhysicsComponent? physics))
                WGFile(uid, blip, Transform(uid), physics);
        }

        _wgNew.Clear();
    }

    private bool WGIndexFresh()
    {
        var now = _wgTiming.CurTick;
        return _wgBlipsTick != GameTick.Zero && now >= _wgBlipsTick && now.Value - _wgBlipsTick.Value < WGRebuildTicks();
    }

    /// <summary>
    /// The blip's current state, or false if it is gone, switched off or on another map since the sort. Its position is
    /// taken once a tick, however many readers ask.
    /// </summary>
    private bool WGTryLive(List<WGEntry> entries, int index, MapId map, out WGBlip live)
    {
        var entry = entries[index];
        live = default;
        if (entry.Blip.LifeStage >= ComponentLifeStage.Stopping || entry.Physics.Deleted || !entry.Blip.Enabled
            || entry.Xform.MapID != map)
            return false;

        var now = _wgTiming.CurTick;
        if (entry.At != now)
        {
            entry.Position = _xform.GetWorldPosition(entry.Xform);
            entry.At = now;
            entries[index] = entry;
        }

        live = new WGBlip(entry.Uid, entry.Blip, entry.Xform, entry.Physics, entry.Position);
        return true;
    }

    /// <summary>The enabled blips on a map now. The list is reused by the next call.</summary>
    public List<WGBlip> WGBlipsOnMap(MapId map)
    {
        WGRefreshBlips();
        _wgOnMap.Clear();
        for (var i = 0; i < _wgCellsUsed; i++)
        {
            var cell = _wgCells[i];
            if (cell.Map != map)
                continue;

            for (var j = 0; j < cell.Blips.Count; j++)
            {
                if (WGTryLive(cell.Blips, j, map, out var blip))
                    _wgOnMap.Add(blip);
            }
        }

        return _wgOnMap;
    }

    /// <summary>
    /// The blips on a map that are seen from any of the sources. Squares out of every source's reach are skipped
    /// whole, so a request costs the blips near it, not every blip on the map. The list is reused by the next call.
    /// </summary>
    public List<WGBlip> WGBlipsInReach(MapId map, List<EntityUid> sources)
    {
        WGRefreshBlips();
        WGNoteSources(sources);
        _wgInReach.Clear();
        // How far a blip may have left its square since the sort.
        var drift = WGMaxBlipSpeed * (float) ((_wgTiming.CurTick.Value - _wgBlipsTick.Value) * _wgTiming.TickPeriod.TotalSeconds);
        for (var i = 0; i < _wgCellsUsed; i++)
        {
            var cell = _wgCells[i];
            if (cell.Map != map || !WGCellInReach(cell, drift))
                continue;

            for (var j = 0; j < cell.Blips.Count; j++)
            {
                if (WGTryLive(cell.Blips, j, map, out var blip)
                    && WGNearAnySource(blip.Position, blip.Blip.MaxDistance))
                    _wgInReach.Add(blip);
            }
        }

        return _wgInReach;
    }

    /// <summary>Whether any source is close enough to the square, grown by drift, to see its furthest-seen blip.</summary>
    private bool WGCellInReach(WGCell cell, float drift)
    {
        var reach = cell.MaxRange + drift;
        var squared = reach * reach;
        foreach (var source in _wgSourcePositions)
        {
            var nearest = Vector2.Clamp(source, cell.Bounds.BottomLeft, cell.Bounds.TopRight);
            if ((source - nearest).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    /// <summary>Takes the radar's source positions once, instead of once per blip.</summary>
    private void WGNoteSources(List<EntityUid> sources)
    {
        _wgSourcePositions.Clear();
        foreach (var source in sources)
        {
            _wgSourcePositions.Add(_xform.GetWorldPosition(source));
        }
    }

    private bool WGNearAnySource(Vector2 position, float range)
    {
        var squared = range * range;
        foreach (var source in _wgSourcePositions)
        {
            if ((source - position).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    private static readonly TimeSpan WGRequestWindow = TimeSpan.FromSeconds(3);

    [Dependency] private GameTicker _wgTicker = default!;

    [Dependency] private IConfigurationManager _wgCfg = default!;

    private readonly Dictionary<ICommonSession, TimeSpan> _wgLastRequest = new();
    private readonly Dictionary<ICommonSession, TimeSpan> _wgNextAnswer = new();
    private readonly List<ICommonSession> _wgStale = new();

    /// <summary>Records that a session is looking at a radar.</summary>
    private void WGNoteRequest(ICommonSession session)
    {
        _wgLastRequest[session] = _wgTiming.CurTime;
    }

    /// <summary>
    /// Whether to answer a session's request now: no more than <see cref="WGRadarCVars.MaxRequestHz"/> a second, in
    /// real time so a slow tick doesn't count against the player. The client's own throttle can't be relied on.
    /// </summary>
    public bool WGTakeRequest(ICommonSession session)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(_wgCfg.GetCVar(WGRadarCVars.MaxRequestHz), 0.5f, 30f));
        TimeSpan? due = _wgNextAnswer.TryGetValue(session, out var at) ? at : null;
        // Requests are handled on tick boundaries, so one may land up to a tick early.
        if (!WGRequestCap.Take(ref due, _wgTiming.RealTime, interval, _wgTiming.TickPeriod))
            return false;

        _wgNextAnswer[session] = due.GetValueOrDefault();
        return true;
    }

    private readonly List<NetEntity> _wgRemoved = new();

    /// <summary>Notes a blip that went away; recent radar users are told of the tick's removals together.</summary>
    private void WGSendRemoval(NetEntity blip)
    {
        if (_wgTicker.RunLevel == GameRunLevel.PreRoundLobby || _wgLastRequest.Count == 0)
            return;

        _wgRemoved.Add(blip);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wgRemoved.Count == 0)
            return;

        WGFlushRemovals();
        _wgRemoved.Clear();
    }

    /// <summary>Tells recent radar users which blips are gone; nobody else is drawing them.</summary>
    private void WGFlushRemovals()
    {

        var now = _wgTiming.CurTime;
        var filter = Filter.Empty();
        var any = false;
        _wgStale.Clear();
        foreach (var (session, last) in _wgLastRequest)
        {
            if (now - last > WGRequestWindow || session.Status == SessionStatus.Disconnected)
            {
                _wgStale.Add(session);
                continue;
            }

            filter.AddPlayer(session);
            any = true;
        }

        foreach (var session in _wgStale)
        {
            _wgLastRequest.Remove(session);
            _wgNextAnswer.Remove(session);
        }

        if (any)
            RaiseNetworkEvent(new WGBlipRemovalBatchEvent(new List<NetEntity>(_wgRemoved)), filter);
    }
}
