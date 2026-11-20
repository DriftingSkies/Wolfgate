using System.Numerics;
using Content.Shared._WG.Paintings;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WG.Paintings;

/// <summary>
/// A canvas's pixels, painted with the left button and erased with the right. Strokes show at once and are
/// collected for the window to send.
/// </summary>
public sealed partial class WGCanvasControl : Control
{
    public enum Tool : byte { Pencil, Fill, Picker }

    public Tool Mode { get; set; } = Tool.Pencil;

    /// <summary>The colour strokes show in until the server confirms them, or null when nothing can paint.</summary>
    public Color? Brush { get; set; }

    /// <summary>Finished canvases only show their painting.</summary>
    public bool Locked { get; set; }

    /// <summary>Draws a line between pixels.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Width of the wooden frame drawn around the painting, or 0 for none.</summary>
    public float FrameWidth { get; set; }

    /// <summary>Side of the longest edge the canvas asks for at least.</summary>
    public float TargetSize { get; set; } = 384;

    private static readonly Color Shadow = new(0f, 0f, 0f, 0.45f);
    private static readonly Color Outline = Color.FromHex("#0b0b0e");
    private static readonly Color GridLine = new(0f, 0f, 0f, 0.14f);
    private static readonly Color Wood = Color.FromHex("#6b4424");
    private static readonly Color WoodLight = Color.FromHex("#93633a");
    private static readonly Color WoodDark = Color.FromHex("#3d2412");

    /// <summary>Space around the painting for its shadow.</summary>
    private const float ShadowOffset = 6f;

    /// <summary>Raised when the picker reads a colour off the canvas.</summary>
    public event Action<Color>? ColourPicked;

    /// <summary>Pixels painted since the window last sent them.</summary>
    public readonly HashSet<ushort> Painted = new();

    /// <summary>Pixels erased since the window last sent them.</summary>
    public readonly HashSet<ushort> Erased = new();

    /// <summary>How long a sent stroke shows over server states that lack it.</summary>
    private static readonly TimeSpan InFlightTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Sent pixels the server hasn't confirmed, with the colour sent and when.</summary>
    private readonly Dictionary<ushort, (Color Colour, uint Stored, TimeSpan Sent)> _inFlight = new();

    private readonly byte[] _scratch = new byte[4];

    [Dependency] private IGameTiming _timing = default!;

    private int _width;
    private int _height;
    private byte[] _pixels = Array.Empty<byte>();
    private bool? _erasing;
    private (int X, int Y)? _last;
    private (int X, int Y)? _hover;

    public WGCanvasControl()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>Notes the strokes about to be sent, so they keep showing until a server state has them.</summary>
    public void MarkSent()
    {
        var now = _timing.RealTime;
        foreach (var index in Erased)
            _inFlight[index] = (WGPaintingRules.Blank, Stored(WGPaintingRules.Blank), now);
        if (Brush is { } brush)
        {
            foreach (var index in Painted)
                _inFlight[index] = (brush, Stored(brush), now);
        }
    }

    /// <summary>A colour as the four bytes a canvas stores it in.</summary>
    private uint Stored(Color colour)
    {
        WGPaintingRules.Write(_scratch, 0, colour);
        return BitConverter.ToUInt32(_scratch, 0);
    }

    /// <summary>Shows the server's pixels, with strokes it doesn't have yet laid over them.</summary>
    public void SetPixels(int width, int height, byte[] pixels)
    {
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            var cell = MathF.Max(10, MathF.Floor(TargetSize / Math.Max(width, height)));
            var margin = 2 * (FrameWidth + ShadowOffset);
            MinSize = new Vector2(width * cell + margin, height * cell + margin);
            Painted.Clear();
            Erased.Clear();
            _inFlight.Clear();
        }

        _pixels = (byte[]) pixels.Clone();
        var now = _timing.RealTime;
        foreach (var (index, sent) in _inFlight)
        {
            if (BitConverter.ToUInt32(_pixels, index * 4) == sent.Stored || now - sent.Sent > InFlightTimeout)
                _inFlight.Remove(index);
            else
                WGPaintingRules.Write(_pixels, index * 4, sent.Colour);
        }

        foreach (var index in Erased)
            WGPaintingRules.Write(_pixels, index * 4, WGPaintingRules.Blank);
        if (Brush is { } brush)
        {
            foreach (var index in Painted)
                WGPaintingRules.Write(_pixels, index * 4, brush);
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        var erase = args.Function == EngineKeyFunctions.UIRightClick;
        if (args.Function != EngineKeyFunctions.UIClick && !erase)
            return;

        args.Handle();
        if (Locked || Cell(args.RelativePosition) is not { } cell)
            return;

        switch (Mode)
        {
            case Tool.Picker:
                ColourPicked?.Invoke(Get(cell.X, cell.Y));
                break;
            case Tool.Fill:
                Fill(cell.X, cell.Y, erase);
                break;
            default:
                _erasing = erase;
                _last = cell;
                Set(cell.X, cell.Y, erase);
                break;
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.UIRightClick)
            StopStroke();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _hover = Locked ? null : Cell(args.RelativePosition);
        if (_erasing is not { } erase || _hover is not { } cell)
            return;

        if (_last is { } last)
            Line(last, cell, erase);
        else
            Set(cell.X, cell.Y, erase);
        _last = cell;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hover = null;
        StopStroke();
    }

    private void StopStroke()
    {
        _erasing = null;
        _last = null;
    }

