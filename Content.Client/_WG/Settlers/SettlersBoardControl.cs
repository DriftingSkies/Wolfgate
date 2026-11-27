using System.Numerics;
using Content.Shared._WG.Settlers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WG.Settlers;

/// <summary>What a click on the board does.</summary>
public enum SettlersPick : byte
{
    None,
    Lane,
    Outpost,
    Colony,
    Pirates,
}

/// <summary>
/// The board seen from above: sectors, numbers, docks, every player's pieces, the pirates and the last roll.
/// Lights the legal targets of the current pick and reports a click on one.
/// </summary>
public sealed class SettlersBoardControl : Control
{
    private static readonly float Sqrt3 = MathF.Sqrt(3f);
    private static readonly Color DockLine = Color.FromHex("#55607E");

    /// <summary>The board's width and height in hex radii, docks included.</summary>
    private static readonly float BoardWidth = 5f * Sqrt3 + 2.2f;
    private const float BoardHeight = 8f + 2.2f;

    private readonly Vector2[] _hex = new Vector2[6];
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _shape = new Vector2[7];
    private readonly Vector2[] _stars = new Vector2[90];
    private readonly float[] _starAlpha = new float[90];
    private readonly Vector2[] _debris = new Vector2[9];
    private const int NebulaLayers = 16;

    /// <summary>Faint nebulae behind the board: centre and radius as fractions of the control, and colour.</summary>
    private static readonly (Vector2 Center, float Radius, Color Color)[] Nebulae =
    {
        (new Vector2(0.18f, 0.22f), 0.34f, Color.FromHex("#6B3FA0")),
        (new Vector2(0.86f, 0.78f), 0.38f, Color.FromHex("#1F6F8B")),
        (new Vector2(0.78f, 0.12f), 0.22f, Color.FromHex("#8B2F5F")),
    };

    /// <summary>An outpost's outline in building radii: a hut with a pointed roof.</summary>
    private static readonly Vector2[] OutpostShape =
    {
        new(0f, -1.1f), new(0.9f, -0.3f), new(0.9f, 0.8f), new(-0.9f, 0.8f), new(-0.9f, -0.3f),
    };

    /// <summary>A colony's outline in building radii: a tower beside a lower hall, fanned from the first corner.</summary>
    private static readonly Vector2[] ColonyShape =
    {
        new(-1f, 0.8f), new(-1f, -0.6f), new(-0.5f, -1.1f), new(0f, -0.6f), new(0f, -0.1f), new(1f, -0.1f), new(1f, 0.8f),
    };

