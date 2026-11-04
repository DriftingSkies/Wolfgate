using System.Numerics;
using Content.Shared._Mono.Radar;
using Content.Server.GameTicking;

using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>An enabled blip and where it is this tick.</summary>
    public readonly record struct WFBlip(EntityUid Uid, RadarBlipComponent Blip, TransformComponent Xform, PhysicsComponent Physics, Vector2 Position);

    private static readonly List<WFBlip> WFNoBlips = new();
    private readonly Dictionary<MapId, List<WFBlip>> _wfBlipsByMap = new();
    private readonly List<Vector2> _wfSourcePositions = new();
    private GameTick _wfBlipsTick = GameTick.Zero;

    /// <summary>The enabled blips on a map, walked once a tick however many radars ask.</summary>
    public List<WFBlip> WFBlipsOnMap(MapId map)
    {
        if (_wfBlipsTick != _wfTiming.CurTick)
        {
            _wfBlipsTick = _wfTiming.CurTick;
            foreach (var list in _wfBlipsByMap.Values)
            {
                list.Clear();
            }

            var query = EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var blip, out var xform, out var physics))
            {
                if (!blip.Enabled)
                    continue;

                if (!_wfBlipsByMap.TryGetValue(xform.MapID, out var list))
                    _wfBlipsByMap[xform.MapID] = list = new List<WFBlip>();

                list.Add(new WFBlip(uid, blip, xform, physics, _xform.GetWorldPosition(xform)));
            }
        }

        return _wfBlipsByMap.GetValueOrDefault(map, WFNoBlips);
    }

    /// <summary>Takes the radar's source positions once, instead of once per blip.</summary>
    private void WFNoteSources(List<EntityUid> sources)
    {
        _wfSourcePositions.Clear();
        foreach (var source in sources)
        {
            _wfSourcePositions.Add(_xform.GetWorldPosition(source));
        }
    }

    private bool WFNearAnySource(Vector2 position, float range)
    {
        var squared = range * range;
        foreach (var source in _wfSourcePositions)
        {
            if ((source - position).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    private static readonly TimeSpan WFRequestWindow = TimeSpan.FromSeconds(3);

    [Dependency] private GameTicker _wfTicker = default!;

    private readonly Dictionary<ICommonSession, TimeSpan> _wfLastRequest = new();
    private readonly List<ICommonSession> _wfStale = new();

    /// <summary>Records that a session is looking at a radar.</summary>
    private void WFNoteRequest(ICommonSession session)
    {
        _wfLastRequest[session] = _wfTiming.CurTime;
    }

    /// <summary>Tells recent radar users a blip is gone; nobody else is drawing it.</summary>
    private void WFSendRemoval(NetEntity blip)
    {
        if (_wfTicker.RunLevel == GameRunLevel.PreRoundLobby || _wfLastRequest.Count == 0)
            return;

        var now = _wfTiming.CurTime;
        var filter = Filter.Empty();
        var any = false;
        _wfStale.Clear();
        foreach (var (session, last) in _wfLastRequest)
        {
            if (now - last > WFRequestWindow || session.Status == SessionStatus.Disconnected)
            {
                _wfStale.Add(session);
                continue;
            }

            filter.AddPlayer(session);
            any = true;
        }

        foreach (var session in _wfStale)
        {
            _wfLastRequest.Remove(session);
        }

        if (any)
            RaiseNetworkEvent(new BlipRemovalEvent(blip), filter);
    }
}
