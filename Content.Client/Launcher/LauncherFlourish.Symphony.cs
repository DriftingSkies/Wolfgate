using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ResourceManagement;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Launcher;

/// <summary>
/// A button that glows Discord blue and breathes, so the way in is the first thing a refused player sees.
/// </summary>
public sealed class SymphonyPulseButton : Button
{
    private static readonly Color Blue = Color.FromHex("#5865F2");
    private readonly StyleBoxFlat _box = new() { BackgroundColor = Blue, ContentMarginLeftOverride = 14, ContentMarginRightOverride = 14, ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6 };
    private float _time;

    public SymphonyPulseButton()
    {
        StyleBoxOverride = _box;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        _time += args.DeltaSeconds;
        // One breath every 1.6 seconds, between the blue and a lighter one; brighter still while hovered.
        var t = (MathF.Sin(_time * MathF.Tau / 1.6f) + 1f) / 2f;
        var lift = IsHovered ? 0.35f : 0.12f + 0.2f * t;
        _box.BackgroundColor = Color.InterpolateBetween(Blue, Color.White, lift);
        _box.BorderColor = Color.InterpolateBetween(Blue, Color.White, 0.5f * t);
        _box.BorderThickness = new Thickness(2);
    }
}

/// <summary>
/// A moproach on the connect screen, mopping up the spills that turn up around the window and wandering about once
/// the floor is clean. Drawn straight from the sprites, since the connect screen has no world to put entities in.
/// Never takes a click.
/// </summary>
public sealed class SymphonyMoproach : Control
{
    private const float Scale = 3f;
    private const float Speed = 70f;
    private const float MopSeconds = 2.6f;
    private const float WetSeconds = 1.8f;
    private const int MaxSpills = 4;
    // A loading hitch would otherwise throw it across the screen in one frame.
    private const float MaxFrame = 0.1f;
    // Water, blood, slime, coffee and space lube, see-through like a real puddle.
    private static readonly Color[] SpillColors =
    [
        Color.FromHex("#5BC0FFB0"), Color.FromHex("#B3242AB0"), Color.FromHex("#6CDC4AB0"), Color.FromHex("#6B4024B0"), Color.FromHex("#E080E0B0"),
    ];

    private readonly IRsiStateLike _state;
    private readonly IRsiStateLike _sparkles;
    private readonly List<(Texture Texture, bool Tinted)> _splats = new();
    private readonly List<Spill> _spills = new();
    private readonly Control[] _avoid;
    private readonly System.Random _random = new();
    private Vector2 _pos = new(120, 160);
    private Vector2 _vel;
    private RsiDirection _facing = RsiDirection.South;
    private Spill? _target;
    private bool _scrubbing;
    private float _time;
    private float _frameTime;
    private int _frame;
    private float _turnIn = 4f;
    private float _spillIn = 1.5f;

    private sealed class Spill
    {
        public Vector2 Pos;
        public Texture Texture = default!;
        public Color Color;
        public float Left = 1f;
        public float Wet;
        public float Age;
    }

    /// <param name="avoid">Windows a spill should not land under, where nobody would see it mopped.</param>
    public SymphonyMoproach(params Control[] avoid)
    {
        MouseFilter = MouseFilterMode.Ignore;
        _avoid = avoid;
        var cache = IoCManager.Resolve<IResourceCache>();
        var rsi = cache.GetResource<RSIResource>("/Textures/Mobs/Animals/mothroach/moproach.rsi").RSI;
        _state = rsi.TryGetState("mothroach-moving", out var moving) ? moving : rsi["icon"];
        _sparkles = cache.GetResource<RSIResource>("/Textures/Fluids/wet_floor_sparkles.rsi").RSI["sparkles"];
        // Only the free-standing decals: puddle.rsi's splat1 to splat15 are tile pieces and look like squares alone.
        void Add(string path, bool tinted, params string[] states)
        {
            var decals = cache.GetResource<RSIResource>(path).RSI;
            foreach (var state in states)
                _splats.Add((decals[state].GetFrame(RsiDirection.South, 0), tinted));
        }
        Add("/Textures/Fluids/puddle.rsi", true, "splata", "splatb", "splat0");
        Add("/Textures/Fluids/splatter.rsi", true, "splatter-0", "splatter-1", "splatter-2", "splatter-3", "splatter-4", "splatter-5");
        Add("/Textures/Fluids/smear.rsi", true, "smear-0", "smear-1", "smear-2", "smear-3", "smear-4", "smear-5", "smear-6");
        Add("/Textures/Fluids/vomit.rsi", false, "vomit-0", "vomit-1", "vomit-2", "vomit-3");
        Add("/Textures/Fluids/tomato_splat.rsi", false, "puddle-0", "puddle-1", "puddle-2");
        _vel = Heading(_random.NextSingle() * MathF.Tau);
    }

