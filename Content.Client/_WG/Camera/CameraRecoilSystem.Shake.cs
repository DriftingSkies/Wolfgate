using System.Numerics;
using Content.Shared.Camera;
using Robust.Shared.Random;

namespace Content.Client.Camera;

public sealed partial class CameraRecoilSystem
{
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Furthest a kick moves the view, in tiles.</summary>
    private const float WGMaxOffset = 0.3f;

    /// <summary>Kick past <see cref="WGMaxOffset"/> it takes to start a shake.</summary>
    private const float WGShakeThreshold = 0.2f;

    private const float WGMaxShakeStrength = 4f;
    private const float WGMinShakeFrequency = 10f;
    private const float WGMaxShakeFrequency = 30f;
    private const float WGMaxShakeTime = 0.8f;

    /// <summary>Seconds over which a shake's amplitude fades out at its end.</summary>
    private const float WGShakeFade = 0.3f;

    private readonly Dictionary<EntityUid, WGShake> _wgShakes = new();
    private readonly List<EntityUid> _wgShakesDone = new();

    private sealed class WGShake
    {
        public float Strength;
        public float Remaining;
        public float Timer;
    }

    /// <summary>Moves the view a short way; any excess becomes a shake in place that is faster the harder the kick.</summary>
    private void WGKickCamera(EntityUid uid, CameraRecoilComponent component, Vector2 recoil)
    {
        var length = recoil.Length();
        if (!(length > 0f) || float.IsInfinity(length))
            return;

        if (length > WGMaxOffset)
        {
            var overflow = length - WGMaxOffset;
            recoil *= WGMaxOffset / length;

            if (overflow > WGShakeThreshold)
                WGStartShake(uid, overflow);
        }

        var dampen = Math.Min(1f, component.CurrentKick.Length() / WGMaxOffset);
        component.CurrentKick += recoil * (1 - dampen);

        if (component.CurrentKick.Length() > WGMaxOffset)
            component.CurrentKick = component.CurrentKick.Normalized() * WGMaxOffset;

        component.LastKickTime = 0;
    }

    private void WGStartShake(EntityUid uid, float overflow)
    {
        var strength = Math.Min(overflow, WGMaxShakeStrength);
        var duration = Math.Min(WGMaxShakeTime, 0.15f + 0.15f * strength);

        if (!_wgShakes.TryGetValue(uid, out var shake))
        {
            _wgShakes[uid] = new WGShake { Strength = strength, Remaining = duration };
            return;
        }

        shake.Strength = Math.Max(shake.Strength, strength);
        shake.Remaining = Math.Max(shake.Remaining, duration);
    }

    public override void FrameUpdate(float frameTime)
    {
        WGUpdateShakes(frameTime);
        base.FrameUpdate(frameTime);
    }

    private void WGUpdateShakes(float frameTime)
    {
        if (_intensity == 0)
        {
            _wgShakes.Clear();
            return;
        }

        foreach (var (uid, shake) in _wgShakes)
        {
            shake.Remaining -= frameTime;
            if (shake.Remaining <= 0 || !TryComp<CameraRecoilComponent>(uid, out var recoil))
            {
                _wgShakesDone.Add(uid);
                continue;
            }

            shake.Timer -= frameTime;
            if (shake.Timer > 0)
                continue;

            var frequency = MathHelper.Lerp(WGMinShakeFrequency, WGMaxShakeFrequency, shake.Strength / WGMaxShakeStrength);
            // Carry the overshoot so the beat keeps its frequency at any frame rate.
            shake.Timer = Math.Max(0f, shake.Timer + 1f / frequency);

            // Swing to the far side of the last offset, give or take 60 degrees.
            var amplitude = WGMaxOffset * Math.Min(1f, shake.Remaining / WGShakeFade);
            var direction = recoil.CurrentKick.LengthSquared() > 0f
                ? -recoil.CurrentKick.Normalized()
                : _random.NextAngle().ToVec();
            var angle = new Angle(_random.NextFloat(-MathF.PI / 3f, MathF.PI / 3f));
            recoil.CurrentKick = angle.RotateVec(direction) * amplitude;

            // Half the restore ramp in, so the view settles between beats.
            recoil.LastKickTime = 0.5f;
        }

        foreach (var uid in _wgShakesDone)
        {
            _wgShakes.Remove(uid);
        }

        _wgShakesDone.Clear();
    }
}