    /// <summary>The cell under a point, keeping cells square and the canvas centred.</summary>
    private (int X, int Y)? Cell(Vector2 position)
    {
        if (_width == 0)
            return null;

        var (origin, cell) = Layout(Size, 1f);
        var x = (int) MathF.Floor((position.X - origin.X) / cell);
        var y = (int) MathF.Floor((position.Y - origin.Y) / cell);
        return x >= 0 && x < _width && y >= 0 && y < _height ? (x, y) : null;
    }

    /// <summary>Where the pixels sit in an area, leaving room for the frame and shadow at the given scale.</summary>
    private (Vector2 Origin, float Cell) Layout(Vector2 area, float scale)
    {
        var margin = 2 * (FrameWidth + ShadowOffset) * scale;
        var cell = MathF.Max(0f, MathF.Min((area.X - margin) / _width, (area.Y - margin) / _height));
        return ((area - new Vector2(_width, _height) * cell) / 2, cell);
    }

    /// <summary>Bresenham's line, so fast strokes leave no gaps.</summary>
    private void Line((int X, int Y) from, (int X, int Y) to, bool erase)
    {
        var (x, y) = from;
        var dx = Math.Abs(to.X - x);
        var dy = -Math.Abs(to.Y - y);
        var sx = x < to.X ? 1 : -1;
        var sy = y < to.Y ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Set(x, y, erase);
            if (x == to.X && y == to.Y)
                return;

            var doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    private Color Get(int x, int y)
    {
        return WGPaintingRules.Read(_pixels, (y * _width + x) * 4);
    }

    private void Set(int x, int y, bool erase)
    {
        var index = (ushort) (y * _width + x);
        if (erase)
        {
            Painted.Remove(index);
            Erased.Add(index);
            WGPaintingRules.Write(_pixels, index * 4, WGPaintingRules.Blank);
        }
        else if (Brush is { } brush)
        {
            Erased.Remove(index);
            Painted.Add(index);
            WGPaintingRules.Write(_pixels, index * 4, brush);
        }
    }

    /// <summary>Paints over the touching area of the clicked pixel's colour.</summary>
    private void Fill(int x, int y, bool erase)
    {
        if ((erase ? WGPaintingRules.Blank : Brush) is not { } paint)
            return;

        // Compared as stored, so a brush between byte values can't keep matching the area.
        var stored = new byte[4];
        WGPaintingRules.Write(stored, 0, paint);
        var target = Get(x, y);
        if (target == WGPaintingRules.Read(stored, 0))
            return;

        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cx >= _width || cy < 0 || cy >= _height || Get(cx, cy) != target)
                continue;

            Set(cx, cy, erase);
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_width == 0)
            return;

        var scale = UIScale;
        var (origin, cell) = Layout(PixelSize, scale);
        var art = UIBox2.FromDimensions(origin, new Vector2(_width, _height) * cell);
        var shadow = new Vector2(ShadowOffset * scale);
        if (FrameWidth > 0)
            DrawFrame(handle, art, FrameWidth * scale, shadow, scale);
        else
            handle.DrawRect(art.Translated(shadow), Shadow);

        var size = new Vector2(cell, cell);
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var box = UIBox2.FromDimensions(origin + new Vector2(x, y) * cell, size);
                handle.DrawRect(box, Get(x, y));
            }
        }

        if (ShowGrid && cell >= 6)
        {
            for (var x = 1; x < _width; x++)
                handle.DrawLine(origin + new Vector2(x * cell, 0), origin + new Vector2(x * cell, art.Height), GridLine);
            for (var y = 1; y < _height; y++)
                handle.DrawLine(origin + new Vector2(0, y * cell), origin + new Vector2(art.Width, y * cell), GridLine);
        }

        handle.DrawRect(art, Outline, false);
        if (_hover is not { } hover || Locked)
            return;

        // The cell under the cursor, with the paint it would get.
        var hovered = UIBox2.FromDimensions(origin + new Vector2(hover.X, hover.Y) * cell, size);
        if (Mode == Tool.Pencil && Brush is { } brush)
            handle.DrawRect(hovered, brush.WithAlpha(0.6f));
        handle.DrawRect(hovered, Color.Black, false);
        handle.DrawRect(new UIBox2(hovered.TopLeft + Vector2.One, hovered.BottomRight - Vector2.One), Color.White, false);
    }

    /// <summary>A bevelled wooden frame around the art, with its shadow.</summary>
    private static void DrawFrame(DrawingHandleScreen handle, UIBox2 art, float width, Vector2 shadow, float scale)
    {
        var outer = new UIBox2(art.TopLeft - new Vector2(width), art.BottomRight + new Vector2(width));
        var bevel = MathF.Max(1f, MathF.Round(2 * scale));
        handle.DrawRect(outer.Translated(shadow), Shadow);
        handle.DrawRect(outer, Wood);
        handle.DrawRect(new UIBox2(outer.Left, outer.Top, outer.Right, outer.Top + bevel), WoodLight);
        handle.DrawRect(new UIBox2(outer.Left, outer.Top, outer.Left + bevel, outer.Bottom), WoodLight);
        handle.DrawRect(new UIBox2(outer.Left, outer.Bottom - bevel, outer.Right, outer.Bottom), WoodDark);
        handle.DrawRect(new UIBox2(outer.Right - bevel, outer.Top, outer.Right, outer.Bottom), WoodDark);
        var inner = new UIBox2(art.TopLeft - new Vector2(bevel), art.BottomRight + new Vector2(bevel));
        handle.DrawRect(inner, WoodDark);
        handle.DrawRect(outer, Outline, false);
    }
}
