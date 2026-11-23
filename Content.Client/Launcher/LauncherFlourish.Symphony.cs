using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Launcher;

/// <summary>
/// A button that glows green and breathes, so the way in is the first thing a refused player sees.
/// </summary>
public sealed class SymphonyPulseButton : Button
{
    private static readonly Color Green = Color.FromHex("#2E9E4F");
    private readonly StyleBoxFlat _box = new() { BackgroundColor = Green, ContentMarginLeftOverride = 14, ContentMarginRightOverride = 14, ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6 };
    private float _time;

    public SymphonyPulseButton()
    {
        StyleBoxOverride = _box;
        ModulateSelfOverride = Color.White;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        _time += args.DeltaSeconds;
        // One breath every 1.6 seconds, between the green and a lighter one; brighter still while hovered.
        var t = (MathF.Sin(_time * MathF.Tau / 1.6f) + 1f) / 2f;
        var lift = IsHovered ? 0.35f : 0.12f + 0.2f * t;
        _box.BackgroundColor = Color.InterpolateBetween(Green, Color.White, lift);
        _box.BorderColor = Color.InterpolateBetween(Green, Color.White, 0.5f * t);
        _box.BorderThickness = new Thickness(2);
    }
}

/// <summary>
/// A moproach on the connect and join queue screens. It wanders about asking for something to mop; a click on the
/// background spills a mess there, and it squeaks and rushes over to clean it. Drawn straight from the sprites, since
/// these screens have no world to put entities in.
/// </summary>
public sealed class SymphonyMoproach : Control
{
    private const float Scale = 3f;
    private const float Speed = 70f;
    private const float RushSpeed = 160f;
    private const float MopSeconds = 2.6f;
    private const float WetSeconds = 1.8f;
    private const float ExcitedSeconds = 2.5f;
    private const float SaySeconds = 3.5f;
    private const float SoundCooldown = 0.35f;
    private const int MaxSpills = 12;
    private const int MaxSounds = 4;
    private const int AskLines = 5;
    private const int ExcitedLines = 4;
    // A loading hitch would otherwise throw it across the screen in one frame.
    private const float MaxFrame = 0.1f;
    // Water, blood, slime, coffee and space lube, see-through like a real puddle.
    private static readonly Color[] SpillColors =
    [
        Color.FromHex("#5BC0FFB0"), Color.FromHex("#B3242AB0"), Color.FromHex("#6CDC4AB0"), Color.FromHex("#6B4024B0"), Color.FromHex("#E080E0B0"),
    ];
    private static readonly string[] Sounds =
    [
        "/Audio/Voice/Moth/moth_chitter.ogg", "/Audio/Voice/Moth/moth_squeak.ogg", "/Audio/Voice/Moth/moth_laugh.ogg",
    ];

    private readonly IAudioManager _audio;
    private readonly IConfigurationManager _cfg;
    private readonly IRsiStateLike _state;
    private readonly IRsiStateLike _sparkles;
    private readonly Font _font;
    private readonly List<AudioStream> _sounds = new();
    private readonly List<IAudioSource> _playing = new();
    private readonly List<(Texture Texture, bool Tinted)> _splats = new();
    private readonly List<Spill> _spills = new();
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
    private float _askIn = 2f;
    private float _excited;
    private float _soundIn;
    private string? _say;
    private float _sayLeft;

    private sealed class Spill
    {
        public Vector2 Pos;
        public Texture Texture = default!;
        public Color Color;
        public float Left = 1f;
        public float Wet;
        public float Age;
    }

