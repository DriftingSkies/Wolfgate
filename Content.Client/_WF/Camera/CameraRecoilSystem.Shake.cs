using System.Numerics;
using Content.Shared.Camera;
using Robust.Shared.Random;

namespace Content.Client.Camera;

public sealed partial class CameraRecoilSystem
{
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Furthest a kick moves the view, in tiles.</summary>
    private const float WFMaxOffset = 0.3f;

    /// <summary>Kick past <see cref="WFMaxOffset"/> it takes to start a shake.</summary>
    private const float WFShakeThreshold = 0.2f;

    private const float WFMaxShakeStrength = 4f;
    private const float WFMinShakeFrequency = 10f;
    private const float WFMaxShakeFrequency = 30f;
    private const float WFMaxShakeTime = 0.8f;

    /// <summary>Seconds over which a shake's amplitude fades out at its end.</summary>
    private const float WFShakeFade = 0.3f;

    private readonly Dictionary<EntityUid, WFShake> _wfShakes = new();
    private readonly List<EntityUid> _wfShakesDone = new();

    private sealed class WFShake
    {
        public float Strength;
        public float Remaining;
        public float Timer;
    }

    /// <summary>Moves the view a short way; any excess becomes a shake in place that is faster the harder the kick.</summary>
    private void WFKickCamera(EntityUid uid, CameraRecoilComponent component, Vector2 recoil)
    {
        var length = recoil.Length();
        if (!(length > 0f) || float.IsInfinity(length))
            return;

        if (length > WFMaxOffset)
        {
            var overflow = length - WFMaxOffset;
            recoil *= WFMaxOffset / length;

            if (overflow > WFShakeThreshold)
                WFStartShake(uid, overflow);
        }

        var dampen = Math.Min(1f, component.CurrentKick.Length() / WFMaxOffset);
        component.CurrentKick += recoil * (1 - dampen);

        if (component.CurrentKick.Length() > WFMaxOffset)
            component.CurrentKick = component.CurrentKick.Normalized() * WFMaxOffset;

        component.LastKickTime = 0;
    }

    private void WFStartShake(EntityUid uid, float overflow)
    {
        var strength = Math.Min(overflow, WFMaxShakeStrength);
        var duration = Math.Min(WFMaxShakeTime, 0.15f + 0.15f * strength);

        if (!_wfShakes.TryGetValue(uid, out var shake))
        {
            _wfShakes[uid] = new WFShake { Strength = strength, Remaining = duration };
            return;
        }

        shake.Strength = Math.Max(shake.Strength, strength);
        shake.Remaining = Math.Max(shake.Remaining, duration);
    }

    public override void FrameUpdate(float frameTime)
    {
        WFUpdateShakes(frameTime);
        base.FrameUpdate(frameTime);
    }

    private void WFUpdateShakes(float frameTime)
    {
        if (_intensity == 0)
        {
            _wfShakes.Clear();
            return;
        }

        foreach (var (uid, shake) in _wfShakes)
        {
            shake.Remaining -= frameTime;
            if (shake.Remaining <= 0 || !TryComp<CameraRecoilComponent>(uid, out var recoil))
            {
                _wfShakesDone.Add(uid);
                continue;
            }

            shake.Timer -= frameTime;
            if (shake.Timer > 0)
                continue;

            var frequency = MathHelper.Lerp(WFMinShakeFrequency, WFMaxShakeFrequency, shake.Strength / WFMaxShakeStrength);
            // Carry the overshoot so the beat keeps its frequency at any frame rate.
            shake.Timer = Math.Max(0f, shake.Timer + 1f / frequency);

            // Swing to the far side of the last offset, give or take 60 degrees.
            var amplitude = WFMaxOffset * Math.Min(1f, shake.Remaining / WFShakeFade);
            var direction = recoil.CurrentKick.LengthSquared() > 0f
                ? -recoil.CurrentKick.Normalized()
                : _random.NextAngle().ToVec();
            var angle = new Angle(_random.NextFloat(-MathF.PI / 3f, MathF.PI / 3f));
            recoil.CurrentKick = angle.RotateVec(direction) * amplitude;

            // Half the restore ramp in, so the view settles between beats.
            recoil.LastKickTime = 0.5f;
        }

        foreach (var uid in _wfShakesDone)
        {
            _wfShakes.Remove(uid);
        }

        _wfShakesDone.Clear();
    }
}
