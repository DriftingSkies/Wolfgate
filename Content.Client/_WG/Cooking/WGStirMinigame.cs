using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WG.Cooking;

/// <summary>
/// The stirring minigame: the pot seen from above, its soup swirling, with a ring between its middle and its rim. The
/// cook holds the spoon down and stirs round in circles; three full turns finish it. Turns made inside the ring count
/// fully, strokes that scrape the rim or poke the middle count for less. Reports a score from 0 to 1.
/// </summary>
public sealed class WGStirMinigame : Control
{
    private static readonly Color PotColor = Color.FromHex("#2E2E34");
    private static readonly Color RimColor = Color.FromHex("#55555E");
    private static readonly Color RingColor = Color.FromHex("#FFFFFF40");
    private static readonly Color SwirlColor = Color.FromHex("#FFFFFF30");
    private static readonly Color SpoonColor = Color.FromHex("#C8A070");

    /// <summary>Turns that finish the stir.</summary>
    private const float Turns = 3f;

    /// <summary>The ring the spoon should stay in, as shares of the pot's radius.</summary>
    private const float Inner = 0.35f;
    private const float Outer = 0.85f;

    public event Action<float>? OnFinished;

    /// <summary>The soup's colour, tinting the pot's surface.</summary>
    public Color Soup = Color.FromHex("#A07040");

    private float _angle;
    private float _good;
    private float _total;
    private float _swirl;
    private Vector2? _last;
    private Vector2 _spoon;
    private bool _stirring;
    private bool _done;

    /// <summary>The way round the stir goes, set by its first stroke: 1 or -1, 0 before it starts.</summary>
    private int _direction;

    public WGStirMinigame()
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(260, 260);
    }

    /// <summary>The share of the three turns done so far, 0 to 1.</summary>
    public float Progress => Math.Clamp(_total / (Turns * MathF.Tau), 0f, 1f);

    public void Start()
    {
        _angle = _good = _total = 0;
        _last = null;
        _done = false;
        _direction = 0;
    }

    private Vector2 Centre => Size / 2f;
    private float Radius => Math.Min(Size.X, Size.Y) * 0.45f;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        // The soup keeps turning a little after each stroke.
        _swirl += args.DeltaSeconds * (0.4f + 2f * Progress);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (_done || args.Function != EngineKeyFunctions.UIClick)
            return;
        _stirring = true;
        _last = args.RelativePosition - Centre;
        _spoon = args.RelativePosition;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        _stirring = false;
        _last = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _spoon = args.RelativePosition;
        if (!_stirring || _done || _last is not { } last)
            return;

        var now = args.RelativePosition - Centre;
        var step = MathF.Atan2(now.Y, now.X) - MathF.Atan2(last.Y, last.X);
        if (step > MathF.PI)
            step -= MathF.Tau;
        else if (step < -MathF.PI)
            step += MathF.Tau;

        // Only turning one way round counts, the way the stir started; wiggling the spoon back and forth does nothing.
        _last = now;
        if (Math.Abs(step) < 0.001f)
            return;
        if (_direction == 0)
            _direction = Math.Sign(step);
        if (Math.Sign(step) != _direction)
            return;

        var turn = Math.Abs(step);
        var reach = now.Length() / Math.Max(1f, Radius);
        _total += turn;
        if (reach >= Inner && reach <= Outer)
            _good += turn;
        _swirl += step;

        if (_total >= Turns * MathF.Tau)
        {
            _done = true;
            _stirring = false;
            OnFinished?.Invoke(Math.Clamp(_good / _total, 0f, 1f));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var scale = UIScale;
        var centre = Centre * scale;
        var radius = Radius * scale;

        handle.DrawCircle(centre, radius + 8f * scale, RimColor);
        handle.DrawCircle(centre, radius, PotColor);
        handle.DrawCircle(centre, radius * 0.95f, Soup.WithAlpha(0.85f));

        // Swirl lines on the soup, turning with the stirring.
        for (var arm = 0; arm < 4; arm++)
        {
            var start = _swirl + arm * MathF.PI / 2f;
            Vector2? previous = null;
            for (var t = 0f; t <= 1f; t += 0.08f)
            {
                var a = start + t * 2.2f;
                var r = radius * (0.15f + 0.75f * t);
                var p = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (previous is { } prev)
                    handle.DrawLine(prev, p, SwirlColor);
                previous = p;
            }
        }

        // The ring to stir in.
        handle.DrawCircle(centre, radius * Outer, RingColor, false);
        handle.DrawCircle(centre, radius * Inner, RingColor, false);

        // Progress round the rim.
        var done = Progress * MathF.Tau;
        Vector2? last = null;
        for (var a = 0f; a <= done; a += 0.05f)
        {
            var p = centre + new Vector2(MathF.Cos(a - MathF.PI / 2), MathF.Sin(a - MathF.PI / 2)) * (radius + 4f * scale);
            if (last is { } l)
                handle.DrawLine(l, p, Color.FromHex("#6FCF6F"));
            last = p;
        }

        // The spoon.
        handle.DrawCircle(_spoon * scale, 7f * scale, SpoonColor);
    }
}
