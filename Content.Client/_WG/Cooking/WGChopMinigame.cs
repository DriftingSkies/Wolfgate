using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WG.Cooking;

/// <summary>
/// The chopping minigame, after Casualties Unknown. The ingredient lies big on a wooden board with dashed guide lines
/// across it, spaced over its own visible shape; the cook drags the knife down along each one. A cut that runs most of
/// the way down the ingredient and stays near an uncut line scores by how straight and close it was, flashes, and the
/// pieces either side slide apart; one that wanders off leaves a red scratch. When every line is cut, or the cook runs
/// out of strokes, it reports a score from 0 to 1.
/// </summary>
public sealed class WGChopMinigame : Control
{
    private static readonly Color BoardColor = Color.FromHex("#78583A");
    private static readonly Color GrainColor = Color.FromHex("#6C4E32");
    private static readonly Color GuideColor = Color.FromHex("#FFFFFFAA");
    private static readonly Color CutColor = Color.FromHex("#6FCF6F");
    private static readonly Color MissColor = Color.FromHex("#E05A4F");
    private static readonly Color StrokeColor = Color.FromHex("#F0E0B0");

    /// <summary>How far off a line a cut may stray and still count, as a share of the ingredient's width.</summary>
    private const float Tolerance = 0.12f;

    /// <summary>How much of the ingredient's height a stroke must run to be a cut.</summary>
    private const float Reach = 0.7f;

    /// <summary>How long a cut takes to slide its pieces apart, and how far, in UI units.</summary>
    private const float SlideTime = 0.25f;
    private const float SlideGap = 10f;
    private const float FlashTime = 0.35f;

    public event Action<float>? OnFinished;
    public event Action<string>? OnStroke;

    private Texture? _texture;

    /// <summary>The ingredient's visible pixels in its texture, as fractions (left, top, right, bottom).</summary>
    private UIBox2 _bounds = new(0, 0, 1, 1);

    private readonly List<float> _guides = new();
    private readonly List<float> _scores = new();
    private readonly Dictionary<int, float> _cutAt = new();
    private readonly List<(Vector2 A, Vector2 B)> _misses = new();
    private readonly List<Vector2> _stroke = new();
    private float _time;
    private bool _dragging;
    private int _strokesLeft;
    private bool _done;

