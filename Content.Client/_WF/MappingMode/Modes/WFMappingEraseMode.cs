using System.Linq;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Decals;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Robust.Client.ComponentTrees;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Erase: a click removes the entity, tile or decal under the cursor, as the sub-choice says; a drag removes them
/// over a rectangle, on a grid or off every grid. Everything removes entities, decals and tiles; its click takes the
/// entity under the cursor, or the tile and its decals when there is none. Where several entities overlap and none is
/// hit exactly, and on a right click, a list of everything under the cursor lets the mapper pick one.
/// </summary>
public sealed partial class WFMappingEraseMode(WFMappingBuildModeSystem modes) : WFMappingDragModeBase(modes)
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Most entities the list under the cursor shows.</summary>
    private const int MaxListed = 12;

    /// <summary>Side of the box round the cursor that sprites must touch to count as under it.</summary>
    private const float CursorBox = 0.02f;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Erase;

    protected override Color PreviewColor => Color.FromHex("#ef5350");

    /// <summary>A drag may start in space, so loose entities off every grid can be erased too.</summary>
    protected override bool ResolvesGrid => true;

    protected override Box2i? Preview(EntityUid? grid, Vector2i start, Vector2i end, List<Vector2i> cells,
        ref Color color)
    {
        var box = Corners(start, end);
        if ((long) box.Width * box.Height > WFMappingModeLimits.MaxDrawCells)
            color = Color.FromHex("#ff1744");

        return box;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (Pressing || !IsDown(args))
            return base.Secondary(args);

        var under = EntitiesUnder(Modes.ToMap(args.Coordinates));
        if (under.Count > 0)
            OpenList(under);

        return true;
    }

    protected override void Release(EntityUid? grid, Vector2i start, Vector2i end)
    {
        if (start == end)
        {
            Click(grid, start);
            return;
        }

        var box = Corners(start, end);
        if ((long) box.Width * box.Height > WFMappingModeLimits.MaxDrawCells)
        {
            Modes.Popup("wf-mapping-screen-too-many", ("max", WFMappingModeLimits.MaxDrawCells));
            return;
        }

        var target = Modes.EraseTarget;
        var all = target == WFMappingEraseTarget.Everything;

        // Everything goes top down: entities and decals before the floor under them.
        if (all || target == WFMappingEraseTarget.Entity)
        {
            var erase = new MappingToolsDrawEvent(grid is { } onGrid ? _entities.GetNetEntity(onGrid) : null,
                MappingDrawOp.EraseEntities, MappingToolsShapes.Rect(start, end, false), null, 0, null, Angle.Zero);
            if (grid == null)
                erase.Map = _entities.GetNetEntity(Modes.MapUid(PressPosition.MapId));

            Modes.SendDraw(erase);
        }

        if (grid is not { } gridUid)
            return;

        var net = _entities.GetNetEntity(gridUid);
        if (all || target == WFMappingEraseTarget.Decal)
        {
            Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.EraseDecals,
                MappingToolsShapes.Rect(start, end, false), null, 0, null, Angle.Zero));
        }

        if (all || target == WFMappingEraseTarget.Tile)
        {
            Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SetTiles,
                MappingToolsShapes.Rect(start, end, false), ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
        }
    }

    private void Click(EntityUid? grid, Vector2i cell)
    {
        var target = Modes.EraseTarget;
        if (target == WFMappingEraseTarget.Everything)
        {
            if (TryEraseClicked())
                return;

            if (grid is { } tileGrid && HasTile(tileGrid, cell))
            {
                var cells = new List<Vector2i> { cell };
                var net = _entities.GetNetEntity(tileGrid);
                Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.EraseDecals, cells, null, 0, null, Angle.Zero));
                Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SetTiles, new List<Vector2i> { cell },
                    ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
            }

            return;
        }

        switch (target)
        {
            case WFMappingEraseTarget.Entity:
                TryEraseClicked();
                break;
            case WFMappingEraseTarget.Tile:
                if (grid is { } gridUid && HasTile(gridUid, cell))
                {
                    Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(gridUid), MappingDrawOp.SetTiles,
                        new List<Vector2i> { cell }, ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
                }

                break;
            case WFMappingEraseTarget.Decal:
                if (PressCoordinates.IsValid(_entities))
                    Modes.Send(new RequestDecalRemovalEvent(_entities.GetNetCoordinates(PressCoordinates)));

                break;
        }
    }

    /// <summary>
    /// Asks to remove the entity the press landed on: the one clicked exactly, else the only sprite under the cursor;
    /// with several and none clicked exactly, lists them. False when there was none.
    /// </summary>
    private bool TryEraseClicked()
    {
        var clicked = PressClicked;
        if (clicked.IsValid() && _entities.EntityExists(clicked) && Erasable(clicked))
        {
            SendErase(clicked);
            return true;
        }

        var under = EntitiesUnder(PressPosition);
        switch (under.Count)
        {
            case 0:
                return false;
            case 1:
                SendErase(under[0]);
                return true;
            default:
                OpenList(under);
                return true;
        }
    }

    private void SendErase(EntityUid target)
    {
        _net.ClientSendMessage(new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = _entities.GetNetEntity(target),
        });
    }

    /// <summary>
    /// The entities whose sprite bounds hold a map position, topmost first, as the engine eraser finds them; not only
    /// those whose origin is in the clicked cell. Players, ghosts and observers are left out.
    /// </summary>
    private List<EntityUid> EntitiesUnder(MapCoordinates position)
    {
        var found = new List<(EntityUid Uid, int Depth)>();
        if (position.MapId == MapId.Nullspace)
            return new List<EntityUid>();

        var tree = _entities.System<SpriteTreeSystem>();
        var box = Box2.CenteredAround(position.Position, new System.Numerics.Vector2(CursorBox));
        foreach (var entry in tree.QueryAabb(position.MapId, box, approx: false))
        {
            if (entry.Component.Visible && Erasable(entry.Uid))
                found.Add((entry.Uid, entry.Component.DrawDepth));
        }

        found.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        return found.ConvertAll(f => f.Uid);
    }

    /// <summary>Whether the client would ask to erase an entity; the server checks again.</summary>
    private bool Erasable(EntityUid uid)
    {
        return uid != _player.LocalEntity &&
               !_entities.HasComponent<MapGridComponent>(uid) &&
               !_entities.HasComponent<MapComponent>(uid) &&
               !_entities.HasComponent<GhostComponent>(uid) &&
               !_entities.HasComponent<WFMappingObserverComponent>(uid);
    }

    /// <summary>A list at the mouse of the entities under the cursor, by name and prototype; a pick erases it.</summary>
    private void OpenList(List<EntityUid> entities)
    {
        var popup = new Popup();
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1b1b1e"),
                BorderColor = Color.FromHex("#4a4a55"),
                BorderThickness = new Thickness(1),
            },
        };
        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            MinWidth = 220,
            Margin = new Thickness(6),
        };
        list.AddChild(new Label { Text = Loc.GetString("wf-mapping-screen-erase-under-title") });

        foreach (var uid in entities.Take(MaxListed))
        {
            var meta = _entities.GetComponent<MetaDataComponent>(uid);
            var button = new Button
            {
                Text = Loc.GetString("wf-mapping-screen-erase-under-entry", ("name", meta.EntityName),
                    ("prototype", meta.EntityPrototype?.ID ?? "-")),
                ClipText = true,
            };
            var target = uid;
            button.OnPressed += _ =>
            {
                popup.Close();
                if (_entities.EntityExists(target))
                    SendErase(target);
            };
            list.AddChild(button);
        }

        if (entities.Count > MaxListed)
        {
            list.AddChild(new Label
            {
                Text = Loc.GetString("wf-mapping-screen-erase-under-more", ("count", entities.Count - MaxListed)),
            });
        }

        panel.AddChild(list);
        popup.AddChild(panel);
        popup.OpenAtMouse();
    }

    private bool HasTile(EntityUid grid, Vector2i cell)
    {
        return _entities.TryGetComponent(grid, out MapGridComponent? comp) && Modes.TileTypeAt((grid, comp), cell) != 0;
    }
}
