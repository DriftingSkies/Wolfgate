using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Hideouts.Shipwright;

/// <summary>
/// A top-down plan of the hull on the bay: its floor, its hardpoints, the free edge tiles a new one may go on and the
/// ones already picked. Clicking a tile picks it.
/// </summary>
public sealed class WFShipwrightHullMap : Control
{
    [Dependency] private IEntityManager _entMan = default!;

    private static readonly Color FloorColor = Color.FromHex("#5A5F68");
    private static readonly Color MountColor = Color.FromHex("#E6C83C");
    private static readonly Color FreeColor = Color.FromHex("#3C8C5A");
    private static readonly Color PlacedColor = Color.FromHex("#5AC8E6");
    private static readonly Color RemovedColor = Color.FromHex("#E05A4F");
    private static readonly Color SelectedColor = Color.White;

    /// <summary>A tile on the plan was clicked.</summary>
    public event Action<Vector2i>? OnTileClicked;

    private EntityUid? _grid;
    private readonly List<Vector2i> _tiles = new();
    private HashSet<Vector2i> _mounts = new();
    private HashSet<Vector2i> _free = new();
    private HashSet<Vector2i> _placed = new();
    private HashSet<Vector2i> _removed = new();
    private Vector2i? _selected;
    private Box2i _bounds;

    public WFShipwrightHullMap()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(240, 240);
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>Reads the hull's floor off its grid and sets what the plan marks.</summary>
    public void Set(NetEntity grid, IEnumerable<Vector2i> mounts, IEnumerable<Vector2i> free)
    {
        _grid = _entMan.GetEntity(grid);
        _mounts = new HashSet<Vector2i>(mounts);
        _free = new HashSet<Vector2i>(free);
        _tiles.Clear();
        if (_grid is { } uid && _entMan.TryGetComponent<MapGridComponent>(uid, out var comp))
        {
            var tiles = _entMan.System<SharedMapSystem>().GetAllTilesEnumerator(uid, comp);
            while (tiles.MoveNext(out var tile))
                _tiles.Add(tile.Value.GridIndices);
        }

        if (_tiles.Count == 0)
        {
            _bounds = new Box2i(0, 0, 1, 1);
            return;
        }

        var min = _tiles[0];
        var max = _tiles[0];
        foreach (var tile in _tiles)
        {
            min = Vector2i.ComponentMin(min, tile);
            max = Vector2i.ComponentMax(max, tile);
        }

        _bounds = new Box2i(min, max + Vector2i.One);
    }

    /// <summary>Sets the picks drawn over the plan.</summary>
    public void SetPicks(IEnumerable<Vector2i> placed, IEnumerable<Vector2i> removed, Vector2i? selected)
    {
        _placed = new HashSet<Vector2i>(placed);
        _removed = new HashSet<Vector2i>(removed);
        _selected = selected;
    }

    private float Scale => Math.Max(1f, MathF.Min(PixelSize.X / (float) Math.Max(1, _bounds.Width), PixelSize.Y / (float) Math.Max(1, _bounds.Height)));

    private Vector2 Origin => (PixelSize - new Vector2(_bounds.Width, _bounds.Height) * Scale) / 2f;

    private UIBox2 TileBox(Vector2i tile)
    {
        var scale = Scale;
        var x = (tile.X - _bounds.Left) * scale;
        // North up: the plan's y grows downward.
        var y = (_bounds.Top - 1 - tile.Y) * scale;
        var origin = Origin;
        return UIBox2.FromDimensions(origin + new Vector2(x, y), new Vector2(scale, scale));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var inset = Math.Max(1f, Scale * 0.12f);
        foreach (var tile in _tiles)
        {
            var box = TileBox(tile);
            handle.DrawRect(box, FloorColor);
            Color? mark = _removed.Contains(tile) ? RemovedColor
                : _placed.Contains(tile) ? PlacedColor
                : _mounts.Contains(tile) ? MountColor
                : _free.Contains(tile) ? FreeColor.WithAlpha(0.45f)
                : null;
            if (mark is { } color)
                handle.DrawRect(new UIBox2(box.TopLeft + new Vector2(inset), box.BottomRight - new Vector2(inset)), color);
            if (_selected == tile)
                handle.DrawRect(box, SelectedColor, false);
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || _tiles.Count == 0)
            return;

        var scale = Scale;
        var local = args.RelativePixelPosition - Origin;
        var tile = new Vector2i(_bounds.Left + (int) MathF.Floor(local.X / scale), _bounds.Top - 1 - (int) MathF.Floor(local.Y / scale));
        if (!_tiles.Contains(tile))
            return;

        OnTileClicked?.Invoke(tile);
        args.Handle();
    }
}
