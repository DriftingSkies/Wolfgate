using System.Linq;
using System.Numerics;
using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Hideouts.Blueprint;

/// <summary>How the left mouse button paints.</summary>
public enum WFBlueprintTool : byte
{
    /// <summary>Paints the tiles the cursor is dragged over.</summary>
    Brush,

    /// <summary>Fills the box dragged out.</summary>
    Box,

    /// <summary>Paints only the edge of the box dragged out: the walls of a room.</summary>
    Outline,

    /// <summary>Takes strokes back off the plan.</summary>
    Unplan,
}

/// <summary>
/// The Blueprint tab's plan: the outpost's floor drawn tile by tile with what stands on it, and the strokes painted over
/// it. Left paints, right drags the view, the wheel zooms.
/// </summary>
public sealed class WFBlueprintCanvas : Control
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private const float MinZoom = 4f;
    private const float MaxZoom = 48f;

    private static readonly Color Background = Color.FromHex("#14151A");
    private static readonly Color GridLine = Color.FromHex("#FFFFFF").WithAlpha(0.06f);
    private static readonly Color ClaimColor = Color.FromHex("#E8A33D");
    private static readonly Color WallColor = Color.FromHex("#23242B");
    private static readonly Color WindowColor = Color.FromHex("#6FB6E0");
    private static readonly Color DoorColor = Color.FromHex("#E08C32");
    private static readonly Color ThingColor = Color.FromHex("#9A6FC0");
    private static readonly Color RoofTint = Color.FromHex("#0B1830").WithAlpha(0.45f);
    private static readonly Color EraseColor = Color.FromHex("#E05A4F");

    private readonly Dictionary<string, Texture?> _textures = new();
    private readonly Dictionary<Vector2i, WFBlueprintCellKind> _cells = new();

    /// <summary>The plan: one stroke a tile on each layer.</summary>
    private readonly Dictionary<(Vector2i Index, int Layer), WFBlueprintStroke> _plan = new();

    /// <summary>What each gesture changed, newest last, for undo.</summary>
    private readonly List<List<((Vector2i Index, int Layer) Key, WFBlueprintStroke? Before)>> _history = new();

    private List<((Vector2i Index, int Layer) Key, WFBlueprintStroke? Before)>? _gesture;
    private EntityUid? _grid;
    private int _claimMargin;
    private bool _fitted;

    /// <summary>The tile at the middle of the view, and the size of a tile in virtual pixels.</summary>
    private Vector2 _centre;
    private float _zoom = 16f;

    private bool _painting;
    private bool _panning;
    private Vector2i? _boxFrom;
    private Vector2i? _hover;

    public WFBlueprintEntry? Brush;

    /// <summary>The palette, by brush id, for drawing the plan's strokes.</summary>
    public Dictionary<string, WFBlueprintEntry> Entries = new();

    /// <summary>Whether the edge of the grid's claim is drawn; a ship has none.</summary>
    public bool ShowClaim = true;
    public WFBlueprintTool Tool = WFBlueprintTool.Brush;
    public Direction Facing = Direction.South;
    public bool ShowRoofs;

    /// <summary>The plan changed.</summary>
    public event Action? OnPlanChanged;

    /// <summary>The cursor moved onto another tile, or off the plan.</summary>
    public event Action<Vector2i?>? OnHover;

    public IEnumerable<WFBlueprintStroke> Plan => _plan.Values;
    public int PlanCount => _plan.Count;
    public bool CanUndo => _history.Count > 0;

    public WFBlueprintCanvas()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetGrid(EntityUid? grid, int claimMargin)
    {
        _claimMargin = claimMargin;
        if (_grid == grid)
            return;

        _grid = grid;
        _fitted = false;
    }

    public void SetCells(List<WFBlueprintCell> cells)
    {
        _cells.Clear();
        foreach (var cell in cells)
        {
            // A wall, window or door shows over whatever shares its tile.
            if (!_cells.TryGetValue(cell.Index, out var kind) || kind == WFBlueprintCellKind.Thing)
                _cells[cell.Index] = cell.Kind;
        }
    }

    public void ClearPlan()
    {
        _plan.Clear();
        _history.Clear();
        OnPlanChanged?.Invoke();
    }

    public void Undo()
    {
        if (_history.Count == 0)
            return;

        var last = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        for (var i = last.Count - 1; i >= 0; i--)
        {
            var (key, before) = last[i];
            if (before is { } stroke)
                _plan[key] = stroke;
            else
                _plan.Remove(key);
        }

        OnPlanChanged?.Invoke();
    }

    /// <summary>Frames the outpost's claim.</summary>
    public void Recentre()
    {
        _fitted = false;
    }

    private bool TryGetClaim(out Box2 claim)
    {
        claim = default;
        if (!_entMan.TryGetComponent<MapGridComponent>(_grid, out var grid))
            return false;

        var floor = grid.LocalAABB;
        if (floor.Width <= 0f || floor.Height <= 0f)
            return false;

        claim = floor.Enlarged(_claimMargin * grid.TileSize);
        return true;
    }

    private void TryFit()
    {
        if (_fitted || Size.X <= 0f || !TryGetClaim(out var claim))
            return;

        _fitted = true;
        _centre = claim.Center;
        _zoom = Math.Clamp(MathF.Min(Size.X / claim.Width, Size.Y / claim.Height) * 0.95f, MinZoom, MaxZoom);
    }

    /// <summary>A point of the plan, in tiles, as a point of the control in pixels.</summary>
    private Vector2 ToPixel(Vector2 tile)
    {
        var scale = _zoom * UIScale;
        return new Vector2(PixelSize.X / 2f + (tile.X - _centre.X) * scale, PixelSize.Y / 2f - (tile.Y - _centre.Y) * scale);
    }

    private Vector2i ToTile(Vector2 pixel)
    {
        var scale = _zoom * UIScale;
        var x = _centre.X + (pixel.X - PixelSize.X / 2f) / scale;
        var y = _centre.Y - (pixel.Y - PixelSize.Y / 2f) / scale;
        return new Vector2i((int) MathF.Floor(x), (int) MathF.Floor(y));
    }

    private UIBox2 TileBox(Vector2i index)
    {
        var topLeft = ToPixel(new Vector2(index.X, index.Y + 1));
        var size = _zoom * UIScale;
        return UIBox2.FromDimensions(topLeft, new Vector2(size, size));
    }

    private Texture? TileTexture(string id)
    {
        if (_textures.TryGetValue(id, out var texture))
            return texture;

        texture = null;
        if (_tiles.TryGetDefinition(id, out var def) && def is ContentTileDefinition { Sprite: { } sprite }
            && _resources.TryGetResource<TextureResource>(sprite, out var resource))
            texture = resource.Texture;

        _textures[id] = texture;
        return texture;
    }

    private void DrawTile(DrawingHandleScreen handle, UIBox2 box, string id, Color tint)
    {
        if (TileTexture(id) is { } texture)
        {
            var side = MathF.Min(texture.Height, texture.Width);
            handle.DrawTextureRectRegion(texture, box, new UIBox2(0, 0, side, side), tint);
            return;
        }

        handle.DrawRect(box, Color.FromHex("#6A6E78") * tint);
    }

    private static Color CellColor(WFBlueprintCellKind kind) => kind switch
    {
        WFBlueprintCellKind.Wall => WallColor,
        WFBlueprintCellKind.Window => WindowColor,
        WFBlueprintCellKind.Door => DoorColor,
        _ => ThingColor,
    };

    protected override void Draw(DrawingHandleScreen handle)
    {
        TryFit();
        handle.DrawRect(PixelSizeBox, Background);
        if (!_entMan.TryGetComponent<MapGridComponent>(_grid, out var grid))
            return;

        var maps = _entMan.System<SharedMapSystem>();
        var min = ToTile(new Vector2(0, PixelSize.Y)) - Vector2i.One;
        var max = ToTile(new Vector2(PixelSize.X, 0)) + Vector2i.One;
        var size = _zoom * UIScale;

        _entMan.TryGetComponent<RoofComponent>(_grid, out var roof);
        var roofs = _entMan.System<SharedRoofSystem>();
        var enumerator = maps.GetLocalTilesEnumerator(_grid!.Value, grid, new Box2(min, max + Vector2i.One));
        while (enumerator.MoveNext(out var tile))
        {
            var box = TileBox(tile.GridIndices);
            DrawTile(handle, box, _tiles[tile.Tile.TypeId].ID, Color.White);
            if (ShowRoofs && roof != null && roofs.IsRooved((_grid.Value, grid, roof), tile.GridIndices))
                handle.DrawRect(box, RoofTint);
        }

        foreach (var (index, kind) in _cells)
        {
            if (index.X < min.X || index.Y < min.Y || index.X > max.X || index.Y > max.Y)
                continue;

            var box = TileBox(index);
            if (kind == WFBlueprintCellKind.Thing)
            {
                var inset = size * 0.3f;
                box = new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
            }

            handle.DrawRect(box, CellColor(kind));
        }

        // Floor strokes lie under entity strokes, roof strokes over both.
        foreach (var ((index, _), stroke) in _plan.OrderBy(pair => pair.Key.Layer))
        {
            if (index.X < min.X || index.Y < min.Y || index.X > max.X || index.Y > max.Y
                || !Entries.TryGetValue(stroke.Brush, out var brush))
                continue;

            DrawStroke(handle, TileBox(index), brush, stroke.Facing, 1f);
        }

        if (size >= 10f)
        {
            for (var x = min.X; x <= max.X + 1; x++)
            {
                var at = ToPixel(new Vector2(x, 0)).X;
                handle.DrawLine(new Vector2(at, 0), new Vector2(at, PixelSize.Y), GridLine);
            }

            for (var y = min.Y; y <= max.Y + 1; y++)
            {
                var at = ToPixel(new Vector2(0, y)).Y;
                handle.DrawLine(new Vector2(0, at), new Vector2(PixelSize.X, at), GridLine);
            }
        }

        if (ShowClaim && TryGetClaim(out var claim))
            handle.DrawRect(new UIBox2(ToPixel(new Vector2(claim.Left, claim.Top)), ToPixel(new Vector2(claim.Right, claim.Bottom))), ClaimColor, false);

        if (_hover is not { } hover)
            return;

        if (_boxFrom is { } from && _painting)
        {
            var low = Vector2i.ComponentMin(from, hover);
            var high = Vector2i.ComponentMax(from, hover);
            handle.DrawRect(new UIBox2(ToPixel(new Vector2(low.X, high.Y + 1)), ToPixel(new Vector2(high.X + 1, low.Y))), Color.White, false);
        }
        else if (Brush != null && Tool != WFBlueprintTool.Unplan)
        {
            DrawStroke(handle, TileBox(hover), Brush, Facing, 0.6f);
        }

        handle.DrawRect(TileBox(hover), Color.White, false);
    }

    private void DrawStroke(DrawingHandleScreen handle, UIBox2 box, WFBlueprintEntry brush, Direction facing, float alpha)
    {
        var size = box.Width;
        switch (brush.Kind)
        {
            case WFBlueprintKind.Floor:
                if (brush.Tile is { } tile)
                    DrawTile(handle, box, tile, Color.White.WithAlpha(alpha));
                handle.DrawRect(box, brush.Color.WithAlpha(0.9f * alpha), false);
                break;
            case WFBlueprintKind.Entity:
                var inset = size * (brush.Under ? 0.3f : 0.12f);
                var inner = new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                if (WFBlueprintIcons.Get(brush) is { } picture)
                {
                    handle.DrawTextureRect(picture, box, Color.White.WithAlpha(alpha));
                }
                else
                {
                    handle.DrawRect(inner, brush.Color.WithAlpha(alpha));
                    handle.DrawRect(inner, Color.White.WithAlpha(0.8f * alpha), false);
                }

                // A tick on the side it faces.
                var vec = facing.ToVec();
                var middle = box.Center;
                handle.DrawLine(middle, middle + new Vector2(vec.X, -vec.Y) * size * 0.38f, Color.White.WithAlpha(alpha));
                break;
            case WFBlueprintKind.Roof:
                handle.DrawRect(box, RoofTint.WithAlpha(0.6f * alpha));
                handle.DrawRect(box, brush.Color.WithAlpha(alpha), false);
                break;
            case WFBlueprintKind.Sky:
                handle.DrawRect(box, brush.Color.WithAlpha(0.25f * alpha));
                handle.DrawRect(box, brush.Color.WithAlpha(alpha), false);
                break;
            case WFBlueprintKind.Decal:
                if (WFBlueprintIcons.Get(brush) is { } decal)
                    handle.DrawTextureRect(decal, box, Color.White.WithAlpha(alpha));
                else
                    handle.DrawRect(box, brush.Color.WithAlpha(0.5f * alpha), false);
                break;
            case WFBlueprintKind.CleanDecals:
                var third = size * 0.3f;
                var small = new UIBox2(box.Left + third, box.Top + third, box.Right - third, box.Bottom - third);
                handle.DrawLine(small.TopLeft, small.BottomRight, EraseColor.WithAlpha(alpha));
                handle.DrawLine(small.TopRight, small.BottomLeft, EraseColor.WithAlpha(alpha));
                break;
            case WFBlueprintKind.Erase:
                handle.DrawLine(box.TopLeft, box.BottomRight, EraseColor.WithAlpha(alpha));
                handle.DrawLine(box.TopRight, box.BottomLeft, EraseColor.WithAlpha(alpha));
                handle.DrawRect(box, EraseColor.WithAlpha(alpha), false);
                break;
        }
    }

    private void Paint(Vector2i index)
    {
        if (_gesture == null)
            return;

        if (Tool == WFBlueprintTool.Unplan)
        {
            for (var layer = 0; layer < WFBlueprintEntry.Layers; layer++)
            {
                if (_plan.Remove((index, layer), out var old))
                    _gesture.Add(((index, layer), old));
            }

            return;
        }

        if (Brush == null)
            return;

        var key = (index, Brush.Layer);
        var stroke = new WFBlueprintStroke(index, Brush.Id, Facing);
        WFBlueprintStroke? before = _plan.TryGetValue(key, out var existing) ? existing : null;
        if (before == stroke || before == null && _plan.Count >= WFBlueprintLimits.Strokes)
            return;

        _plan[key] = stroke;
        _gesture.Add((key, before));
    }

    private void PaintBox(Vector2i from, Vector2i to)
    {
        var low = Vector2i.ComponentMin(from, to);
        var high = Vector2i.ComponentMax(from, to);
        for (var x = low.X; x <= high.X; x++)
        {
            for (var y = low.Y; y <= high.Y; y++)
            {
                if (Tool == WFBlueprintTool.Outline && x != low.X && x != high.X && y != low.Y && y != high.Y)
                    continue;

                Paint(new Vector2i(x, y));
            }
        }
    }

    private void EndGesture()
    {
        _painting = false;
        _boxFrom = null;
        if (_gesture is { Count: > 0 } gesture)
        {
            _history.Add(gesture);
            OnPlanChanged?.Invoke();
        }

        _gesture = null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            var index = ToTile(args.RelativePixelPosition);
            _painting = true;
            _gesture = new List<((Vector2i, int), WFBlueprintStroke?)>();
            if (Tool is WFBlueprintTool.Box or WFBlueprintTool.Outline)
                _boxFrom = index;
            else
                Paint(index);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = true;
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick && _painting)
        {
            if (_boxFrom is { } from)
                PaintBox(from, ToTile(args.RelativePixelPosition));
            EndGesture();
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = false;
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_panning)
        {
            _centre -= new Vector2(args.Relative.X, -args.Relative.Y) / _zoom;
            return;
        }

        var index = ToTile(args.RelativePixelPosition);
        if (_hover != index)
        {
            _hover = index;
            OnHover?.Invoke(index);
        }

        if (_painting && _boxFrom == null)
            Paint(index);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hover = null;
        _panning = false;
        OnHover?.Invoke(null);
        if (_painting)
            EndGesture();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        _zoom = Math.Clamp(_zoom * (args.Delta.Y > 0 ? 1.25f : 0.8f), MinZoom, MaxZoom);
        args.Handle();
    }
}