    private static readonly Vector2[] OutlineOffsets =
    {
        new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f), new(-0.7f, -0.7f), new(0.7f, -0.7f), new(-0.7f, 0.7f), new(0.7f, 0.7f),
    };
    private readonly Texture[] _icons = new Texture[SettlersRules.ResourceCount];
    private readonly Texture _pirate;
    private readonly Font _numberFont;
    private readonly Font _smallFont;

    private Vector2 _mouse = new(-1000f);
    private int _hover = -1;

    public SettlersState? State;
    public int? Seat;
    public SettlersPick Pick;

    public event Action<SettlersPick, int>? OnPicked;

    public SettlersBoardControl()
    {
        MinSize = new Vector2(540f, 520f);
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;

        for (var resource = 0; resource < _icons.Length; resource++)
        {
            _icons[resource] = SettlersArt.ResourceTexture(resource);
        }

        _pirate = SettlersArt.PirateTexture();
        _numberFont = SettlersArt.Font(13);
        _smallFont = SettlersArt.Font(9);

        // The same sky every time the window opens.
        var random = new System.Random(7);
        for (var i = 0; i < _stars.Length; i++)
        {
            _stars[i] = new Vector2(random.NextSingle(), random.NextSingle());
            _starAlpha[i] = 0.15f + random.NextSingle() * 0.5f;
        }

        for (var i = 0; i < _debris.Length; i++)
        {
            var angle = random.NextSingle() * MathF.Tau;
            _debris[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (0.2f + random.NextSingle() * 0.5f);
        }
    }

    private float Radius => MathF.Min(PixelWidth / BoardWidth, PixelHeight / BoardHeight);

    private Vector2 ToPixel(Vector2 board) => new Vector2(PixelWidth, PixelHeight) * 0.5f + board * Radius;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mouse = args.RelativePixelPosition;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _mouse = new Vector2(-1000f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _mouse = args.RelativePixelPosition;
        var target = FindHover();
        if (target < 0)
            return;

        OnPicked?.Invoke(Pick, target);
        args.Handle();
    }

    /// <summary>Whether the local player may pick this corner, side or tile now.</summary>
    private bool Legal(int target)
    {
        if (State is not { } state || Seat is not { } seat)
            return false;

        return Pick switch
        {
            SettlersPick.Outpost => state.CanPlaceOutpost(seat, target),
            SettlersPick.Colony => state.CanPlaceColony(seat, target),
            SettlersPick.Lane => state.CanPlaceLane(seat, target),
            SettlersPick.Pirates => target != state.Pirates,
            _ => false,
        };
    }

    private int TargetCount => Pick switch
    {
        SettlersPick.Outpost or SettlersPick.Colony => SettlersBoard.VertexCount,
        SettlersPick.Lane => SettlersBoard.EdgeCount,
        SettlersPick.Pirates => SettlersBoard.TileCount,
        _ => 0,
    };

    private Vector2 TargetPos(int target) => Pick switch
    {
        SettlersPick.Outpost or SettlersPick.Colony => SettlersBoard.VertexPos[target],
        SettlersPick.Lane => SettlersBoard.EdgeMid(target),
        _ => SettlersBoard.TileCenter[target],
    };

    /// <summary>The legal target nearest the mouse, if it is close enough.</summary>
    private int FindHover()
    {
        var reach = Pick == SettlersPick.Pirates ? 0.85f : 0.4f;
        var best = -1;
        var bestDistance = reach * Radius;
        for (var target = 0; target < TargetCount; target++)
        {
            var distance = Vector2.Distance(ToPixel(TargetPos(target)), _mouse);
            if (distance >= bestDistance || !Legal(target))
                continue;

            best = target;
            bestDistance = distance;
        }

        return best;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = new Vector2(PixelWidth, PixelHeight);
        handle.DrawRect(new UIBox2(Vector2.Zero, size), SettlersArt.Space);
        var span = MathF.Max(PixelWidth, PixelHeight);
        foreach (var (center, extent, tint) in Nebulae)
        {
            for (var layer = 0; layer < NebulaLayers; layer++)
            {
                Circle(handle, center * size, extent * span * (1f - layer / (float) NebulaLayers), tint.WithAlpha(0.012f));
            }
        }

        for (var i = 0; i < _stars.Length; i++)
        {
            Circle(handle, _stars[i] * new Vector2(PixelWidth, PixelHeight), 1f, Color.White.WithAlpha(_starAlpha[i]));
        }

        if (State is not { } state)
            return;

        _hover = FindHover();
        var radius = Radius;

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            DrawHex(handle, SettlersBoard.TileCenter[tile], 1.18f, SettlersArt.Coast);
        }

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            DrawHex(handle, SettlersBoard.TileCenter[tile], 1.08f, SettlersArt.Rim);
        }

        DrawDocks(handle, state, radius);

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            DrawTile(handle, state, tile, radius);
        }

        for (var edge = 0; edge < SettlersBoard.EdgeCount; edge++)
        {
            if (state.EdgeOwner[edge] >= 0)
                DrawLane(handle, edge, SettlersArt.SeatColors[state.EdgeOwner[edge]], radius);
        }

        for (var vertex = 0; vertex < SettlersBoard.VertexCount; vertex++)
        {
            if (state.VertexOwner[vertex] >= 0)
                DrawBuilding(handle, ToPixel(SettlersBoard.VertexPos[vertex]), SettlersArt.SeatColors[state.VertexOwner[vertex]], state.Colony[vertex], radius);
        }

        DrawPirates(handle, ToPixel(SettlersBoard.TileCenter[state.Pirates] - new Vector2(0f, 0.28f)), radius);
        DrawTargets(handle, radius);
        DrawDice(handle, state, radius);
    }

    private void DrawHex(DrawingHandleScreen handle, Vector2 center, float scale, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            var angle = MathF.PI / 3f * i - MathF.PI / 2f;
            _hex[i] = ToPixel(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * scale);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _hex, Color.ToSrgb(color));
    }

    private void DrawTile(DrawingHandleScreen handle, SettlersState state, int tile, float radius)
    {
        var center = SettlersBoard.TileCenter[tile];
        var sector = state.Sectors[tile];
        var color = SettlersArt.SectorColors[(int) sector];
        DrawHex(handle, center, 0.97f, Color.InterpolateBetween(color, Color.Black, 0.45f));
        DrawHex(handle, center, 0.9f, Color.InterpolateBetween(color, Color.White, 0.14f));
        DrawHex(handle, center, 0.85f, color);

        if (sector == SettlersSector.DeadZone)
        {
            var rock = Color.InterpolateBetween(color, Color.White, 0.25f);
            for (var i = 0; i < _debris.Length; i++)
            {
                Circle(handle, ToPixel(center + _debris[i]), (0.025f + 0.012f * (i % 3)) * radius, rock);
            }
        }

        if (SettlersRules.Yield(sector) is { } yield)
        {
            var size = 0.62f * radius;
            var at = ToPixel(center - new Vector2(0f, 0.28f));
            handle.DrawTextureRect(_icons[(int) yield], UIBox2.FromDimensions(at - new Vector2(size * 0.5f), new Vector2(size)));
        }

        var number = state.Numbers[tile];
        if (number == 0)
            return;

        var token = ToPixel(center + new Vector2(0f, 0.32f));
        var produced = state.Rolled && state.Die1 + state.Die2 == number;
        Circle(handle, token + new Vector2(0f, 0.04f * radius), 0.29f * radius, Color.Black.WithAlpha(0.45f));
        if (produced)
            Circle(handle, token, 0.34f * radius, SettlersArt.Rolled);

        Circle(handle, token, 0.29f * radius, SettlersArt.Outline);
        Circle(handle, token, 0.27f * radius, SettlersArt.Token);
        var text = number.ToString();
        var textColor = SettlersRules.IsHot(number) ? SettlersArt.TokenHot : SettlersArt.TokenText;
        var dimensions = handle.GetDimensions(_numberFont, text, UIScale);
        handle.DrawString(_numberFont, token - new Vector2(dimensions.X * 0.5f, dimensions.Y * 0.62f), text, UIScale, textColor);

        var pips = SettlersRules.Odds(number);
        var gap = 0.065f * radius;
        for (var pip = 0; pip < pips; pip++)
        {
            Circle(handle, token + new Vector2((pip - (pips - 1) * 0.5f) * gap, 0.15f * radius), 0.022f * radius + 0.5f, textColor);
        }
    }

    /// <summary>Each dock: a marker off the coast with its trade, tied to the two corners that use it.</summary>
    private void DrawDocks(DrawingHandleScreen handle, SettlersState state, float radius)
    {
        for (var dock = 0; dock < SettlersBoard.DockCount; dock++)
        {
            var edge = SettlersBoard.DockEdges[dock];
            var mid = SettlersBoard.EdgeMid(edge);
            var at = mid + Vector2.Normalize(mid) * 0.62f;
            var pixel = ToPixel(at);
            var resource = state.Docks[dock];

            var fill = resource == SettlersRules.AnyDock ? SettlersArt.AnyDockColor : SettlersArt.SectorColors[resource];
            var pier = Color.InterpolateBetween(fill, DockLine, 0.5f);
            DrawThickLine(handle, pixel, ToPixel(SettlersBoard.VertexPos[SettlersBoard.EdgeA[edge]]), 0.07f * radius, pier);
            DrawThickLine(handle, pixel, ToPixel(SettlersBoard.VertexPos[SettlersBoard.EdgeB[edge]]), 0.07f * radius, pier);

            Circle(handle, pixel, 0.31f * radius, SettlersArt.Outline);
            Circle(handle, pixel, 0.28f * radius, Color.InterpolateBetween(fill, Color.White, 0.2f));
            Circle(handle, pixel, 0.25f * radius, fill);

            var ratio = resource == SettlersRules.AnyDock ? SettlersRules.AnyDockRatio : SettlersRules.ResourceDockRatio;
            var text = Loc.GetString("wg-settlers-ratio", ("ratio", ratio));
            if (resource != SettlersRules.AnyDock)
            {
                var size = 0.34f * radius;
                handle.DrawTextureRect(_icons[resource], UIBox2.FromDimensions(pixel - new Vector2(size * 0.5f, size * 0.62f), new Vector2(size)));
            }

            var dimensions = handle.GetDimensions(_smallFont, text, UIScale);
            var textAt = resource == SettlersRules.AnyDock
                ? pixel - dimensions * 0.5f
                : pixel + new Vector2(-dimensions.X * 0.5f, 0.06f * radius);
            handle.DrawString(_smallFont, textAt, text, UIScale, Color.White);
        }
    }

    private void DrawLane(DrawingHandleScreen handle, int edge, Color color, float radius)
    {
        var a = SettlersBoard.VertexPos[SettlersBoard.EdgeA[edge]];
        var b = SettlersBoard.VertexPos[SettlersBoard.EdgeB[edge]];
        var from = ToPixel(Vector2.Lerp(a, b, 0.14f));
        var to = ToPixel(Vector2.Lerp(a, b, 0.86f));
        DrawThickLine(handle, from, to, 0.2f * radius, SettlersArt.Outline);
        DrawThickLine(handle, from, to, 0.12f * radius, color);
    }

    /// <summary>An outpost or colony silhouette in the owner's colour, outlined and lit along its roof.</summary>
    private void DrawBuilding(DrawingHandleScreen handle, Vector2 at, Color color, bool colony, float radius)
    {
        var shape = colony ? ColonyShape : OutpostShape;
        var scale = (colony ? 0.2f : 0.16f) * radius;
        var outline = MathF.Max(1.5f, 0.035f * radius);

        foreach (var offset in OutlineOffsets)
        {
            DrawShape(handle, shape, at + offset * outline, scale, SettlersArt.Outline.WithAlpha(color.A));
        }

        DrawShape(handle, shape, at, scale, color);
        DrawShape(handle, shape, at + new Vector2(0f, scale * 0.15f), scale * 0.8f, Color.InterpolateBetween(color, Color.Black, 0.2f).WithAlpha(color.A));
    }

    private void DrawShape(DrawingHandleScreen handle, Vector2[] shape, Vector2 at, float scale, Color color)
    {
        var points = new Span<Vector2>(_shape, 0, shape.Length);
        for (var i = 0; i < shape.Length; i++)
        {
            points[i] = at + shape[i] * scale;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, Color.ToSrgb(color));
    }

    private void DrawPirates(DrawingHandleScreen handle, Vector2 at, float radius)
    {
        Circle(handle, at, 0.4f * radius, SettlersArt.TokenHot);
        Circle(handle, at, 0.36f * radius, Color.FromHex("#120A0C"));
        var size = 0.52f * radius;
        handle.DrawTextureRect(_pirate, UIBox2.FromDimensions(at - new Vector2(size * 0.5f), new Vector2(size)));
    }

    /// <summary>Rings every legal target; the one under the mouse is filled in the player's colour.</summary>
    private void DrawTargets(DrawingHandleScreen handle, float radius)
    {
        if (Pick == SettlersPick.None || Seat is not { } seat)
            return;

        var color = SettlersArt.SeatColors[seat];
        for (var target = 0; target < TargetCount; target++)
        {
            if (!Legal(target))
                continue;

            var hovered = target == _hover;
            switch (Pick)
            {
                case SettlersPick.Lane:
                    var a = SettlersBoard.VertexPos[SettlersBoard.EdgeA[target]];
                    var b = SettlersBoard.VertexPos[SettlersBoard.EdgeB[target]];
                    DrawThickLine(handle, ToPixel(Vector2.Lerp(a, b, 0.2f)), ToPixel(Vector2.Lerp(a, b, 0.8f)),
                        (hovered ? 0.14f : 0.06f) * radius, hovered ? color : Color.White.WithAlpha(0.55f));
                    break;

                case SettlersPick.Pirates:
                    var center = ToPixel(SettlersBoard.TileCenter[target]);
                    Circle(handle, center, 0.5f * radius, hovered ? SettlersArt.TokenHot.WithAlpha(0.45f) : Color.White.WithAlpha(0.12f));
                    break;

                default:
                    var at = ToPixel(SettlersBoard.VertexPos[target]);
                    if (hovered)
                        DrawBuilding(handle, at, color.WithAlpha(0.85f), Pick == SettlersPick.Colony, radius);
                    else
                        Circle(handle, at, 0.13f * radius, Color.White.WithAlpha(0.6f), false);
                    break;
            }
        }
    }

    /// <summary>The last roll, as two dice in the top left corner.</summary>
    private void DrawDice(DrawingHandleScreen handle, SettlersState state, float radius)
    {
        if (state.RollCount == 0)
            return;

        var size = 0.5f * radius;
        var origin = new Vector2(0.25f * radius);
        DrawDie(handle, origin, size, state.Die1);
        DrawDie(handle, origin + new Vector2(size * 1.25f, 0f), size, state.Die2);
    }

    private static void DrawDie(DrawingHandleScreen handle, Vector2 at, float size, int value)
    {
        var corner = size * 0.18f;
        DrawRoundRect(handle, UIBox2.FromDimensions(at + new Vector2(size * 0.06f), new Vector2(size)), corner, Color.Black.WithAlpha(0.5f));
        DrawRoundRect(handle, UIBox2.FromDimensions(at, new Vector2(size)), corner, SettlersArt.Token);

        var center = at + new Vector2(size * 0.5f);
        var step = size * 0.27f;
        var pip = size * 0.08f;
        var color = SettlersArt.TokenText;
        if (value % 2 == 1)
            Circle(handle, center, pip, color);
        if (value >= 2)
        {
            Circle(handle, center + new Vector2(-step, -step), pip, color);
            Circle(handle, center + new Vector2(step, step), pip, color);
        }

        if (value >= 4)
        {
            Circle(handle, center + new Vector2(step, -step), pip, color);
            Circle(handle, center + new Vector2(-step, step), pip, color);
        }

        if (value == 6)
        {
            Circle(handle, center + new Vector2(-step, 0f), pip, color);
            Circle(handle, center + new Vector2(step, 0f), pip, color);
        }
    }

    /// <summary>
    /// Polygon draws convert their colour from sRGB twice and come out too dark, so shapes pass theirs through
    /// <see cref="Color.ToSrgb"/> first; rects and textures are drawn as given.
    /// </summary>
    private static void Circle(DrawingHandleScreen handle, Vector2 at, float radius, Color color, bool filled = true)
    {
        handle.DrawCircle(at, radius, Color.ToSrgb(color), filled);
    }

    private static void DrawRoundRect(DrawingHandleScreen handle, UIBox2 box, float corner, Color color)
    {
        handle.DrawRect(new UIBox2(box.Left + corner, box.Top, box.Right - corner, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Left, box.Top + corner, box.Left + corner, box.Bottom - corner), color);
        handle.DrawRect(new UIBox2(box.Right - corner, box.Top + corner, box.Right, box.Bottom - corner), color);
        Circle(handle, new Vector2(box.Left + corner, box.Top + corner), corner, color);
        Circle(handle, new Vector2(box.Right - corner, box.Top + corner), corner, color);
        Circle(handle, new Vector2(box.Left + corner, box.Bottom - corner), corner, color);
        Circle(handle, new Vector2(box.Right - corner, box.Bottom - corner), corner, color);
    }

    private void DrawThickLine(DrawingHandleScreen handle, Vector2 from, Vector2 to, float width, Color color)
    {
        var along = to - from;
        if (along.LengthSquared() < 0.01f)
            return;

        var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * (width * 0.5f);
        _quad[0] = from + side;
        _quad[1] = to + side;
        _quad[2] = to - side;
        _quad[3] = from - side;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _quad, Color.ToSrgb(color));
    }
}
