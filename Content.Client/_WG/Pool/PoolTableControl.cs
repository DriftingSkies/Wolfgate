using System.Numerics;
using Content.Shared._WG.Pool;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WG.Pool;

/// <summary>
/// The pool table seen from above. Replays each shot with the shared physics, and takes the local player's aim:
/// the mouse points the cue, holding the button and pulling back sets the power, letting go shoots.
/// </summary>
public sealed class PoolTableControl : Control
{
    /// <summary>Width of the wooden rail and height of the power gauge's strip under the table, in table units.</summary>
    private const float Rail = 5f;
    private const float Tray = 5f;

    /// <summary>How long a pocketed ball takes to drop out of sight.</summary>
    private const float SinkTime = 0.22f;

    /// <summary>How far the mouse is pulled back for full power, in table units.</summary>
    private const float PullLength = 35f;

    private const float SoundGap = 0.05f;

    /// <summary>How long the table stays locked for the server's answer to a shot.</summary>
    private const float AnswerWait = 2f;

    private static readonly Color Wood = Color.FromHex("#6B4426");
    private static readonly Color WoodLight = Color.FromHex("#8A5A33");
    private static readonly Color WoodDark = Color.FromHex("#3A2413");
    private static readonly Color Cushion = Color.FromHex("#155A31");
    private static readonly Color CushionEdge = Color.FromHex("#0F4526");
    private static readonly Color Felt = Color.FromHex("#1F7A44");
    private static readonly Color PocketRim = Color.FromHex("#241710");
    private static readonly Color PocketColor = Color.FromHex("#070707");
    private static readonly Color Marking = Color.White.WithAlpha(0.16f);
    private static readonly Color Guide = Color.White.WithAlpha(0.6f);
    private static readonly Color Shadow = Color.Black.WithAlpha(0.3f);
    private static readonly Color Shaft = Color.FromHex("#E2C48C");
    private static readonly Color Butt = Color.FromHex("#4A2A1A");
    private static readonly Color Chalk = Color.FromHex("#488CD6");
    private static readonly Color GaugeBack = Color.FromHex("#141416");
    private static readonly Color GaugeEdge = Color.FromHex("#4A4A52");
    private static readonly Color[] GaugeFill = { Color.FromHex("#3AB85A"), Color.FromHex("#E8C020"), Color.FromHex("#D0302A") };

    private static readonly ResolvedSoundSpecifier ClackSound = new ResolvedPathSpecifier("/Audio/Effects/glass_knock.ogg");
    private static readonly ResolvedSoundSpecifier CushionSound = new ResolvedPathSpecifier("/Audio/Effects/weak_hit1.ogg");
    private static readonly ResolvedSoundSpecifier PocketSound = new ResolvedPathSpecifier("/Audio/Effects/drop.ogg");

    private readonly AudioSystem _audio;
    private readonly IConfigurationManager _cfg;
    private readonly PoolSim _sim = new();
    private readonly Vector2[] _final = new Vector2[PoolRules.BallCount];
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _before = new Vector2[PoolRules.BallCount];

    /// <summary>Two axes of each ball, turned as it rolls: the one through its number spots and one beside it.</summary>
    private readonly Vector3[] _spot = new Vector3[PoolRules.BallCount];
    private readonly Vector3[] _side = new Vector3[PoolRules.BallCount];

    /// <summary>Time since each ball dropped into a pocket, negative when it isn't dropping, and its way down.</summary>
    private readonly float[] _sinking = new float[PoolRules.BallCount];
    private readonly Vector2[] _sinkFrom = new Vector2[PoolRules.BallCount];
    private readonly Vector2[] _sinkTo = new Vector2[PoolRules.BallCount];
    private ushort _finalDown;
    private float _clock;
    private float _sinceSound;
    private float _waiting;

    private Vector2 _mouse = PoolRules.FootSpot;
    private Vector2 _aim = Vector2.UnitX;
    private Vector2 _press;
    private bool _charging;
    private bool _dragging;
    private bool _dragValid;
    private float _power;