    private static Vector2 Heading(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Speed;

    private float Size => 32 * Scale * UIScale;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        // Until the screen has room for it there is nowhere to go; bouncing in a zero-sized box made it twitch.
        if (PixelSize.X < Size * 2 || PixelSize.Y < Size * 2)
            return;
        var dt = MathF.Min(args.DeltaSeconds, MaxFrame);
        _time += dt;

        // Somebody keeps spilling things, a few at a time.
        _spillIn -= dt;
        if (_spillIn <= 0)
        {
            if (_spills.Count(s => s.Left > 0) < MaxSpills)
                TrySpill();
            _spillIn = 4f + _random.NextSingle() * 6f;
        }

        // A mopped spot stays wet and sparkly for a moment before it dries.
        foreach (var spill in _spills)
        {
            spill.Age += dt;
            if (spill.Left <= 0)
                spill.Wet -= dt;
        }
        _spills.RemoveAll(s => s.Left <= 0 && s.Wet <= 0);

        _target ??= Nearest();
        _scrubbing = false;
        if (_target != null)
            Chase(_target, dt);
        else
            Wander(dt);

        _frameTime += dt;
        var delay = _state.GetDelay(_frame);
        if (_state.IsAnimated && _frameTime >= delay)
        {
            _frameTime -= delay;
            _frame = (_frame + 1) % _state.AnimationFrameCount;
        }
    }

    private Spill? Nearest()
    {
        Spill? best = null;
        var bestDistance = float.MaxValue;
        foreach (var spill in _spills)
        {
            if (spill.Left <= 0)
                continue;
            var distance = (spill.Pos - _pos).LengthSquared();
            if (distance < bestDistance)
                (best, bestDistance) = (spill, distance);
        }
        return best;
    }

    /// <summary>Heads for the spill, then scrubs side to side over it until it is gone.</summary>
    private void Chase(Spill spill, float dt)
    {
        var to = spill.Pos - _pos;
        var step = Speed * 1.3f * dt * UIScale;
        if (to.Length() > step)
        {
            _vel = to / to.Length() * Speed * 1.3f;
            _pos += to / to.Length() * step;
            _facing = Facing(_vel);
            return;
        }

        _pos = spill.Pos;
        _scrubbing = true;
        _facing = MathF.Sin(_time * MathF.Tau * 2.5f) > 0 ? RsiDirection.East : RsiDirection.West;
        spill.Left -= dt / MopSeconds;
        if (spill.Left > 0)
            return;

        // Done: a little break before the next one.
        spill.Wet = WetSeconds;
        _target = null;
        _vel = Heading(_random.NextSingle() * MathF.Tau);
        _turnIn = 1f + _random.NextSingle() * 2f;
    }

