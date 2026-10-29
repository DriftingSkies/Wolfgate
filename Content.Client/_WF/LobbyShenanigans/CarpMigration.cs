using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.LobbyShenanigans;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// A school of rainbow space carp swims across the lobby. Click one and it bites and bolts; any moproaches in the way
/// get barged aside, and a singularity can drag them off course and eat them.
/// </summary>
public sealed class CarpMigration : LobbyShenanigan, ILobbyPullable
{
    private const float Scale = 2f;
    private const int PerPress = 14;
    private const float SpawnGap = 0.12f;
    private const float BoltSpeed = 4f;
    private const int MaxCarp = 80;
    private static readonly ResolvedSoundSpecifier Bite = new ResolvedPathSpecifier("/Audio/Effects/bite.ogg");

    private sealed class Carp
    {
        public Vector2 Pos;
        public float BaseY;
        public float Speed;
        public float Phase;
        public Color Tint;
        public bool Bolting;
        public float Pop;
    }

    private readonly IRsiStateLike _body;
    private readonly IRsiStateLike _mouth;
    private readonly List<Carp> _carp = new();
    private readonly System.Random _random = new();
    private int _queued;
    private float _spawnIn;
    private float _time;
    private float _frameTime;
    private int _frame;
    // The school all comes from one side, and a lane somewhere up or down the screen.
    private int _dir = 1;
    private float _lane;
    private float _scale = 1f;

    public override string Id => LobbyShenaniganIds.CarpMigration;

    public CarpMigration()
    {
        var rsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>("/Textures/Mobs/Aliens/Carps/space.rsi").RSI;
        _body = rsi["alive"];
        _mouth = rsi["mouth"];
    }

    private static float Size(float scale) => 32 * Scale * scale;

    public override void Trigger(Vector2 bounds, float scale)
    {
        if (_queued == 0)
        {
            _dir = _random.Next(2) == 0 ? 1 : -1;
            _lane = bounds.Y * (0.2f + 0.6f * _random.NextSingle());
        }
        _queued += PerPress;
    }

    public override void Update(float dt, Vector2 bounds, float scale)
    {
        _time += dt;
        _scale = scale;
        var size = Size(scale);

        _spawnIn -= dt;
        while (_queued > 0 && _spawnIn <= 0)
        {
            _queued--;
            _spawnIn += SpawnGap * (0.5f + _random.NextSingle());
            if (_carp.Count < MaxCarp)
                Spawn(bounds, scale);
        }
        if (_queued == 0)
            _spawnIn = MathF.Max(_spawnIn, 0);

        var shovable = new List<ILobbyShovable>();
        foreach (var other in All)
        {
            if (other is ILobbyShovable s)
                shovable.Add(s);
        }

        foreach (var carp in _carp)
        {
            var speed = carp.Speed * (carp.Bolting ? BoltSpeed : 1f);
            var vel = new Vector2(_dir * speed * scale, 0);
            carp.Pos.X += vel.X * dt;
            // A lazy up-and-down as it swims; a bolting one goes straight.
            var wobble = carp.Bolting ? 0 : MathF.Sin(_time * 2.2f + carp.Phase) * 22f * scale;
            carp.Pos.Y = carp.BaseY + wobble;
            carp.Pop = MathF.Max(0, carp.Pop - dt * 3f);
            foreach (var s in shovable)
                s.Shove(carp.Pos, size * 0.32f, vel);
        }
        _carp.RemoveAll(c => _dir > 0 ? c.Pos.X > bounds.X + size : c.Pos.X < -size);

        _frameTime += dt;
        var delay = _body.GetDelay(_frame);
        if (_body.IsAnimated && _frameTime >= delay)
        {
            _frameTime -= delay;
            _frame = (_frame + 1) % _body.AnimationFrameCount;
        }
    }

    private void Spawn(Vector2 bounds, float scale)
    {
        var size = Size(scale);
        var spread = (_random.NextSingle() - 0.5f) * 2f * 140f * scale;
        _carp.Add(new Carp
        {
            Pos = new Vector2(_dir > 0 ? -size : bounds.X + size, 0),
            BaseY = Math.Clamp(_lane + spread, size, MathF.Max(size, bounds.Y - size)),
            Speed = 150f + _random.NextSingle() * 90f,
            Phase = _random.NextSingle() * MathF.Tau,
            // The same rainbow the game rolls for a space carp.
            Tint = Color.FromHsv(new Vector4(_random.NextSingle(), 0.55f, 1f, 1f)),
        });
    }

    /// <summary>The carp under <paramref name="pos"/>, by its body: the sprite is long and flat inside its square.</summary>
    private Carp? At(Vector2 pos)
    {
        var half = new Vector2(Size(_scale) * 0.4f, Size(_scale) * 0.22f);
        for (var i = _carp.Count - 1; i >= 0; i--)
        {
            var carp = _carp[i];
            var d = Vector2.Abs(carp.Pos - pos);
            if (!carp.Bolting && d.X < half.X && d.Y < half.Y)
                return carp;
        }
        return null;
    }

    public override bool HitTest(Vector2 pos) => At(pos) != null;

    /// <summary>A click bites back: the carp chomps and bolts. Nothing is held, so <see cref="Release"/> has nothing to do.</summary>
    public override bool TryGrab(Vector2 pos)
    {
        if (At(pos) is not { } carp)
            return false;
        carp.Bolting = true;
        carp.Pop = 1f;
        PlaySound(Bite, -4f);
        return true;
    }

    public int Pull(Vector2 at, float reach, float swallow, float strength, float dt)
    {
        var before = _carp.Count;
        foreach (var carp in _carp)
        {
            var d = at - carp.Pos;
            var dist = d.Length();
            if (dist >= reach || dist < 0.001f)
                continue;
            // Dragged sideways off its lane; the swim still carries it along, so it curls in.
            var step = d / dist * strength * 0.12f * (1f - dist / reach) * dt;
            carp.Pos.X += step.X;
            carp.BaseY += step.Y;
        }
        _carp.RemoveAll(c => (c.Pos - at).Length() < swallow);
        return before - _carp.Count;
    }

    /// <summary>Clearing sends the whole school bolting off the way it was going.</summary>
    public override void Clear()
    {
        _queued = 0;
        foreach (var carp in _carp)
            carp.Bolting = true;
    }

    public override void Draw(DrawingHandleScreen handle, float scale)
    {
        if (_carp.Count == 0)
            return;
        var size = Size(scale);
        var dir = _dir > 0 ? RsiDirection.East : RsiDirection.West;
        var body = _body.GetFrame(dir, _frame);
        var mouth = _mouth.GetFrame(dir, _frame % _mouth.AnimationFrameCount);
        foreach (var carp in _carp)
        {
            // A bitten one swells for a moment as it snaps.
            var grown = size * (1f + 0.3f * carp.Pop);
            var box = UIBox2.FromDimensions(carp.Pos - new Vector2(grown / 2f), new Vector2(grown));
            handle.DrawTextureRect(body, box, carp.Tint);
            handle.DrawTextureRect(mouth, box);
        }
    }
}