    /// <summary>A shot is being replayed.</summary>
    public bool Animating { get; private set; }

    /// <summary>The local player may shoot now.</summary>
    public bool CanShoot;

    /// <summary>The local player may also move the cue ball, behind the head string if it is the break.</summary>
    public bool CanPlace;
    public bool Kitchen;

    /// <summary>A shot or placement was sent and the server hasn't answered yet.</summary>
    private bool Waiting => _waiting > 0f;

    public event Action<float, float>? OnShoot;
    public event Action<Vector2>? OnPlace;
    public event Action? OnSettled;

    public PoolTableControl()
    {
        _audio = IoCManager.Resolve<IEntityManager>().System<AudioSystem>();
        _cfg = IoCManager.Resolve<IConfigurationManager>();
        MinSize = new Vector2((PoolRules.Width + 2f * Rail) * 6f, (PoolRules.Height + 2f * Rail + Tray) * 6f);
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        Rerack();
    }

    /// <summary>Turns every ball spot up, each stripe at its own angle, as a fresh rack has them.</summary>
    public void Rerack()
    {
        for (var ball = 0; ball < PoolRules.BallCount; ball++)
        {
            var angle = ball * 2.4f;
            _spot[ball] = PoolBallArt.FaceUp;
            _side[ball] = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
            _sinking[ball] = -1f;
        }
    }

    /// <summary>Shows a settled table. During a replay it only replaces where the replay will end.</summary>
    public void Show(Vector2[] balls, ushort down)
    {
        Array.Copy(balls, _final, PoolRules.BallCount);
        _finalDown = down;
        _waiting = 0f;
        _dragging = false;
        if (!Animating)
            _sim.Load(_final, _finalDown);
    }

    /// <summary>Replays a shot from the table it started on, then settles on the server's result.</summary>
    public void Replay(Vector2[] start, ushort startDown, Vector2 velocity, Vector2[] balls, ushort down)
    {
        Show(balls, down);
        _sim.Load(start, startDown);
        _sim.Strike(velocity);
        Animating = true;
        _charging = false;
        _clock = 0f;
    }

    private bool Aiming => CanShoot && !Animating && !Waiting && !PoolRules.IsDown(_sim.Down, PoolRules.CueBall);

    #region Input

    private float Scale => MathF.Min(PixelWidth / (PoolRules.Width + 2f * Rail), PixelHeight / (PoolRules.Height + 2f * Rail + Tray));

    /// <summary>Pixel position of the playfield's top left corner.</summary>
    private Vector2 Origin
    {
        get
        {
            var scale = Scale;
            return new Vector2((PixelWidth - PoolRules.Width * scale) * 0.5f, Rail * scale);
        }
    }

    private Vector2 ToTable(Vector2 pixel) => (pixel - Origin) / Scale;

    private Vector2 ToPixel(Vector2 table) => Origin + table * Scale;

    private void Point(Vector2 pixel)
    {
        _mouse = ToTable(pixel);
        if (_charging)
        {
            _power = Math.Clamp(Vector2.Dot(_press - _mouse, _aim) / PullLength, 0f, 1f);
            return;
        }

        if (_dragging)
        {
            var at = Vector2.Clamp(_mouse, new Vector2(PoolRules.BallRadius),
                new Vector2(Kitchen ? PoolRules.HeadSpot.X : PoolRules.Width - PoolRules.BallRadius, PoolRules.Height - PoolRules.BallRadius));
            _dragValid = PoolRules.CanPlace(_sim.Pos, _sim.Down, PoolRules.CueBall, at, Kitchen);
            _sim.Pos[PoolRules.CueBall] = at;
            return;
        }

        var to = _mouse - _sim.Pos[PoolRules.CueBall];
        if (to.LengthSquared() > 0.25f)
            _aim = Vector2.Normalize(to);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        Point(args.RelativePixelPosition);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick && (_charging || _dragging))
        {
            Cancel();
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick || !Aiming)
            return;

