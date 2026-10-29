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
/// A singularity forms in the lobby, wanders about and grows, pulling anything another shenanigan shows into a spiral
/// and swallowing it; the more it eats the faster it grows. It collapses after a while, or when cleared. A click
/// pokes it away from the cursor. Setting it off again while one is loose feeds it a level.
/// </summary>
public sealed class Singularity : LobbyShenanigan
{
    private const int FirstLevel = 2;
    private const int LastLevel = 5;
    private const float Drift = 45f;
    private const float LifeSeconds = 32f;
    private const float CollapseSeconds = 0.9f;
    private const float GrowSeconds = 7f;
    private const int MealsPerLevel = 6;
    private static readonly ResolvedSoundSpecifier Form = new ResolvedPathSpecifier("/Audio/Effects/singularity_form.ogg");
    private static readonly ResolvedSoundSpecifier Collapse = new ResolvedPathSpecifier("/Audio/Effects/singularity_collapse.ogg");

    private readonly IRsiStateLike[] _levels = new IRsiStateLike[LastLevel + 1];
    private readonly System.Random _random = new();
    private bool _alive;
    private int _level;
    private float _shown;
    private Vector2 _pos;
    private Vector2 _vel;
    private Vector2 _shove;
    private float _age;
    private float _growIn;
    private float _turnIn;
    private int _meals;
    private float _collapsing;
    private float _frameTime;
    private int _frame;
    private float _scale = 1f;

    public override string Id => LobbyShenaniganIds.Singularity;

    /// <summary>Under everything else, so what it pulls in is seen going in.</summary>
    public override int DrawOrder => -1;

    public Singularity()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        for (var level = 1; level <= LastLevel; level++)
            _levels[level] = cache.GetResource<RSIResource>($"/Textures/Structures/Power/Generation/Singularity/singularity_{level}.rsi").RSI[$"singularity_{level}"];
    }

    /// <summary>How wide it is drawn at a level, as the game draws it: 32 pixels and 64 more a level.</summary>
    private float Width(float level) => (32f + 64f * (level - 1)) * _scale;

    private float Radius => Width(_shown) / 2f;

    public override void Trigger(Vector2 bounds, float scale)
    {
        _scale = scale;
        if (_alive && _collapsing <= 0)
        {
            Grow();
            return;
        }
        _alive = true;
        _level = FirstLevel;
        _shown = 1;
        _age = 0;
        _meals = 0;
        _collapsing = 0;
        _growIn = GrowSeconds;
        _pos = new Vector2(bounds.X * (0.25f + 0.5f * _random.NextSingle()), bounds.Y * (0.25f + 0.5f * _random.NextSingle()));
        _vel = Heading();
        _shove = Vector2.Zero;
        PlaySound(Form, -6f);
    }

    private Vector2 Heading()
    {
        var angle = _random.NextSingle() * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Drift;
    }

    private void Grow()
    {
        _level = Math.Min(LastLevel, _level + 1);
        _growIn = GrowSeconds;
    }

    public override void Update(float dt, Vector2 bounds, float scale)
    {
        if (!_alive)
            return;
        _scale = scale;
        _age += dt;

        if (_collapsing > 0)
        {
            _collapsing -= dt;
            _shown = MathF.Max(0.2f, _shown - dt * _shown * 2.5f);
            if (_collapsing <= 0)
                _alive = false;
            return;
        }
        if (_age >= LifeSeconds)
        {
            StartCollapse();
            return;
        }

        // Grows on its own now and then, and sooner for every few things it eats.
        _growIn -= dt;
        if (_growIn <= 0)
            Grow();
        _shown += (_level - _shown) * MathF.Min(1f, dt * 2f);

        _turnIn -= dt;
        if (_turnIn <= 0)
        {
            _vel = Heading();
            _turnIn = 2f + _random.NextSingle() * 3f;
        }
        _shove *= MathF.Max(0f, 1f - dt * 2.5f);
        _pos += (_vel + _shove) * dt * scale;
        var r = Radius * 0.6f;
        if ((_pos.X < r && _vel.X < 0) || (_pos.X > bounds.X - r && _vel.X > 0))
            _vel.X = -_vel.X;
        if ((_pos.Y < r && _vel.Y < 0) || (_pos.Y > bounds.Y - r && _vel.Y > 0))
            _vel.Y = -_vel.Y;
        _pos = Vector2.Clamp(_pos, new Vector2(r), Vector2.Max(new Vector2(r), bounds - new Vector2(r)));

        // Pulls from three times its size, swallows what reaches its middle.
        var reach = Radius * 3f + 120f * scale;
        var swallow = Radius * 0.45f;
        var strength = (2600f + 900f * _shown) * scale;
        foreach (var other in All)
        {
            if (other is not ILobbyPullable pullable)
                continue;
            _meals += pullable.Pull(_pos, reach, swallow, strength, dt);
        }
        while (_meals >= MealsPerLevel)
        {
            _meals -= MealsPerLevel;
            Grow();
        }

        _frameTime += dt;
        var state = _levels[Math.Clamp((int) MathF.Round(_shown), 1, LastLevel)];
        var delay = state.GetDelay(_frame % state.AnimationFrameCount);
        if (_frameTime >= delay)
        {
            _frameTime -= delay;
            _frame = (_frame + 1) % state.AnimationFrameCount;
        }
    }

    private void StartCollapse()
    {
        if (!_alive || _collapsing > 0)
            return;
        _collapsing = CollapseSeconds;
        PlaySound(Collapse, -6f);
    }

    public override void Clear() => StartCollapse();

    public override bool HitTest(Vector2 pos) => _alive && _collapsing <= 0 && (pos - _pos).Length() < Radius * 0.6f;

    /// <summary>A click pokes it: it lurches away from the cursor. Nothing is held.</summary>
    public override bool TryGrab(Vector2 pos)
    {
        if (!HitTest(pos))
            return false;
        var away = _pos - pos;
        var dir = away.Length() > 0.001f ? away / away.Length() : new Vector2(0, -1);
        _shove = dir * 450f;
        _vel = dir * Drift;
        return true;
    }

    public override void Draw(DrawingHandleScreen handle, float scale)
    {
        if (!_alive)
            return;
        var level = Math.Clamp((int) MathF.Round(_shown), 1, LastLevel);
        var state = _levels[level];
        var width = Width(_shown);
        var texture = state.GetFrame(RsiDirection.South, _frame % state.AnimationFrameCount);
        handle.DrawTextureRect(texture, UIBox2.FromDimensions(_pos - new Vector2(width / 2f), new Vector2(width)));
    }
}
