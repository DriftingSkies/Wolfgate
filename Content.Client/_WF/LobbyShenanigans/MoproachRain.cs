using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.LobbyShenanigans;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// Moproaches tumble in from the top of the screen and pile up at the bottom. Players can pick one up, drag it about
/// to shove the others and throw it. The oldest fade away once the pile is full.
/// </summary>
public sealed class MoproachRain : LobbyShenanigan, ILobbyShovable, ILobbyPullable
{
    private const float Scale = 2f;
    private const int PerPress = 12;
    private const float SpawnGap = 0.05f;
    private const int MaxBodies = 140;
    private const float FadeSeconds = 0.6f;

    private readonly MoproachPile _pile = new();
    private readonly List<Texture> _looks = new();
    private readonly Dictionary<MoproachPile.Body, float> _fading = new();
    private readonly List<MoproachPile.Body> _swallowed = new();
    private readonly System.Random _random = new();
    private int _queued;
    private float _spawnIn;

    public override string Id => LobbyShenaniganIds.MoproachRain;

    public MoproachRain()
    {
        var rsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>("/Textures/Mobs/Animals/mothroach/moproach.rsi").RSI;
        var state = rsi["mothroach"];
        foreach (var dir in new[] { RsiDirection.South, RsiDirection.East, RsiDirection.West, RsiDirection.North })
            _looks.Add(state.GetFrame(dir, 0));
        // Now and then one lands belly up.
        _looks.Add(rsi["mothroach_dead"].GetFrame(RsiDirection.South, 0));
    }

    public override void Trigger(Vector2 bounds, float scale) => _queued += PerPress;

    public override void Update(float dt, Vector2 bounds, float scale)
    {
        // A press drops them one after another, not all in one clump.
        _spawnIn -= dt;
        while (_queued > 0 && _spawnIn <= 0)
        {
            _queued--;
            _spawnIn += SpawnGap;
            Spawn(bounds, scale);
        }
        if (_queued == 0)
            _spawnIn = MathF.Max(_spawnIn, 0);

        // Over the limit, the oldest start to fade; faded ones leave the pile.
        var over = _pile.Bodies.Count - _fading.Count - MaxBodies;
        for (var i = 0; over > 0 && i < _pile.Bodies.Count; i++)
        {
            if (_pile.Bodies[i] != _pile.Held && _fading.TryAdd(_pile.Bodies[i], FadeSeconds))
                over--;
        }
        foreach (var body in new List<MoproachPile.Body>(_fading.Keys))
        {
            if ((_fading[body] -= dt) > 0)
                continue;
            _fading.Remove(body);
            _pile.Remove(body);
        }

        if (_pile.Bodies.Count > 0)
            _pile.Step(dt, bounds, scale);
    }

    public override void Clear()
    {
        _queued = 0;
        _pile.Release();
        foreach (var body in _pile.Bodies)
            _fading.TryAdd(body, FadeSeconds);
    }

    public override bool HitTest(Vector2 pos) => _pile.At(pos) is { } body && !_fading.ContainsKey(body);

    public override bool TryGrab(Vector2 pos)
    {
        if (_pile.At(pos) is not { } body || _fading.ContainsKey(body))
            return false;
        _pile.Grab(body, pos);
        return true;
    }

    public override void Drag(Vector2 pos, float dt) => _pile.Drag(pos, dt);

    public void Shove(Vector2 at, float radius, Vector2 velocity) => _pile.Shove(at, radius, velocity);

    public int Pull(Vector2 at, float reach, float swallow, float strength, float dt)
    {
        _swallowed.Clear();
        _pile.Pull(at, reach, swallow, strength, dt, _swallowed);
        foreach (var body in _swallowed)
            _fading.Remove(body);
        return _swallowed.Count;
    }

    public override void Release() => _pile.Release();

    private void Spawn(Vector2 bounds, float scale)
    {
        var size = 32 * Scale * scale;
        var radius = size * 0.36f;
        _pile.Bodies.Add(new MoproachPile.Body
        {
            Pos = new Vector2(radius + _random.NextSingle() * MathF.Max(1, bounds.X - radius * 2), -size * (1 + _random.NextSingle())),
            Vel = new Vector2((_random.NextSingle() - 0.5f) * 300f * scale, _random.NextSingle() * 200f * scale),
            Radius = radius,
            Angle = _random.NextSingle() * MathF.Tau,
            Spin = (_random.NextSingle() - 0.5f) * 8f,
            Look = _random.Next(10) == 0 ? _looks.Count - 1 : _random.Next(_looks.Count - 1),
        });
    }

    public override void Draw(DrawingHandleScreen handle, float scale)
    {
        if (_pile.Bodies.Count == 0)
            return;
        var size = 32 * Scale * scale;
        var half = new Vector2(size / 2f);
        var old = handle.GetTransform();
        foreach (var body in _pile.Bodies)
        {
            var alpha = _fading.TryGetValue(body, out var left) ? Math.Clamp(left / FadeSeconds, 0f, 1f) : 1f;
            handle.SetTransform(Matrix3x2.Multiply(Matrix3Helpers.CreateTransform(body.Pos, new Angle(body.Angle)), old));
            handle.DrawTextureRect(_looks[body.Look], new UIBox2(-half, half), Color.White.WithAlpha(alpha));
        }
        handle.SetTransform(old);
    }
}
