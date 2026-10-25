using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Shared._WF.MappingMode;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.MappingMode;

public sealed partial class WFMappingClientSystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>Whether the next tiles placed off every grid start a new grid instead of joining the nearest.</summary>
    public bool NewGridNext { get; private set; }

    public event Action? NewGridChanged;

    private void InitializeWorldGrid()
    {
        SubscribeNetworkEvent<WFMappingNewGridStateEvent>(OnNewGridState);
    }

    private void OnNewGridState(WFMappingNewGridStateEvent ev)
    {
        if (NewGridNext == ev.Enabled)
            return;

        NewGridNext = ev.Enabled;
        NewGridChanged?.Invoke();
    }

    /// <summary>Asks for the next tiles placed off every grid to start a new grid, or not.</summary>
    public void SetNewGrid(bool enabled)
    {
        NewGridNext = enabled;
        NewGridChanged?.Invoke();
        RaiseNetworkEvent(new WFMappingNewGridRequest(enabled));
    }

    /// <summary>
    /// The grid a tile placed at a point off every grid joins, as the server picks it: the nearest grid on the map,
    /// or null for a new grid.
    /// </summary>
    public EntityUid? TileTarget(MapId mapId, Vector2 point)
    {
        if (NewGridNext)
            return null;

        EntityUid? nearest = null;
        var best = float.MaxValue;
        // Session maps are paused, so their grids are too.
        var query = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != mapId || xform.ParentUid != xform.MapUid)
                continue;

            var bounds = _transformSystem.GetWorldMatrix(uid).TransformBox(grid.LocalAABB);
            var distance = Vector2.DistanceSquared(point, Vector2.Clamp(point, bounds.BottomLeft, bounds.TopRight));
            if (distance >= best)
                continue;

            best = distance;
            nearest = uid;
        }

        return nearest;
    }

    /// <summary>Swaps the engine's tile placement for one that keeps tiles on whole world tiles.</summary>
    private void UpdateTilePlacement()
    {
        if (_placement.CurrentPermission is { IsTile: true } &&
            _placement.CurrentMode is AlignTileAny &&
            _placement is PlacementManager manager)
        {
            _placement.CurrentMode = new WFAlignWorldTile(manager);
        }
    }
}