    public SymphonyMoproach()
    {
        // Takes the clicks that land on the background; the windows above it still get theirs first.
        MouseFilter = MouseFilterMode.Stop;
        _audio = IoCManager.Resolve<IAudioManager>();
        _cfg = IoCManager.Resolve<IConfigurationManager>();
        var cache = IoCManager.Resolve<IResourceCache>();
        var rsi = cache.GetResource<RSIResource>("/Textures/Mobs/Animals/mothroach/moproach.rsi").RSI;
        _state = rsi.TryGetState("mothroach-moving", out var moving) ? moving : rsi["icon"];
        _sparkles = cache.GetResource<RSIResource>("/Textures/Fluids/wet_floor_sparkles.rsi").RSI["sparkles"];
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 11);
        foreach (var sound in Sounds)
            _sounds.Add(cache.GetResource<AudioResource>(sound).AudioStream);
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

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        args.Handle();
        SpillAt(args.RelativePixelPosition);
    }

    /// <summary>Spills a mess centred on a point, and the moproach drops everything for it.</summary>
    private void SpillAt(Vector2 at)
    {
        var size = Size;
        if (_spills.Count(s => s.Left > 0) < MaxSpills)
        {
            var (texture, tinted) = _splats[_random.Next(_splats.Count)];
            // A spill is drawn a quarter of the moproach's height below its position, under its feet.
            var spill = new Spill
            {
                Pos = at - new Vector2(size / 2f, size * 0.75f),
                Texture = texture,
                Color = tinted ? SpillColors[_random.Next(SpillColors.Length)] : Color.White.WithAlpha(0.85f),
            };
            _spills.Add(spill);
            if (!_scrubbing)
                _target = spill;
        }

        _excited = ExcitedSeconds;
        Say(Loc.GetString($"symphony-moproach-excited-{_random.Next(ExcitedLines) + 1}"));
        Squeak();
    }

    private void Say(string line)
    {
        _say = line;
        _sayLeft = SaySeconds;
    }

    private void Squeak()
    {
        if (_soundIn > 0 || _playing.Count >= MaxSounds)
            return;

        _soundIn = SoundCooldown;
        if (_audio.CreateAudioSource(_sounds[_random.Next(_sounds.Count)]) is not { } source)
            return;

        source.Global = true;
        source.Gain = _cfg.GetCVar(CCVars.InterfaceVolume);
        source.Pitch = 0.9f + _random.NextSingle() * 0.3f;
        source.StartPlaying();
        _playing.Add(source);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        foreach (var source in _playing)
            source.Dispose();
        _playing.Clear();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            if (_playing[i].Playing)
                continue;
            _playing[i].Dispose();
            _playing.RemoveAt(i);
        }

        if (!VisibleInTree)
            return;
        // Until the screen has room for it there is nowhere to go; bouncing in a zero-sized box made it twitch.
        if (PixelSize.X < Size * 2 || PixelSize.Y < Size * 2)
            return;
        var dt = MathF.Min(args.DeltaSeconds, MaxFrame);
        _time += dt;
        _excited = MathF.Max(0, _excited - dt);
        _soundIn -= dt;
        _sayLeft -= dt;

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

        _frameTime += dt * (_excited > 0 ? 2f : 1f);
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
        var speed = _excited > 0 ? RushSpeed : Speed * 1.3f;
        var to = spill.Pos - _pos;
        var step = speed * dt * UIScale;
        if (to.Length() > step)
        {
            _vel = to / to.Length() * speed;
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

        // Done: a little break before the next one, and a while before it asks again.
        spill.Wet = WetSeconds;
        _target = null;
        _vel = Heading(_random.NextSingle() * MathF.Tau);
        _turnIn = 1f + _random.NextSingle() * 2f;
        _askIn = MathF.Max(_askIn, 5f);
    }

    private void Wander(float dt)
    {
        // With nothing to mop, it keeps asking for something.
        _askIn -= dt;
        if (_askIn <= 0)
        {
            Say(Loc.GetString($"symphony-moproach-ask-{_random.Next(AskLines) + 1}"));
            _askIn = 8f + _random.NextSingle() * 8f;
        }

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

        // A gentle bob makes it float; scrubbing shuffles it side to side, and excitement makes it hop.
        var bob = _scrubbing
            ? new Vector2(MathF.Sin(_time * MathF.Tau * 2.5f) * 5f * UIScale, 0)
            : _excited > 0
                ? new Vector2(0, -MathF.Abs(MathF.Sin(_time * MathF.Tau * 3f)) * 12f * UIScale)
                : new Vector2(0, MathF.Sin(_time * 3f) * 4f * UIScale);
        var drawn = _pos + bob;
        handle.DrawTextureRect(_state.GetFrame(_facing, _frame), UIBox2.FromDimensions(drawn, new Vector2(size)));

        if (_say != null && _sayLeft > 0)
            DrawBubble(handle, _say, drawn + new Vector2(size / 2f, 0), Math.Clamp(_sayLeft / 0.5f, 0f, 1f));
    }

    /// <summary>A speech bubble centred above a point, kept on the screen.</summary>
    private void DrawBubble(DrawingHandleScreen handle, string text, Vector2 above, float alpha)
    {
        var scale = UIScale;
        var width = handle.GetDimensions(_font, text, scale).X;
        var height = _font.GetLineHeight(scale);
        var pad = 6f * scale;
        var box = UIBox2.FromDimensions(
            new Vector2(above.X - width / 2f - pad, above.Y - height - pad * 2f),
            new Vector2(width + pad * 2f, height + pad * 2f));
        // Slide it back inside the edges rather than cut it off.
        var shift = new Vector2(
            MathF.Max(0, -box.Left) - MathF.Max(0, box.Right - PixelSize.X),
            MathF.Max(0, -box.Top));
        box = box.Translated(shift);

        handle.DrawRect(box, Color.FromHex("#101418").WithAlpha(0.85f * alpha));
        handle.DrawRect(box, Color.FromHex("#9FD8FF").WithAlpha(alpha), filled: false);
        handle.DrawString(_font, box.TopLeft + new Vector2(pad, pad), text, scale, Color.White.WithAlpha(alpha));
    }
}
