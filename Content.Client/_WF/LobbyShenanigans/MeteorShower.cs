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
/// A volley of meteors streaks down across the lobby and blows up at the bottom, flinging any moproaches near the blast.
/// Click one to shoot it down early; a singularity can drag them in and eat them.
/// </summary>
public sealed class MeteorShower : LobbyShenanigan, ILobbyPullable
{
    private const float Scale = 2f;
    private const int PerPress = 10;
    private const float SpawnGap = 0.3f;
    private const int MaxMeteors = 60;
    private const float BlastScale = 1.6f;
    private const float BlastReach = 110f;
    private const float SoundGap = 0.08f;
    private static readonly string[] Sizes = ["small", "medium", "big"];
    private static readonly ResolvedSoundSpecifier[] Booms =
    [
        new ResolvedPathSpecifier("/Audio/Effects/explosion_small1.ogg"),
        new ResolvedPathSpecifier("/Audio/Effects/explosion_small2.ogg"),
        new ResolvedPathSpecifier("/Audio/Effects/explosion_small3.ogg"),
    ];
    private static readonly Color Trail = Color.FromHex("#ffb347");

    private sealed class Meteor
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Angle;
        public float Spin;
        public int Look;
        public float Frame;
    }

    private sealed class Blast
    {
        public Vector2 Pos;
        public float Age;
        public float Size;
    }

    private readonly List<IRsiStateLike> _looks = new();
    private readonly IRsiStateLike _blast;
    private readonly List<Meteor> _meteors = new();
    private readonly List<Blast> _blasts = new();
    private readonly System.Random _random = new();
    private int _queued;
    private float _spawnIn;
    private float _soundIn;
    // A volley all slants the same way, as if from one direction out in space.
    private float _slant;
    private float _scale = 1f;

    public override string Id => LobbyShenaniganIds.MeteorShower;

    public MeteorShower()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var rsi = cache.GetResource<RSIResource>("/Textures/Objects/Misc/meteor.rsi").RSI;
        foreach (var size in Sizes)
            _looks.Add(rsi[size]);
        _blast = cache.GetResource<RSIResource>("/Textures/Effects/explosion.rsi").RSI["explosionfast"];
    }

    private static float Size(float scale) => 32 * Scale * scale;

    private float BlastLife => _blast.AnimationFrameCount * _blast.GetDelay(0);

    public override void Trigger(Vector2 bounds, float scale)
    {
        if (_queued == 0)
            _slant = (_random.Next(2) == 0 ? 1 : -1) * (0.25f + 0.35f * _random.NextSingle());
        _queued += PerPress;
    }

    public override void Update(float dt, Vector2 bounds, float scale)
    {
        _scale = scale;
        var size = Size(scale);
        _soundIn = MathF.Max(0, _soundIn - dt);

        _spawnIn -= dt;
        while (_queued > 0 && _spawnIn <= 0)
        {
            _queued--;
            _spawnIn += SpawnGap * (0.4f + _random.NextSingle());
            if (_meteors.Count < MaxMeteors)
                Spawn(bounds, scale);
        }
        if (_queued == 0)
            _spawnIn = MathF.Max(_spawnIn, 0);

        var ground = bounds.Y - size * 0.3f;
        for (var i = _meteors.Count - 1; i >= 0; i--)
        {
            var m = _meteors[i];
            m.Pos += m.Vel * dt;
            m.Angle += m.Spin * dt;
            m.Frame += dt;
            if (m.Pos.Y < ground)
                continue;
            _meteors.RemoveAt(i);
            Explode(new Vector2(m.Pos.X, ground), 1f, scale);
        }
        // Gone off the side on a steep slant: no blast, nobody saw it land.
        _meteors.RemoveAll(m => m.Pos.X < -size * 2 || m.Pos.X > bounds.X + size * 2);

        foreach (var b in _blasts)
            b.Age += dt;
        _blasts.RemoveAll(b => b.Age >= BlastLife);
    }

    private void Spawn(Vector2 bounds, float scale)
    {
        var size = Size(scale);
        var speed = (420f + _random.NextSingle() * 260f) * scale;
        var dir = Vector2.Normalize(new Vector2(_slant, 1f));
        // Started far enough upwind that a slanted one still crosses the middle of the screen.
        var x = _random.NextSingle() * bounds.X - _slant * bounds.Y * 0.5f;
        _meteors.Add(new Meteor
        {
            Pos = new Vector2(x, -size),
            Vel = dir * speed,
            Angle = _random.NextSingle() * MathF.Tau,
            Spin = (_random.NextSingle() - 0.5f) * 6f,
            Look = _random.Next(10) < 6 ? 1 : _random.Next(2) == 0 ? 0 : 2,
        });
    }

    /// <summary>A blast where it landed or was shot down, throwing anything near it out and up.</summary>
    private void Explode(Vector2 at, float strength, float scale)
    {
        _blasts.Add(new Blast { Pos = at, Size = 96 * BlastScale * strength * scale });
        foreach (var other in All)
        {
            if (other is ILobbyShovable s)
                s.Shove(at, BlastReach * strength * scale, new Vector2(0, -420f * strength * scale));
        }
        // A whole volley landing in one frame is one boom, not ten stacked into a roar.
        if (_soundIn > 0)
            return;
        _soundIn = SoundGap;
        PlaySound(Booms[_random.Next(Booms.Length)], -7f);
    }

    private Meteor? At(Vector2 pos)
    {
        var reach = Size(_scale) * 0.4f;
        for (var i = _meteors.Count - 1; i >= 0; i--)
        {
            if ((_meteors[i].Pos - pos).Length() < reach)
                return _meteors[i];
        }
        return null;
    }

    public override bool HitTest(Vector2 pos) => At(pos) != null;

    /// <summary>A click shoots it down: a smaller blast in the air, which still knocks aside anything close.</summary>
    public override bool TryGrab(Vector2 pos)
    {
        if (At(pos) is not { } m)
            return false;
        _meteors.Remove(m);
        Explode(m.Pos, 0.6f, _scale);
        return true;
    }

    public int Pull(Vector2 at, float reach, float swallow, float strength, float dt)
    {
        var before = _meteors.Count;
        foreach (var m in _meteors)
        {
            var d = at - m.Pos;
            var dist = d.Length();
            if (dist >= reach || dist < 0.001f)
                continue;
            // Bent off its line rather than stopped, so it curves in.
            m.Vel += d / dist * strength * (1f - dist / reach) * dt;
        }
        _meteors.RemoveAll(m => (m.Pos - at).Length() < swallow);
        return before - _meteors.Count;
    }

    /// <summary>Clearing burns them all up where they are; a blast already going plays out.</summary>
    public override void Clear()
    {
        _queued = 0;
        _meteors.Clear();
    }

    public override void Draw(DrawingHandleScreen handle, float scale)
    {
        if (_meteors.Count == 0 && _blasts.Count == 0)
            return;
        var size = Size(scale);
        var half = new Vector2(size / 2f);
        // The trail first, so the rock sits on its own glow.
        foreach (var m in _meteors)
        {
            var back = -Vector2.Normalize(m.Vel) * size;
            for (var i = 0; i < 3; i++)
                handle.DrawLine(m.Pos + back * (i * 0.6f), m.Pos + back * ((i + 1) * 0.6f), Trail.WithAlpha(0.55f - i * 0.17f));
        }
        var old = handle.GetTransform();
        foreach (var m in _meteors)
        {
            var look = _looks[m.Look];
            var frame = look.GetFrame(RsiDirection.South, (int) (m.Frame / look.GetDelay(0)) % look.AnimationFrameCount);
            handle.SetTransform(Matrix3x2.Multiply(Matrix3Helpers.CreateTransform(m.Pos, new Angle(m.Angle)), old));
            handle.DrawTextureRect(frame, new UIBox2(-half, half));
        }
        handle.SetTransform(old);
        foreach (var b in _blasts)
        {
            var frame = _blast.GetFrame(RsiDirection.South, Math.Min((int) (b.Age / _blast.GetDelay(0)), _blast.AnimationFrameCount - 1));
            handle.DrawTextureRect(frame, UIBox2.FromDimensions(b.Pos - new Vector2(b.Size / 2f), new Vector2(b.Size)));
        }
    }
}