        Point(args.RelativePixelPosition);
        args.Handle();
        if (CanPlace && Vector2.Distance(_mouse, _sim.Pos[PoolRules.CueBall]) < PoolRules.BallRadius * 1.5f)
        {
            _dragging = true;
            _dragValid = true;
            return;
        }

        _charging = true;
        _press = _mouse;
        _power = 0f;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (_dragging)
        {
            if (_dragValid)
            {
                _dragging = false;
                _waiting = AnswerWait;
                OnPlace?.Invoke(_sim.Pos[PoolRules.CueBall]);
            }
            else
            {
                Cancel();
            }

            return;
        }

        if (!_charging)
            return;

        _charging = false;
        if (_power < PoolRules.MinPower || !Aiming)
            return;

        _waiting = AnswerWait;
        OnShoot?.Invoke(MathF.Atan2(_aim.Y, _aim.X), _power);
    }

    /// <summary>Drops a shot being charged, and puts a dragged cue ball back.</summary>
    private void Cancel()
    {
        _charging = false;
        if (!_dragging)
            return;

        _dragging = false;
        _sim.Pos[PoolRules.CueBall] = _final[PoolRules.CueBall];
    }

    #endregion

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _sinceSound += args.DeltaSeconds;
        _waiting -= args.DeltaSeconds;

        for (var ball = 0; ball < PoolRules.BallCount; ball++)
        {
            if (_sinking[ball] < 0f)
                continue;

            _sinking[ball] += args.DeltaSeconds;
            if (_sinking[ball] > SinkTime)
                _sinking[ball] = -1f;
        }

        if (!Animating)
        {
            if (!Aiming)
                Cancel();
            return;
        }

        // A long frame catches up over the next few rather than freezing on hundreds of steps.
        _clock = MathF.Min(_clock + args.DeltaSeconds, 0.1f);
        while (_clock >= PoolRules.Step && _sim.Moving && _sim.Time < PoolRules.MaxShotSeconds)
        {
            _clock -= PoolRules.Step;
            Array.Copy(_sim.Pos, _before, PoolRules.BallCount);
            _sim.Step();

            for (var ball = 0; ball < PoolRules.BallCount; ball++)
            {
                var moved = _sim.Pos[ball] - _before[ball];
                if (moved == Vector2.Zero)
                    continue;

                _spot[ball] = Vector3.Normalize(PoolBallArt.Roll(_spot[ball], moved));
                var side = PoolBallArt.Roll(_side[ball], moved);
                _side[ball] = Vector3.Normalize(side - _spot[ball] * Vector3.Dot(side, _spot[ball]));
            }

            for (var i = _sim.Sunk.Count - _sim.StepSunk; i < _sim.Sunk.Count; i++)
            {
                var ball = _sim.Sunk[i];
                _sinking[ball] = 0f;
                _sinkFrom[ball] = _sim.Pos[ball];
                _sinkTo[ball] = NearestPocket(_sim.Pos[ball]);
            }

            if (_sim.StepSunk > 0)
                Play(PocketSound, -4f);
            else if (_sim.StepBallImpact > 4f)
                Play(ClackSound, -14f + 12f * MathF.Min(1f, _sim.StepBallImpact / 120f));
            else if (_sim.StepCushionImpact > 12f)
                Play(CushionSound, -18f + 8f * MathF.Min(1f, _sim.StepCushionImpact / 150f));
        }

        if (_sim.Moving && _sim.Time < PoolRules.MaxShotSeconds)
            return;

        Animating = false;
        _sim.Load(_final, _finalDown);
        OnSettled?.Invoke();
    }

    private static Vector2 NearestPocket(Vector2 at)
    {
        var nearest = PoolRules.Pockets[0];
        foreach (var pocket in PoolRules.Pockets)
        {
            if (Vector2.DistanceSquared(at, pocket) < Vector2.DistanceSquared(at, nearest))
                nearest = pocket;
        }

        return nearest;
    }

    private void Play(ResolvedSoundSpecifier sound, float volume)
    {
        var gain = _cfg.GetCVar(CCVars.InterfaceVolume);
        if (gain <= 0f || _sinceSound < SoundGap)
            return;

        _sinceSound = 0f;
        _audio.PlayGlobal(sound, Filter.Local(), false, AudioParams.Default.WithVolume(volume + SharedAudioSystem.GainToVolume(gain)));
    }

    #region Drawing

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var scale = Scale;
        var topLeft = ToPixel(Vector2.Zero);
        var bottomRight = ToPixel(new Vector2(PoolRules.Width, PoolRules.Height));
        var rail = new Vector2(Rail * scale);

        // The rail: a dark edge, a lit bevel along the top and left, and the sights set into the wood.
        handle.DrawRect(new UIBox2(topLeft - rail, bottomRight + rail), WoodDark);
        handle.DrawRect(new UIBox2(topLeft - rail * 0.88f, bottomRight + rail * 0.88f), WoodLight);
        handle.DrawRect(new UIBox2(topLeft - rail * 0.76f, bottomRight + rail * 0.88f), Wood);
        for (var i = 1; i < 8; i++)
        {
            if (i == 4)
                continue;

            var x = PoolRules.Width * i / 8f;
            handle.DrawCircle(ToPixel(new Vector2(x, -Rail * 0.55f)), 0.4f * scale, PoolBallArt.Ivory);
            handle.DrawCircle(ToPixel(new Vector2(x, PoolRules.Height + Rail * 0.55f)), 0.4f * scale, PoolBallArt.Ivory);
        }

        for (var i = 1; i < 4; i++)
        {
            var y = PoolRules.Height * i / 4f;
            handle.DrawCircle(ToPixel(new Vector2(-Rail * 0.55f, y)), 0.4f * scale, PoolBallArt.Ivory);
            handle.DrawCircle(ToPixel(new Vector2(PoolRules.Width + Rail * 0.55f, y)), 0.4f * scale, PoolBallArt.Ivory);
        }

        handle.DrawRect(new UIBox2(topLeft - new Vector2(1.4f * scale), bottomRight + new Vector2(1.4f * scale)), CushionEdge);
        handle.DrawRect(new UIBox2(topLeft - new Vector2(scale), bottomRight + new Vector2(scale)), Cushion);
        handle.DrawRect(new UIBox2(topLeft, bottomRight), Felt);

        handle.DrawLine(ToPixel(new Vector2(PoolRules.HeadSpot.X, 0f)), ToPixel(new Vector2(PoolRules.HeadSpot.X, PoolRules.Height)), Marking);
        handle.DrawCircle(ToPixel(PoolRules.HeadSpot), 0.4f * scale, Marking);
        handle.DrawCircle(ToPixel(PoolRules.FootSpot), 0.4f * scale, Marking);

        for (var pocket = 0; pocket < PoolRules.Pockets.Length; pocket++)
        {
            var at = ToPixel(PoolRules.Pockets[pocket]);
            handle.DrawCircle(at, PoolRules.PocketRadius(pocket) * 1.08f * scale, PocketRim);
            handle.DrawCircle(at, PoolRules.PocketRadius(pocket) * 0.88f * scale, PocketColor);
        }

        var radius = PoolRules.BallRadius * scale;

        // Balls on their way down a pocket shrink into it and darken.
        for (var ball = 0; ball < PoolRules.BallCount; ball++)
        {
            if (_sinking[ball] < 0f)
                continue;

            var drop = _sinking[ball] / SinkTime;
            PoolBallArt.Draw(handle, ball, ToPixel(Vector2.Lerp(_sinkFrom[ball], _sinkTo[ball], drop)), radius * (1f - 0.45f * drop),
                _spot[ball], _side[ball], 1f - 0.7f * drop);
        }

        var shadow = new Vector2(radius * 0.22f, radius * 0.3f);
        for (var ball = 0; ball < PoolRules.BallCount; ball++)
        {
            if (!PoolRules.IsDown(_sim.Down, ball))
                handle.DrawCircle(ToPixel(_sim.Pos[ball]) + shadow, radius, Shadow);
        }

        for (var ball = PoolRules.BallCount - 1; ball >= 0; ball--)
        {
            if (PoolRules.IsDown(_sim.Down, ball))
                continue;

            var shade = ball == PoolRules.CueBall && _dragging && !_dragValid ? 0.45f : 1f;
            PoolBallArt.Draw(handle, ball, ToPixel(_sim.Pos[ball]), radius, _spot[ball], _side[ball], shade);
        }

        if (Aiming && !_dragging)
            DrawAim(handle, scale);

        DrawGauge(handle, scale, bottomRight.Y + rail.Y);
    }

    private void DrawAim(DrawingHandleScreen handle, float scale)
    {
        var cue = _sim.Pos[PoolRules.CueBall];
        var ghost = PoolSim.Trace(_sim.Pos, _sim.Down, _aim, out var hit);
        handle.DrawLine(ToPixel(cue + _aim * PoolRules.BallRadius), ToPixel(ghost), Guide);
        handle.DrawCircle(ToPixel(ghost), PoolRules.BallRadius * scale, Guide, false);

        if (hit >= 0)
        {
            // Where the struck ball heads: along the line between the two centres.
            var away = Vector2.Normalize(_sim.Pos[hit] - ghost);
            handle.DrawLine(ToPixel(_sim.Pos[hit] + away * PoolRules.BallRadius), ToPixel(_sim.Pos[hit] + away * 10f), Guide);
        }

        var side = new Vector2(-_aim.Y, _aim.X);
        var tip = cue - _aim * (PoolRules.BallRadius + 1f + (_charging ? _power * 14f : 0f));
        var ferrule = tip - _aim * 0.5f;
        var shaft = ferrule - _aim * 1.1f;
        var joint = shaft - _aim * 28f;
        var end = joint - _aim * 16f;
        DrawQuad(handle, tip, ferrule, side, 0.32f, 0.33f, Chalk);
        DrawQuad(handle, ferrule, shaft, side, 0.33f, 0.36f, PoolBallArt.Ivory);
        DrawQuad(handle, shaft, joint, side, 0.36f, 0.58f, Shaft);
        DrawQuad(handle, joint, end, side, 0.58f, 0.78f, Butt);
    }

    /// <summary>A tapered strip between two table points, with a half-width at each end.</summary>
    private void DrawQuad(DrawingHandleScreen handle, Vector2 from, Vector2 to, Vector2 side, float fromHalf, float toHalf, Color color)
    {
        _quad[0] = ToPixel(from + side * fromHalf);
        _quad[1] = ToPixel(to + side * toHalf);
        _quad[2] = ToPixel(to - side * toHalf);
        _quad[3] = ToPixel(from - side * fromHalf);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _quad, color);
    }

    /// <summary>The power gauge under the table: twenty blocks that fill green to red as the shot is pulled back.</summary>
    private void DrawGauge(DrawingHandleScreen handle, float scale, float top)
    {
        const int blocks = 20;
        var width = 60f * scale;
        var left = (PixelWidth - width) * 0.5f;
        var middle = top + Tray * scale * 0.5f;
        var bar = new UIBox2(left, middle - 0.9f * scale, left + width, middle + 0.9f * scale);
        handle.DrawRect(bar, GaugeBack);

        var block = width / blocks;
        var lit = _charging ? (int) MathF.Ceiling(_power * blocks) : 0;
        for (var i = 0; i < lit; i++)
        {
            var at = bar.Left + i * block;
            handle.DrawRect(new UIBox2(at + 1f, bar.Top + 1f, at + block - 1f, bar.Bottom - 1f), GaugeFill[i * GaugeFill.Length / blocks]);
        }

        handle.DrawRect(bar, GaugeEdge, false);
    }

    #endregion
}