    public WGChopMinigame()
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(260, 260);
    }

    /// <summary>
    /// Starts over on <paramref name="texture"/> with <paramref name="cuts"/> guide lines across the ingredient's visible
    /// shape, <paramref name="bounds"/> (fractions of the texture: left, top, right, bottom), or the middle if unknown.
    /// </summary>
    public void Start(Texture? texture, UIBox2? bounds, int cuts)
    {
        _texture = texture;
        _bounds = bounds ?? new UIBox2(0.2f, 0.2f, 0.8f, 0.8f);
        _guides.Clear();
        _scores.Clear();
        _cutAt.Clear();
        _misses.Clear();
        _stroke.Clear();
        _done = false;
        _time = 0;
        cuts = Math.Clamp(cuts, 1, 4);
        for (var i = 1; i <= cuts; i++)
            _guides.Add(i / (cuts + 1f));
        _strokesLeft = cuts + 2;
    }

    /// <summary>The square the texture is drawn in, in UI units: as big as fits, centred.</summary>
    private UIBox2 Square()
    {
        var side = Math.Min(Size.X, Size.Y) * 0.92f;
        var origin = (Size - new Vector2(side, side)) / 2f;
        return UIBox2.FromDimensions(origin, new Vector2(side, side));
    }

    /// <summary>The ingredient's visible shape on screen, in UI units.</summary>
    private UIBox2 Ingredient()
    {
        var square = Square();
        return new UIBox2(square.Left + _bounds.Left * square.Width, square.Top + _bounds.Top * square.Height,
            square.Left + _bounds.Right * square.Width, square.Top + _bounds.Bottom * square.Height);
    }

    /// <summary>A guide line's x on screen, in UI units.</summary>
    private float GuideX(int i)
    {
        var box = Ingredient();
        return box.Left + _guides[i] * box.Width;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (_done || args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = true;
        _stroke.Clear();
        _stroke.Add(args.RelativePosition);
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_dragging)
            _stroke.Add(args.RelativePosition);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (!_dragging || args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = false;
        Judge();
        args.Handle();
    }

    /// <summary>Scores the stroke just made against the nearest uncut line.</summary>
    private void Judge()
    {
        if (_stroke.Count < 2)
            return;

        var box = Ingredient();
        var width = Math.Max(1f, box.Width);
        var height = Math.Max(1f, box.Height);
        var meanX = _stroke.Average(p => p.X);
        var top = Math.Max(box.Top, _stroke.Min(p => p.Y));
        var bottom = Math.Min(box.Bottom, _stroke.Max(p => p.Y));
        _strokesLeft--;

        var nearest = -1;
        var best = float.MaxValue;
        for (var i = 0; i < _guides.Count; i++)
        {
            if (_cutAt.ContainsKey(i))
                continue;
            var d = Math.Abs(meanX - GuideX(i)) / width;
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        var wander = _stroke.Average(p => Math.Abs(p.X - meanX)) / width;
        var reached = (bottom - top) / height >= Reach;
        if (nearest >= 0 && reached && best <= Tolerance)
        {
            var score = Math.Clamp(1f - best / Tolerance * 0.6f - wander / Tolerance * 0.6f, 0.2f, 1f);
            _cutAt[nearest] = _time;
            _scores.Add(score);
            OnStroke?.Invoke(Loc.GetString(score >= 0.85f ? "wg-chop-clean" : "wg-chop-ok"));
        }
        else
        {
            _misses.Add((_stroke[0], _stroke[^1]));
            OnStroke?.Invoke(Loc.GetString(reached ? "wg-chop-off-line" : "wg-chop-too-short"));
        }

        _stroke.Clear();
        if (_cutAt.Count == _guides.Count || _strokesLeft <= 0)
        {
            _done = true;
            OnFinished?.Invoke(_guides.Count == 0 ? 1f : _scores.Sum() / _guides.Count);
        }
    }

    /// <summary>How far a cut has slid the pieces right of it, 0 to 1, easing out.</summary>
    private float Slide(int guide)
    {
        if (!_cutAt.TryGetValue(guide, out var at))
            return 0f;
        var t = Math.Clamp((_time - at) / SlideTime, 0f, 1f);
        return 1f - (1f - t) * (1f - t);
    }

    private bool _drawFailed;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_drawFailed)
            return;

        // A drawing fault here must never take the whole client down; it just stops drawing the minigame.
        try
        {
            DrawBoard(handle);
        }
        catch (Exception e)
        {
            _drawFailed = true;
            Logger.GetSawmill("wg.cooking").Error($"Chopping minigame failed to draw: {e}");
        }
    }

    private void DrawBoard(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        var size = PixelSize;

        // The board.
        handle.DrawRect(new UIBox2(Vector2.Zero, size), BoardColor);
        for (var y = 0f; y < size.Y; y += 22f * scale)
            handle.DrawLine(new Vector2(0, y), new Vector2(size.X, y + 6f * scale), GrainColor);

        var square = Square();
        var box = Ingredient();

        // The ingredient, drawn in pieces between its cut lines; each piece is pushed right by every cut left of it.
        if (_texture != null)
        {
            var edges = new List<(float Frac, int Guide)> { (0f, -1) };
            for (var i = 0; i < _guides.Count; i++)
            {
                if (_cutAt.ContainsKey(i))
                    edges.Add(((box.Left + _guides[i] * box.Width - square.Left) / square.Width, i));
            }
            edges.Add((1f, -1));
            edges.Sort((a, b) => a.Frac.CompareTo(b.Frac));

            var shift = 0f;
            for (var k = 0; k < edges.Count - 1; k++)
            {
                if (edges[k].Guide >= 0)
                    shift += Slide(edges[k].Guide) * SlideGap;
                var from = edges[k].Frac;
                var to = edges[k + 1].Frac;
                if (to <= from)
                    continue;

                var region = UIBox2.FromDimensions(new Vector2(from * _texture.Width, 0),
                    new Vector2((to - from) * _texture.Width, _texture.Height));
                var dest = new UIBox2(
                    (square.Left + from * square.Width + shift) * scale, square.Top * scale,
                    (square.Left + to * square.Width + shift) * scale, square.Bottom * scale);
                handle.DrawTextureRectRegion(_texture, dest, region);
            }
        }

        // Guide lines over the ingredient only. A fresh cut flashes white in the middle of its opening gap, then fades
        // to green and away, leaving the gap to show the cut.
        for (var i = 0; i < _guides.Count; i++)
        {
            var x = GuideX(i) * scale;
            if (_cutAt.TryGetValue(i, out var at))
            {
                var before = 0f;
                for (var j = 0; j < _guides.Count; j++)
                {
                    if (j != i && _guides[j] < _guides[i])
                        before += Slide(j) * SlideGap;
                }

                var age = _time - at;
                var flash = Math.Clamp(1f - age / FlashTime, 0f, 1f);
                var fade = Math.Clamp(1f - (age - FlashTime) / 0.4f, 0f, 1f);
                if (fade <= 0f)
                    continue;
                var lineX = x + (before + Slide(i) * SlideGap / 2f) * scale;
                var color = Color.InterpolateBetween(CutColor, Color.White, flash).WithAlpha(fade);
                handle.DrawLine(new Vector2(lineX, box.Top * scale), new Vector2(lineX, box.Bottom * scale), color);
                continue;
            }

            for (var y = box.Top * scale; y < box.Bottom * scale; y += 12f * scale)
                handle.DrawLine(new Vector2(x, y), new Vector2(x, Math.Min(box.Bottom * scale, y + 7f * scale)), GuideColor);
        }

        foreach (var (a, b) in _misses)
            handle.DrawLine(a * scale, b * scale, MissColor);

        for (var i = 1; i < _stroke.Count; i++)
            handle.DrawLine(_stroke[i - 1] * scale, _stroke[i] * scale, StrokeColor);
    }
}
