using System.Numerics;
using Content.Client.Camera;
using Content.Shared._WF.Warheads;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warheads;

/// <summary>
/// Tracks nuclear detonations on the client and drives their flash, shockwave, fireball, cloud and camera shake.
/// </summary>
public sealed class WFWarheadEffectsSystem : EntitySystem
{
    [Dependency] private CameraRecoilSystem _recoil = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Seconds the flash takes to fade.</summary>
    public const float FlashFade = 3f;

    /// <summary>Seconds the camera keeps shaking after the shockwave passes.</summary>
    private const float ShakeTime = 1.2f;

    public readonly List<WFActiveDetonation> Active = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFNuclearDetonationEvent>(OnDetonation);

        _overlays.AddOverlay(new WFNuclearBlastOverlay());
        _overlays.AddOverlay(new WFNuclearFlashOverlay());
        _overlays.AddOverlay(new WFCountdownOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFNuclearBlastOverlay>();
        _overlays.RemoveOverlay<WFNuclearFlashOverlay>();
        _overlays.RemoveOverlay<WFCountdownOverlay>();
    }

    private void OnDetonation(WFNuclearDetonationEvent ev)
    {
        var map = GetEntity(ev.Map);
        if (!TryComp<MapComponent>(map, out var mapComp))
            return;

        Active.Add(new WFActiveDetonation(ev, mapComp.MapId, _random.NextFloat()));
    }

    /// <summary>Seconds since a detonation started.</summary>
    public float Age(WFActiveDetonation det)
    {
        return (float) (_timing.CurTime - det.Event.Start).TotalSeconds;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var player = _player.LocalEntity;
        var playerPos = player != null ? _transform.GetMapCoordinates(player.Value) : MapCoordinates.Nullspace;

        for (var i = Active.Count - 1; i >= 0; i--)
        {
            var det = Active[i];
            var age = Age(det);
            var ev = det.Event;
            var shockTime = ev.ShockwaveSpeed > 0f ? ev.ShockwaveRange / ev.ShockwaveSpeed : 0f;

            if (age > Math.Max(Math.Max(ev.Duration, shockTime), FlashFade))
            {
                Active.RemoveAt(i);
                continue;
            }

            if (player == null || playerPos.MapId != det.MapId || ev.ShakeStrength <= 0f)
                continue;

            var distance = Vector2.Distance(playerPos.Position, ev.Position);
            if (distance > ev.ShockwaveRange || age * ev.ShockwaveSpeed < distance)
                continue;

            det.ShakeLeft ??= ShakeTime;
            if (det.ShakeLeft <= 0f)
                continue;

            det.ShakeLeft -= frameTime;
            var strength = ev.ShakeStrength * (1f - distance / Math.Max(ev.ShockwaveRange, 1f)) * (det.ShakeLeft.Value / ShakeTime);
            _recoil.KickCamera(player.Value, _random.NextVector2(strength * 0.15f));
        }
    }
}

/// <summary>A detonation the client is drawing.</summary>
public sealed class WFActiveDetonation(WFNuclearDetonationEvent ev, MapId mapId, float seed)
{
    public readonly WFNuclearDetonationEvent Event = ev;
    public readonly MapId MapId = mapId;
    public readonly float Seed = seed;

    /// <summary>Seconds of camera shake left once the shockwave reaches the player; null until it does.</summary>
    public float? ShakeLeft;
}