    private void Wander(float dt)
    {
        // Now and then it changes its mind about where it is going.
        _turnIn -= dt;
        if (_turnIn <= 0)
        {
            var angle = MathF.Atan2(_vel.Y, _vel.X) + (_random.NextSingle() - 0.5f) * 1.6f;
            _vel = Heading(angle);
            _turnIn = 2f + _random.NextSingle() * 4f;
        }

        var size = Size;
        var bounds = PixelSize;
        _pos += _vel * dt * UIScale;
        // Turn only when heading into the edge, so one past it after a resize walks back instead of flipping each frame.
        if ((_pos.X < 0 && _vel.X < 0) || (_pos.X > bounds.X - size && _vel.X > 0))
            _vel.X = -_vel.X;
        if ((_pos.Y < 0 && _vel.Y < 0) || (_pos.Y > bounds.Y - size && _vel.Y > 0))
            _vel.Y = -_vel.Y;
        _pos = Vector2.Clamp(_pos, Vector2.Zero, new Vector2(bounds.X - size, bounds.Y - size));
        _facing = Facing(_vel);
    }

    private static RsiDirection Facing(Vector2 vel) => MathF.Abs(vel.X) > MathF.Abs(vel.Y)
        ? (vel.X > 0 ? RsiDirection.East : RsiDirection.West)
        : (vel.Y > 0 ? RsiDirection.South : RsiDirection.North);

    /// <summary>A spill somewhere no window covers; gives up quietly when it finds no room.</summary>
    private void TrySpill()
    {
        var size = Size;
        var bounds = PixelSize;
        if (bounds.X < size * 2 || bounds.Y < size * 2)
            return;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var pos = new Vector2(_random.NextSingle() * (bounds.X - size), _random.NextSingle() * (bounds.Y - size));
            var box = UIBox2.FromDimensions(pos, new Vector2(size, size));
            if (_avoid.Any(c => c.VisibleInTree && Covers(c, box)) || _spills.Any(s => (s.Pos - pos).Length() < size))
                continue;
            var (texture, tinted) = _splats[_random.Next(_splats.Count)];
            _spills.Add(new Spill
            {
                Pos = pos,
                Texture = texture,
                Color = tinted ? SpillColors[_random.Next(SpillColors.Length)] : Color.White.WithAlpha(0.85f),
            });
            return;
        }
    }

    private bool Covers(Control control, UIBox2 box)
    {
        var rect = control.GlobalPixelRect;
        var at = GlobalPixelPosition;
        return new UIBox2(rect.Left - at.X, rect.Top - at.Y, rect.Right - at.X, rect.Bottom - at.Y).Intersects(box);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = Size;
        // Spills sit under its feet, a quarter of the way down from where it stands.
        var feet = new Vector2(0, size * 0.25f);

        // A spill shrinks and fades as it is mopped, and sparkles while the floor dries.
        foreach (var spill in _spills)
        {
            if (spill.Left > 0)
            {
                var shrunk = size * (0.35f + 0.65f * spill.Left);
                var at = spill.Pos + feet + new Vector2((size - shrunk) / 2f);
                handle.DrawTextureRect(spill.Texture, UIBox2.FromDimensions(at, new Vector2(shrunk)), spill.Color.WithAlpha(spill.Color.A * (0.3f + 0.7f * spill.Left)));
            }
            if (spill.Left < 0.6f)
            {
                var fade = spill.Left > 0 ? 1f : Math.Clamp(spill.Wet / WetSeconds, 0f, 1f);
                var frame = (int) (spill.Age / 0.15f) % _sparkles.AnimationFrameCount;
                handle.DrawTextureRect(_sparkles.GetFrame(RsiDirection.South, frame), UIBox2.FromDimensions(spill.Pos + feet, new Vector2(size)), Color.White.WithAlpha(fade));
            }
        }

        // A gentle bob makes it float; while scrubbing it shuffles side to side instead.
        var bob = _scrubbing
            ? new Vector2(MathF.Sin(_time * MathF.Tau * 2.5f) * 5f * UIScale, 0)
            : new Vector2(0, MathF.Sin(_time * 3f) * 4f * UIScale);
        handle.DrawTextureRect(_state.GetFrame(_facing, _frame), UIBox2.FromDimensions(_pos + bob, new Vector2(size)));
    }
}
