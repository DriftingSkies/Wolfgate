using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._WF.MappingTools;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingTools;

/// <summary>
/// Draws the Select tool's selection, box, drag preview and paste preview.
/// </summary>
public sealed class MappingSelectionOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly Color SelectionColor = Color.FromHex("#4aa3ff");
    private static readonly Color BoxColor = Color.FromHex("#ffffff");
    private static readonly Color MoveColor = Color.FromHex("#ffd24a");

    /// <summary>
    /// Areas larger than this only get an outline in the previews.
    /// </summary>
    private const int MaxPreviewCells = 4096;

    private readonly MappingSelectionTool _tool;
    private readonly SharedMapSystem _map;
    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public MappingSelectionOverlay(MappingSelectionTool tool)
    {
        IoCManager.InjectDependencies(this);
        _tool = tool;
        _map = _entities.System<SharedMapSystem>();
        _sprite = _entities.System<SpriteSystem>();
        _transform = _entities.System<TransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_tool.Active)
            return;

        _tool.UpdateDrag();
        var handle = args.WorldHandle;

        if (OnMap(_tool.Grid, args.MapId) is { } grid)
        {
            handle.SetTransform(_transform.GetWorldMatrix(grid));
            DrawSelection(handle);
        }

        if (OnMap(_tool.DragGrid, args.MapId) is { } dragGrid && _tool.TryGetMouseCell(dragGrid, out var mouse))
        {
            handle.SetTransform(_transform.GetWorldMatrix(dragGrid));
            switch (_tool.Drag)
            {
                case MappingSelectionTool.DragMode.Box:
                    DrawBox(handle, MappingToolsMath.FromCorners(_tool.DragStart, mouse), BoxColor);
                    break;
                case MappingSelectionTool.DragMode.Move:
                    DrawMove(handle, dragGrid, mouse - _tool.DragStart, _tool.DragTurns);
                    break;
            }
        }

        if (_tool is { Pasting: true, Clipboard: { } clip })
            DrawPaste(handle, clip, args.MapId);

        handle.SetTransform(Matrix3x2.Identity);
    }

    private EntityUid? OnMap(EntityUid? grid, MapId map)
    {
        return grid is { } uid && _entities.TryGetComponent(uid, out TransformComponent? xform) && xform.MapID == map
            ? uid
            : null;
    }

    private void DrawSelection(DrawingHandleWorld handle)
    {
        if (_tool.Area is { } area)
            DrawBox(handle, area, SelectionColor);

        foreach (var uid in _tool.PickedEntities())
        {
            var pos = _entities.GetComponent<TransformComponent>(uid).LocalPosition;
            DrawBox(handle, Box2.CenteredAround(pos, new Vector2(0.9f)), SelectionColor);
        }
    }

    private void DrawMove(DrawingHandleWorld handle, EntityUid grid, Vector2i offset, int turns)
    {
        if (_tool.GetExtent() is not { } extent || !_entities.TryGetComponent(grid, out MapGridComponent? gridComp))
            return;

        if (_tool.Area is { } area)
        {
            if (area.Area <= MaxPreviewCells)
            {
                for (var x = area.Left; x < area.Right; x++)
                {
                    for (var y = area.Bottom; y < area.Top; y++)
                    {
                        var cell = new Vector2i(x, y);
                        if (!_map.TryGetTileRef(grid, gridComp, cell, out var tile) || tile.Tile.IsEmpty)
                            continue;

                        var dest = MappingToolsMath.TransformCell(cell, extent, offset, turns);
                        handle.DrawRect(new Box2(dest, dest + Vector2i.One), MoveColor.WithAlpha(0.2f));
                    }
                }
            }

            DrawBox(handle, MappingToolsMath.TransformBox(area, extent, offset, turns), MoveColor);
        }

        foreach (var uid in _tool.SelectedEntities())
        {
            var xform = _entities.GetComponent<TransformComponent>(uid);
            var pos = MappingToolsMath.TransformPoint(xform.LocalPosition, extent, offset, turns);
            var proto = _entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
            DrawIcon(handle, proto, pos, xform.LocalRotation + MappingToolsMath.RotationDelta(turns));
        }
    }

    private void DrawPaste(DrawingHandleWorld handle, MappingToolsClipboardEvent clip, MapId map)
    {
        var mouse = _tool.MouseMapPosition;
        if (mouse.MapId != map)
            return;

        // Mirrors the server: paste onto the grid under the cursor, else the source grid, else a new grid with
        // the cursor at its origin.
        Vector2i cursor;
        if (TryGetPasteGrid(clip, mouse, out var grid, out var gridComp))
        {
            handle.SetTransform(_transform.GetWorldMatrix(grid));
            cursor = _map.TileIndicesFor(grid, gridComp, mouse);
        }
        else
        {
            handle.SetTransform(Matrix3x2.CreateTranslation(MathF.Floor(mouse.X), MathF.Floor(mouse.Y)));
            cursor = Vector2i.Zero;
        }

        var turns = _tool.PasteTurns;
        var extent = new Box2i(Vector2i.Zero, clip.Size);
        var offset = MappingToolsMath.PasteOrigin(cursor, clip.Size, turns) -
                     MappingToolsMath.DestinationOrigin(extent, Vector2i.Zero, turns);

        if (clip.Tiles.Count <= MaxPreviewCells)
        {
            foreach (var cell in clip.Tiles)
            {
                var dest = MappingToolsMath.TransformCell(cell, extent, offset, turns);
                handle.DrawRect(new Box2(dest, dest + Vector2i.One), MoveColor.WithAlpha(0.2f));
            }
        }

        foreach (var entity in clip.Entities)
        {
            var pos = MappingToolsMath.TransformPoint(entity.Position, extent, offset, turns);
            DrawIcon(handle, entity.Prototype, pos, entity.Rotation + MappingToolsMath.RotationDelta(turns));
        }

        DrawBox(handle, MappingToolsMath.TransformBox(extent, extent, offset, turns), MoveColor);
    }

    private bool TryGetPasteGrid(MappingToolsClipboardEvent clip, MapCoordinates mouse, out EntityUid grid,
        [NotNullWhen(true)] out MapGridComponent? gridComp)
    {
        if (_map.TryFindGridAt(mouse, out grid, out gridComp))
            return true;

        if (!_entities.TryGetEntity(clip.SourceGrid, out var source) ||
            !_entities.TryGetComponent(source, out gridComp) ||
            _entities.GetComponent<TransformComponent>(source.Value).MapID != mouse.MapId)
        {
            return false;
        }

        grid = source.Value;
        return true;
    }

    private void DrawIcon(DrawingHandleWorld handle, string? prototype, Vector2 pos, Angle rotation)
    {
        if (prototype == null || !_prototypes.HasIndex<EntityPrototype>(prototype))
        {
            handle.DrawRect(Box2.CenteredAround(pos, new Vector2(0.3f)), MoveColor.WithAlpha(0.6f));
            return;
        }

        var texture = _sprite.GetPrototypeIcon(prototype).Default;
        var box = Box2.CenteredAround(pos, Vector2.One);
        handle.DrawTextureRect(texture, new Box2Rotated(box, rotation, pos), Color.White.WithAlpha(0.6f));
    }

    private static void DrawBox(DrawingHandleWorld handle, Box2i box, Color color)
    {
        DrawBox(handle, new Box2(box.BottomLeft, box.TopRight), color);
    }

    private static void DrawBox(DrawingHandleWorld handle, Box2 box, Color color)
    {
        handle.DrawRect(box, color.WithAlpha(0.12f));
        handle.DrawRect(box, color, filled: false);
    }
}
