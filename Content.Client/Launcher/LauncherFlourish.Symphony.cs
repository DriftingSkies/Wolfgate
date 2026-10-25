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
/// A moproach drifting about behind the refusal, facing the way it goes and bouncing off the edges. Drawn straight
/// from its sprite, since the connect screen has no world to put an entity in. Never takes a click.
/// </summary>
public sealed class SymphonyMoproach : Control
{
    private const float Scale = 3f;
    private const float Speed = 70f;
    private readonly IRsiStateLike _state;
    private Vector2 _pos = new(120, 160);
    private Vector2 _vel;
    private float _time;
    private float _frameTime;
    private int _frame;
    private float _turnIn = 4f;
    private readonly System.Random _random = new();

    public SymphonyMoproach()
    {
        MouseFilter = MouseFilterMode.Ignore;
        var rsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>("/Textures/Mobs/Animals/mothroach/moproach.rsi").RSI;
        _state = rsi.TryGetState("mothroach-moving", out var moving) ? moving : rsi["icon"];
        _vel = Heading(_random.NextSingle() * MathF.Tau);
    }

    private static Vector2 Heading(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Speed;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        var dt = args.DeltaSeconds;
        _time += dt;

        // Now and then it changes its mind about where it is going.
        _turnIn -= dt;
        if (_turnIn <= 0)
        {
            var angle = MathF.Atan2(_vel.Y, _vel.X) + (_random.NextSingle() - 0.5f) * 1.6f;
            _vel = Heading(angle);
            _turnIn = 2f + _random.NextSingle() * 4f;
        }

        var size = 32 * Scale * UIScale;
        var bounds = PixelSize;
        _pos += _vel * dt * UIScale;
        if (_pos.X < 0 || _pos.X > bounds.X - size)
        {
            _vel.X = -_vel.X;
            _pos.X = Math.Clamp(_pos.X, 0, Math.Max(0, bounds.X - size));
        }
        if (_pos.Y < 0 || _pos.Y > bounds.Y - size)
        {
            _vel.Y = -_vel.Y;
            _pos.Y = Math.Clamp(_pos.Y, 0, Math.Max(0, bounds.Y - size));
        }

        _frameTime += dt;
        var delay = _state.GetDelay(_frame);
        if (_state.IsAnimated && _frameTime >= delay)
        {
            _frameTime -= delay;
            _frame = (_frame + 1) % _state.AnimationFrameCount;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        // The way it faces is the way it mostly goes; a gentle bob makes it float.
        var dir = MathF.Abs(_vel.X) > MathF.Abs(_vel.Y)
            ? (_vel.X > 0 ? RsiDirection.East : RsiDirection.West)
            : (_vel.Y > 0 ? RsiDirection.South : RsiDirection.North);
        var texture = _state.GetFrame(dir, _frame);
        var size = 32 * Scale * UIScale;
        var bob = MathF.Sin(_time * 3f) * 4f * UIScale;
        handle.DrawTextureRect(texture, UIBox2.FromDimensions(new Vector2(_pos.X, _pos.Y + bob), new Vector2(size, size)));
    }
}
