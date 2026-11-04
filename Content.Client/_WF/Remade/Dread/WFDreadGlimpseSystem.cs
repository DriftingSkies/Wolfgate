using System.Numerics;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Remade.Dread;

/// <summary>
/// Shows the player a Remade for a fraction of a second, in a dark spot near the edge of their view and nowhere else. It
/// exists on this client only. Reduced motion and the dread visuals setting turn it off.
/// </summary>
public sealed partial class WFDreadGlimpseSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private const string Prototype = "WFRemadeGlimpse";
    private const float LightSearch = 14f;
    private const float Fade = 0.06f;
    private const int Attempts = 16;

    /// <summary>How long a glimpse with nowhere dark to stand waits for one, trying again as the player moves.</summary>
    private static readonly TimeSpan PendingFor = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(0.25);

    /// <summary>Linear brightness of the map's ambient light above which a spot isn't dark.</summary>
    private const float DaylightLimit = 0.08f;

    private static readonly Color Tint = new(0.62f, 0.52f, 0.52f);

    private readonly List<(EntityUid Uid, TimeSpan Start, TimeSpan End)> _live = new();
    private readonly HashSet<Entity<PointLightComponent>> _lights = new();
    private WFDreadGlimpseEvent? _pending;
    private TimeSpan _pendingUntil;
    private TimeSpan _nextTry;

    /// <summary>How many glimpses this client has shown. Tests read it.</summary>
    public int Shown { get; private set; }

    /// <summary>How many are on screen now. Tests read it.</summary>
    public int Showing => _live.Count;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFDreadGlimpseEvent>(OnGlimpse);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Clear();
    }

    private void Clear()
    {
        foreach (var (uid, _, _) in _live)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        _live.Clear();
        _pending = null;
    }

    private bool Allowed()
    {
        return _config.GetCVar(WFDreadCVars.Visuals) > 0f && !_config.GetCVar(CCVars.ReducedMotion);
    }

    private void OnGlimpse(WFDreadGlimpseEvent ev)
    {
        if (!Allowed() || _live.Count > 0 || ev.Sprite < 0 || ev.Sprite >= WFDreadGlimpses.All.Length)
            return;

        if (!TryShow(ev))
        {
            _pending = ev;
            _pendingUntil = _timing.CurTime + PendingFor;
            _nextTry = _timing.CurTime + RetryGap;
        }
    }

    /// <summary>Shows a glimpse if there is a dark spot for it now.</summary>
    private bool TryShow(WFDreadGlimpseEvent ev)
    {
        if (_player.LocalEntity is not { } me || PickSpot(me) is not { } spot)
            return false;

        var glimpse = WFDreadGlimpses.All[ev.Sprite];
        var uid = Spawn(Prototype, spot);
        var sprite = Comp<SpriteComponent>(uid);
        sprite.LayerSetRSI(0, new ResPath(glimpse.Rsi));
        sprite.LayerSetState(0, WFDreadGlimpses.State);
        sprite.Offset = new Vector2(0f, WFDreadGlimpses.Offset(glimpse.Height));
        sprite.Color = Tint.WithAlpha(0f);

        // Faces the player, so it seems to be looking.
        var toward = _transform.GetWorldPosition(me) - _transform.GetWorldPosition(uid);
        if (toward.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(uid, toward.ToWorldAngle());

        var now = _timing.CurTime;
        _live.Add((uid, now, now + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 0.1f, 1f))));
        Shown++;
        _pending = null;
        return true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        if (_pending is { } pending && now >= _nextTry)
        {
            _nextTry = now + RetryGap;
            if (now > _pendingUntil || !Allowed())
                _pending = null;
            else
                TryShow(pending);
        }

        if (_live.Count == 0)
            return;

        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var (uid, start, end) = _live[i];
            if (now >= end || TerminatingOrDeleted(uid) || !Allowed())
            {
                if (!TerminatingOrDeleted(uid))
                    Del(uid);

                _live.RemoveAt(i);
                continue;
            }

            // Pops in, holds, and is gone before it can be looked at.
            var alpha = Math.Min((float) (now - start).TotalSeconds, (float) (end - now).TotalSeconds) / Fade;
            Comp<SpriteComponent>(uid).Color = Tint.WithAlpha(Math.Clamp(alpha, 0f, 1f) * 0.85f);
        }
    }

    /// <summary>A dark, clear, unobstructed floor tile near the edge of what the player sees.</summary>
    private EntityCoordinates? PickSpot(EntityUid me)
    {
        var view = _eye.GetWorldViewbounds();
        var half = view.Box.Size / 2f;
        var map = _transform.GetMapCoordinates(me).MapId;
        if (map == MapId.Nullspace)
            return null;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            // A point in the outer fifth of the view, on a random side.
            var along = _random.NextFloat(0.62f, 0.92f) * (_random.Prob(0.5f) ? 1f : -1f);
            var across = _random.NextFloat(-0.9f, 0.9f);
            var local = _random.Prob(0.5f) ? new Vector2(along, across) : new Vector2(across, along);
            var world = view.Center + view.Rotation.RotateVec(local * half);
            var at = new MapCoordinates(world, map);

            if (!_maps.TryFindGridAt(at, out var grid, out _))
                continue;

            var coordinates = _transform.ToCoordinates(at);
            if (!_turf.TryGetTileRef(coordinates, out var tile)
                || _turf.IsSpace(tile.Value)
                || _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable)
                || !_examine.InRangeUnOccluded(me, at, LightSearch)
                || !IsDark(at, grid))
            {
                continue;
            }

            return _turf.GetTileCenter(tile.Value);
        }

        return null;
    }

    /// <summary>True if no light in sight of a spot lights it brightly and the map isn't lit by day.</summary>
    private bool IsDark(MapCoordinates at, EntityUid grid)
    {
        var ambient = 0f;
        if (TryComp<MapLightComponent>(grid, out var gridLight) || TryComp(_maps.GetMapOrInvalid(at.MapId), out gridLight))
            ambient = Brightness(gridLight.AmbientLightColor);

        _lights.Clear();
        _lookup.GetEntitiesInRange(at, LightSearch, _lights);
        var lights = new List<WFDreadLight>();
        foreach (var light in _lights)
        {
            // A light behind a wall doesn't light the spot.
            if (light.Comp.Enabled && !light.Comp.ContainerOccluded && _examine.InRangeUnOccluded(light.Owner, at, LightSearch + light.Comp.Radius))
                lights.Add(new WFDreadLight(_transform.GetWorldPosition(light), light.Comp.Radius, light.Comp.Energy));
        }

        return WFDreadMath.IsDark(at.Position, lights, ambient, DaylightLimit);
    }

    private static float Brightness(Color linear) => 0.2126f * linear.R + 0.7152f * linear.G + 0.0722f * linear.B;
}
