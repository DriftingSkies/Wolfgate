using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Server.Chat;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Singularity.Components;
using Content.Server.Spawners.Components;
using Content.Server.StationEvents.Components;
using Content.Server.Warps;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Points;
using Content.Shared.Radio.Components;
using Content.Shared.Singularity.Components;
using Content.Shared._FarHorizons.StarSystem;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private readonly WFMappingThrottle<NetUserId> _editThrottle = new(60, 60);
    private readonly WFMappingThrottle<NetUserId> _toolsThrottle = new(10, 10);
    private readonly WFMappingThrottle<NetUserId> _joinThrottle = new(1 / 3.0, 1);
    private readonly WFMappingThrottle<NetUserId> _kickThrottle = new(1, 3);
    private readonly WFMappingThrottle<NetUserId> _listThrottle = new(2, 2);
    private readonly WFMappingThrottle<NetUserId> _pickThrottle = new(1 / 5.0, 1);
    private readonly WFMappingThrottle<NetUserId> _confirmThrottle = new(1, 1);

    /// <summary>How long a counted entity total is trusted before it is counted again.</summary>
    private static readonly TimeSpan CountLifetime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Components that reach past the session or its paused map: global PVS overrides, server-wide announcements,
    /// roles, rules and events, singularity behaviour. Nothing carrying one goes onto a mapper's map. Some more are
    /// denied only with certain data; see <see cref="DeniesComponent"/>.
    /// </summary>
    private readonly HashSet<string> _deniedComponents = new();

    private readonly List<Type> _deniedTypes = new();

    /// <summary>How deep a chain of spawn-on-despawn prototypes is followed before it counts as denied.</summary>
    private const int MaxSpawnChain = 4;

    private void InitializeLimits()
    {
        _deniedComponents.Add(_factory.GetComponentName<SingularityDistortionComponent>());
        _deniedComponents.Add(_factory.GetComponentName<PointManagerComponent>());
        _deniedComponents.Add(_factory.GetComponentName<GameRuleComponent>());
        _deniedComponents.Add(_factory.GetComponentName<StarSystemMapComponent>());
        _deniedComponents.Add(_factory.GetComponentName<SingularityComponent>());
        _deniedComponents.Add(_factory.GetComponentName<EventHorizonComponent>());
        _deniedComponents.Add(_factory.GetComponentName<GravityWellComponent>());
        _deniedComponents.Add(_factory.GetComponentName<AnnounceOnSpawnComponent>());
        _deniedComponents.Add(_factory.GetComponentName<GhostRoleComponent>());
        _deniedComponents.Add(_factory.GetComponentName<GhostRoleMobSpawnerComponent>());
        _deniedComponents.Add(_factory.GetComponentName<StationEventComponent>());
        foreach (var name in _deniedComponents)
        {
            _deniedTypes.Add(_factory.GetRegistration(name).Type);
        }

        Subs.CVar(_cfg, WFMappingModeCVars.MaxEditsPerSecond, rate =>
        {
            _editThrottle.Rate = Math.Max(1, rate);
            _editThrottle.Burst = Math.Max(1, rate);
        }, true);
    }

    private bool TryThrottle(WFMappingThrottle<NetUserId> throttle, ICommonSession player, string what)
    {
        if (throttle.TryTake(player.UserId, _timing.CurTime))
            return true;

        Log.Debug($"Dropped a {what} request from {player}: too many");
        return false;
    }

    /// <summary>Side of the square every tile must lie in, in tiles.</summary>
    public int MapSize => Math.Clamp(_cfg.GetCVar(WFMappingModeCVars.MapSize), 64, 512);

    /// <summary>Whether a map position lies on a tile inside the map area.</summary>
    private bool InBounds(Vector2 position)
    {
        var half = MapSize / 2;
        var x = (int) MathF.Floor(position.X);
        var y = (int) MathF.Floor(position.Y);
        return x >= -half && x < half && y >= -half && y < half;
    }

    /// <summary>Whether a box in map coordinates lies inside the map area.</summary>
    private bool InBounds(Box2 box)
    {
        const float epsilon = 0.01f;
        var half = MapSize / 2f + epsilon;
        return box.Left >= -half && box.Bottom >= -half && box.Right <= half && box.Top <= half;
    }

    /// <summary>Whether cells, local to <paramref name="grid"/> or map tiles without one, lie inside the map area.</summary>
    private bool CellsInBounds(EntityUid? grid, Box2i cells)
    {
        var box = new Box2(cells.Left, cells.Bottom, cells.Right, cells.Top);
        if (grid is { } gridUid)
            box = _transform.GetWorldMatrix(gridUid).TransformBox(box);

        return InBounds(box);
    }

    /// <summary>How many grids a session may have: a share per member, up to the session cap.</summary>
    public int GridCap(WFMappingSession session)
    {
        var perPlayer = _cfg.GetCVar(WFMappingModeCVars.GridsPerPlayer);
        return Math.Min(perPlayer * Math.Max(1, session.Members.Count),
            _cfg.GetCVar(WFMappingModeCVars.MaxGridsPerSession));
    }

    public int CountGrids(WFMappingSession session)
    {
        var count = 0;
        foreach (var map in SessionMaps(session.Id))
        {
            var children = Transform(map).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<MapGridComponent>(child))
                    count++;
            }
        }

        return count;
    }

    /// <summary>Entities across the session's maps, counted at most once a second.</summary>
    public int EntityCount(WFMappingSession session)
    {
        var now = _timing.CurTime;
        if (now - session.CountedAt < CountLifetime)
            return session.EntityCount;

        var count = 0;
        foreach (var map in SessionMaps(session.Id))
        {
            count += CountTree(map) - 1;
        }

        // A running live test is the session's copy of itself plus the range; it counts against the same cap.
        foreach (var map in TestMaps(session.Id))
        {
            count += CountTree(map) - 1;
        }

        session.EntityCount = count;
        session.CountedAt = now;
        return count;
    }

    /// <summary>An entity and everything under it.</summary>
    private int CountTree(EntityUid root)
    {
        var count = 0;
        var stack = new Stack<EntityUid>();
        stack.Push(root);
        while (stack.TryPop(out var uid))
        {
            count++;
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                stack.Push(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Entities whose world position falls in a cell of a grid, whatever they are parented to. Entities in
    /// <paramref name="moving"/>, and anything under them, aren't counted.
    /// </summary>
    private int EntitiesOnTile(EntityUid grid, Vector2i cell, HashSet<EntityUid>? moving = null)
    {
        var count = 0;
        var invMatrix = _transform.GetInvWorldMatrix(grid);
        foreach (var uid in _lookup.GetLocalEntitiesIntersecting(grid, cell, 0f))
        {
            if (uid == grid || moving != null && UnderAny(uid, grid, moving))
                continue;

            var local = Vector2.Transform(_transform.GetWorldPosition(uid), invMatrix);
            if (MappingToolsMath.CellOf(local) == cell)
                count++;
        }

        return count;
    }

    /// <summary>Whether an entity, or a parent of it below <paramref name="stop"/>, is in <paramref name="set"/>.</summary>
    private bool UnderAny(EntityUid uid, EntityUid stop, HashSet<EntityUid> set)
    {
        while (uid.IsValid() && uid != stop)
        {
            if (set.Contains(uid))
                return true;

            uid = Transform(uid).ParentUid;
        }

        return false;
    }

    /// <summary>Decals on a grid whose centre falls in the cell.</summary>
    private int DecalsOnTile(EntityUid grid, Vector2i cell)
    {
        var count = 0;
        var centre = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
        foreach (var (_, decal) in _decals.GetDecalsInRange(grid, centre, 1.5f))
        {
            if (MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f)) == cell)
                count++;
        }

        return count;
    }

    /// <summary>Whether mappers may not place a prototype: abstract, hidden, or carrying a denied component.</summary>
    private bool IsDenied(EntityPrototype prototype)
    {
        return prototype.Abstract || prototype.HideSpawnMenu || CarriesDenied(prototype, 0);
    }

    /// <summary>Whether a prototype carries a denied component.</summary>
    public bool CarriesDenied(EntityPrototype prototype)
    {
        return CarriesDenied(prototype, 0);
    }

    private bool CarriesDenied(EntityPrototype prototype, int depth)
    {
        foreach (var (name, entry) in prototype.Components)
        {
            if (_deniedComponents.Contains(name) || DeniesComponent(entry.Component, depth))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The components denied only with certain data: a radio that hears every channel, a warp point ghosts follow
    /// (Nar'Sie, the singularity, the nuke disk), and a despawn that leaves something denied behind.
    /// </summary>
    private bool DeniesComponent(IComponent component, int depth)
    {
        switch (component)
        {
            case ActiveRadioComponent radio:
                return radio.GlobalReceive || radio.ReceiveAllChannels;
            case WarpPointComponent warp:
                return warp.Follow;
            case SpawnOnDespawnComponent spawn:
                return depth >= MaxSpawnChain ||
                       !_prototypes.TryIndex<EntityPrototype>(spawn.Prototype, out var spawned) ||
                       CarriesDenied(spawned, depth + 1);
            default:
                return false;
        }
    }

    /// <summary>Whether a live entity carries a denied component.</summary>
    public bool HasDeniedComponent(EntityUid uid)
    {
        foreach (var type in _deniedTypes)
        {
            if (HasComp(uid, type))
                return true;
        }

        return TryComp(uid, out ActiveRadioComponent? radio) && DeniesComponent(radio, 0) ||
               TryComp(uid, out WarpPointComponent? warp) && DeniesComponent(warp, 0) ||
               TryComp(uid, out SpawnOnDespawnComponent? spawn) && DeniesComponent(spawn, 0);
    }

    public WFMappingLimitsState GetLimits(WFMappingSession session)
    {
        return new WFMappingLimitsState(CountGrids(session), GridCap(session), EntityCount(session),
            _cfg.GetCVar(WFMappingModeCVars.MaxEntities), MapSize);
    }

    /// <summary>
    /// The size limits of one mapping tools request: cells selected, entities touched and decals carried.
    /// </summary>
    private bool CheckWorkLimits(WFMappingCanEditEvent ev)
    {
        var maxCells = _cfg.GetCVar(WFMappingModeCVars.MaxSelectionCells);
        if (ev.SelectionCells > maxCells)
        {
            Cancel(ev, "wf-mapping-mode-limit-selection", ("max", maxCells));
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEditEntities);
        if (ev.TouchedEntities > maxEntities)
        {
            Cancel(ev, "wf-mapping-mode-limit-edit-entities", ("max", maxEntities));
            return false;
        }

        var maxDecals = _cfg.GetCVar(WFMappingModeCVars.MaxEditDecals);
        if (ev.Decals > maxDecals || ev.DecalCells?.Count > maxDecals)
        {
            Cancel(ev, "wf-mapping-mode-limit-edit-decals", ("max", maxDecals));
            return false;
        }

        return true;
    }

    /// <summary>
    /// The area, grid, entity, per-tile entity and per-tile decal limits of a mapping tools edit or paste.
    /// </summary>
    private void CheckEditLimits(WFMappingCanEditEvent ev, WFMappingSession session)
    {
        if (ev.Prototypes != null)
        {
            foreach (var id in ev.Prototypes)
            {
                if (!_prototypes.TryIndex<EntityPrototype>(id, out var prototype) || IsDenied(prototype))
                {
                    Cancel(ev, "wf-mapping-mode-limit-prototype");
                    return;
                }
            }
        }

        if (ev.CarriedPrototypes != null)
        {
            foreach (var id in ev.CarriedPrototypes)
            {
                if (_prototypes.TryIndex<EntityPrototype>(id, out var prototype) && CarriesDenied(prototype))
                {
                    Cancel(ev, "wf-mapping-mode-limit-prototype");
                    return;
                }
            }
        }

        if (ev.Cells is { } cells && !CellsInBounds(ev.Grid, cells))
        {
            Cancel(ev, "wf-mapping-mode-limit-area", ("size", MapSize));
            return;
        }

        if (ev.NewGrid)
        {
            var cap = GridCap(session);
            if (CountGrids(session) >= cap)
            {
                Cancel(ev, "wf-mapping-mode-limit-grids", ("max", cap));
                return;
            }
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        if (ev.Entities > 0 && EntityCount(session) + ev.Entities > maxEntities)
        {
            Cancel(ev, "wf-mapping-mode-limit-entities", ("max", maxEntities));
            return;
        }

        if (ev.DecalCells is { Count: > 0 } decalCells)
        {
            var maxDecals = _cfg.GetCVar(WFMappingModeCVars.MaxDecalsPerTile);
            foreach (var group in decalCells.GroupBy(c => c))
            {
                var existing = ev.Grid is { } decalGrid ? DecalsOnTile(decalGrid, group.Key) : 0;
                if (existing + group.Count() > maxDecals)
                {
                    Cancel(ev, "wf-mapping-mode-limit-decals-per-tile", ("max", maxDecals));
                    return;
                }
            }
        }

        if (ev.EntityCells is not { Count: > 0 } entityCells)
            return;

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxEntitiesPerTile);
        foreach (var group in entityCells.GroupBy(c => c))
        {
            var existing = ev.Grid is { } grid ? EntitiesOnTile(grid, group.Key, ev.Moving) : 0;
            if (existing + group.Count() > maxPerTile)
            {
                Cancel(ev, "wf-mapping-mode-limit-entities-per-tile", ("max", maxPerTile));
                return;
            }
        }
    }

    /// <summary>
    /// The engine placement gate for mappers: placing, laying tiles and erasing, only on their session's maps and
    /// within its limits. Admins are let through before this by the sandbox's own check.
    /// </summary>
    public bool AllowPlacement(ICommonSession player, MsgPlacement msg)
    {
        if (!Enabled || !TryGetPresent(player, out var session))
            return false;

        if (IsTesting(player))
        {
            Log.Debug($"Refused a placement from {player} while they test their map");
            if (msg.PlaceType is PlacementManagerMessage.RequestPlacement or PlacementManagerMessage.RequestEntRemove)
                Refuse(player, "wf-mapping-test-no-edit");

            return false;
        }

        switch (msg.PlaceType)
        {
            case PlacementManagerMessage.StartPlacement:
            case PlacementManagerMessage.CancelPlacement:
                return true;
            case PlacementManagerMessage.RequestPlacement:
                return TryThrottle(_editThrottle, player, "placement") && AllowPlace(player, session, msg);
            case PlacementManagerMessage.RequestEntRemove:
                return TryThrottle(_editThrottle, player, "erase") && AllowErase(player, session, msg);
            case PlacementManagerMessage.RequestRectRemove:
                // The engine's area erase takes everything in the box but players, ghosts and observers included;
                // mappers erase areas through Erase mode's tools request instead.
                Log.Debug($"Refused an engine area erase from {player}");
                Refuse(player, "wf-mapping-mode-erase-area-tools");
                return false;
            default:
                return false;
        }
    }

    private bool AllowPlace(ICommonSession player, WFMappingSession session, MsgPlacement msg)
    {
        var coords = GetCoordinates(msg.NetCoordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetMap(coords) is not { } map || !IsSessionMap(map, session))
        {
            Log.Info($"Dropped a placement from {player} at {msg.NetCoordinates}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!InBounds(mapCoords.Position))
        {
            Refuse(player, "wf-mapping-mode-limit-area", ("size", MapSize));
            return false;
        }

        EntityUid? gridUid = null;
        MapGridComponent? grid = null;
        if (TryComp(coords.EntityId, out MapGridComponent? own))
            (gridUid, grid) = (coords.EntityId, own);
        else if (_map.TryFindGridAt(mapCoords, out var found, out var foundGrid))
            (gridUid, grid) = (found, foundGrid);

        if (msg.IsTile)
        {
            if (!_tileDefs.TryGetDefinition(msg.TileType, out _))
            {
                Refuse(player, "wf-mapping-mode-bad-tile");
                return false;
            }

            if (gridUid == null && msg.TileType != 0)
            {
                RouteOffGridTile(player, map, mapCoords, msg);
                if (TryComp<MapGridComponent>(GetCoordinates(msg.NetCoordinates).EntityId, out _))
                    return true;

                var cap = GridCap(session);
                if (CountGrids(session) >= cap)
                {
                    Refuse(player, "wf-mapping-mode-limit-grids", ("max", cap));
                    return false;
                }
            }

            return true;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(msg.EntityTemplateName ?? string.Empty, out var prototype) ||
            IsDenied(prototype))
        {
            Refuse(player, "wf-mapping-mode-limit-prototype");
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        if (EntityCount(session) + 1 > maxEntities)
        {
            Refuse(player, "wf-mapping-mode-limit-entities", ("max", maxEntities));
            return false;
        }

        // Mappers build on grids; an entity in space would escape the per-tile count.
        if (gridUid is not { } g || grid == null)
        {
            Refuse(player, "wf-mapping-mode-limit-off-grid");
            return false;
        }

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxEntitiesPerTile);
        var cell = _map.TileIndicesFor(g, grid, mapCoords);
        if (EntitiesOnTile(g, cell) >= maxPerTile)
        {
            Refuse(player, "wf-mapping-mode-limit-entities-per-tile", ("max", maxPerTile));
            return false;
        }

        session.EntityCount++;
        WarnUnsaved(player, prototype.ID);
        return true;
    }

    private bool AllowErase(ICommonSession player, WFMappingSession session, MsgPlacement msg)
    {
        if (!TryGetEntity(msg.EntityUid, out var target) || TerminatingOrDeleted(target))
            return false;

        if (Transform(target.Value).MapUid is not { } map || !IsSessionMap(map, session))
        {
            Log.Info($"Dropped an erase of {ToPrettyString(target.Value)} from {player}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        if (HasComp<MapComponent>(target) || !_protection.CanTouch(target.Value))
        {
            Refuse(player, "wf-mapping-mode-erase-refused");
            return false;
        }

        return true;
    }
}
