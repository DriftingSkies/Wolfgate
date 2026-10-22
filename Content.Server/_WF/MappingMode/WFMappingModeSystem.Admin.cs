using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.MappingMode;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    /// <summary>The private map admin observers start on, made on the first observe.</summary>
    private EntityUid? _adminMap;

    /// <summary>Admin observers spawned by observe, which Return to lobby takes back.</summary>
    private readonly HashSet<EntityUid> _adminObservers = new();

    private void InitializeAdmin()
    {
        SubscribeNetworkEvent<WFMappingAdminObserveRequest>(OnAdminObserve);
        SubscribeNetworkEvent<WFMappingAdminLeaveRequest>(OnAdminLeave);
        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
    }

    /// <summary>A forced round start loads no station while the mode is on.</summary>
    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        if (Enabled)
            ev.Maps.Clear();
    }

    private void OnAdminObserve(WFMappingAdminObserveRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !_admin.HasAdminFlag(player, AdminFlags.Admin))
            return;

        if (player.AttachedEntity != null || player.Status != SessionStatus.InGame || TryGetPresent(player, out _))
        {
            Refuse(player, "wf-mapping-mode-not-in-lobby");
            return;
        }

        var map = EnsureAdminMap();
        var observer = SpawnAttached(player, GameTicker.AdminObserverPrototypeName, Comp<MapComponent>(map).MapId);
        _adminObservers.Add(observer);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} observes mapping mode as {ToPrettyString(observer)} on the admin map");
        SendState(player);
    }

    /// <summary>Whether a player is on an admin observer that observe spawned.</summary>
    public bool IsAdminObserver(ICommonSession player)
    {
        if (player.AttachedEntity is not { } entity || !_adminObservers.Contains(entity))
            return false;

        if (!TerminatingOrDeleted(entity))
            return true;

        _adminObservers.Remove(entity);
        return false;
    }

    private EntityUid EnsureAdminMap()
    {
        if (_adminMap is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        var map = _map.CreateMap(out _, runMapInit: false);
        _meta.SetEntityName(map, Loc.GetString("wf-mapping-mode-admin-map-name"));
        RegisterMap(map, WFMappingMapKind.Admin, 0);
        _adminMap = map;
        return map;
    }

    private void OnAdminLeave(WFMappingAdminLeaveRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !_admin.HasAdminFlag(player, AdminFlags.Admin) || player.AttachedEntity is not { } entity)
            return;

        // A mapper leaves through their session instead, so it hands over ownership.
        if (TryGetPresent(player, out _))
            return;

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{player:player} left mapping mode observation");
        _adminObservers.Remove(entity);
        ReturnToLobby(player, entity);
    }

    /// <summary>Sends every admin observer, and anyone on the admin map, back to the lobby and deletes the map.</summary>
    private void ReturnAdminObservers()
    {
        var map = _adminMap;
        foreach (var player in _players.Sessions.ToList())
        {
            if (player.AttachedEntity is { } entity &&
                (_adminObservers.Contains(entity) || map != null && Transform(entity).MapUid == map))
            {
                ReturnToLobby(player, entity);
            }
        }

        if (map != null && !TerminatingOrDeleted(map.Value))
            QueueDel(map.Value);

        _adminMap = null;
        _adminObservers.Clear();
    }

    /// <summary>
    /// Deletes every entity of a prototype on a session's maps, with what is inside it. Players, ghosts and observers
    /// under one are moved off first, and one that is itself protected stays. The count deleted.
    /// </summary>
    public int Purge(WFMappingSession session, string prototype)
    {
        var found = new List<EntityUid>();
        var stack = new Stack<EntityUid>();
        foreach (var map in SessionMaps(session.Id))
        {
            stack.Push(map);
            while (stack.TryPop(out var uid))
            {
                if (uid != map && MetaData(uid).EntityPrototype?.ID == prototype && !_protection.IsProtected(uid))
                {
                    found.Add(uid);
                    continue;
                }

                var children = Transform(uid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    stack.Push(child);
                }
            }
        }

        foreach (var uid in found)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            _protection.MoveProtectedOff(uid);
            Del(uid);
        }

        if (found.Count > 0)
        {
            MarkDirty(session);
            session.CountedAt = TimeSpan.Zero;
        }

        return found.Count;
    }

    /// <summary>The ground level of a session, for admins going there.</summary>
    public MapId RootMapId(WFMappingSession session)
    {
        return Comp<MapComponent>(session.RootMap).MapId;
    }
}
