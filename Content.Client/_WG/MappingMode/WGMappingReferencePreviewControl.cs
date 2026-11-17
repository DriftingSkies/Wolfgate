using System.Numerics;
using Content.Shared._WG.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WG.MappingMode;

/// <summary>Draws a reference file's floor plan, scaled to fit: its floor, then walls, then windows and doors.</summary>
public sealed class WGMappingReferencePreviewControl : Control
{
    private const float Margin = 8f;
    private const float MaxTilePixels = 16f;

    private static readonly Color BackgroundColor = Color.FromHex("#101014");
    private static readonly Color FloorColor = Color.FromHex("#3d4a5c");
    private static readonly Color WallColor = Color.FromHex("#c9d4e3");
    private static readonly Color OpeningColor = Color.FromHex("#4ad9ff");

    private WGMappingReferencePreview? _preview;
    private Vector2i _builtFor;
    private Vector2[] _floor = Array.Empty<Vector2>();
    private Vector2[] _walls = Array.Empty<Vector2>();
    private Vector2[] _openings = Array.Empty<Vector2>();

    /// <summary>The floor plan shown, if any.</summary>
    public WGMappingReferencePreview? Preview => _preview;

    public WGMappingReferencePreviewControl()
    {
        RectClipContent = true;
    }

    /// <summary>Shows a floor plan, or nothing.</summary>
    public void SetPreview(WGMappingReferencePreview? preview)
    {
        _preview = preview;
        _builtFor = default;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, BackgroundColor);
        if (_preview == null)
            return;

        if (_builtFor != PixelSize)
            Build(_preview);

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _floor, FloorColor);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _walls, WallColor);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _openings, OpeningColor);
    }

    private void Build(WGMappingReferencePreview preview)
    {
        _builtFor = PixelSize;

        var floor = new List<Vector2>();
        var walls = new List<Vector2>();
        var openings = new List<Vector2>();
        foreach (var grid in preview.Grids)
        {
            var matrix = Matrix3Helpers.CreateTransform(grid.Offset, grid.Rotation);
            AddRects(grid.Floor, matrix, floor);
            AddRects(grid.Walls, matrix, walls);
            AddRects(grid.Openings, matrix, openings);
        }

        if (floor.Count == 0 && walls.Count == 0 && openings.Count == 0)
        {
            _floor = _walls = _openings = Array.Empty<Vector2>();
            return;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var list in new[] { floor, walls, openings })
        {
            foreach (var point in list)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        var size = max - min;
        var pixels = new Vector2(PixelSize.X, PixelSize.Y);
        var room = Vector2.Max(pixels - new Vector2(Margin * 2f * UIScale), Vector2.One);
        var scale = MathF.Min(MathF.Min(room.X / size.X, room.Y / size.Y), MaxTilePixels * UIScale);
        var centre = (min + max) / 2f;

        _floor = ToScreen(floor, centre, scale, pixels);
        _walls = ToScreen(walls, centre, scale, pixels);
        _openings = ToScreen(openings, centre, scale, pixels);
    }

    private static void AddRects(List<Box2i> rects, Matrix3x2 matrix, List<Vector2> vertices)
    {
        foreach (var rect in rects)
        {
            var a = Vector2.Transform(new Vector2(rect.Left, rect.Bottom), matrix);
            var b = Vector2.Transform(new Vector2(rect.Right, rect.Bottom), matrix);
            var c = Vector2.Transform(new Vector2(rect.Right, rect.Top), matrix);
            var d = Vector2.Transform(new Vector2(rect.Left, rect.Top), matrix);
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(a);
            vertices.Add(c);
            vertices.Add(d);
        }
    }

    /// <summary>World north is up on screen, so Y flips.</summary>
    private static Vector2[] ToScreen(List<Vector2> world, Vector2 centre, float scale, Vector2 pixels)
    {
        var result = new Vector2[world.Count];
        for (var i = 0; i < world.Count; i++)
        {
            var offset = world[i] - centre;
            result[i] = new Vector2(pixels.X / 2f + offset.X * scale, pixels.Y / 2f - offset.Y * scale);
        }

        return result;
    }
}
