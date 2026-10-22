using Content.Server._WF.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
/// Caps networked gunshot sounds for fast guns. The shooter hears every shot predicted on their own client; this
/// limits the networked copies other players receive to five per second per gun.
/// </summary>
public sealed partial class GunSystem
{
    // Judgement call: above this a gun is throttled, and a throttled gun sounds at most once per interval.
    private const float WfUnthrottledFireRate = 5f;
    private static readonly TimeSpan WfGunshotInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>Plays a gunshot, skipping it if a fast gun already sounded within the interval.</summary>
    private void WfPlayGunshot(EntityUid gunUid, GunComponent gun, SoundSpecifier? sound, EntityUid? user)
    {
        if (gun.FireRateModified <= WfUnthrottledFireRate)
        {
            Audio.PlayPredicted(sound, gunUid, user);
            return;
        }

        var throttle = EnsureComp<WFGunshotAudioThrottleComponent>(gunUid);
        if (Timing.CurTime < throttle.Next)
            return;

        throttle.Next = Timing.CurTime + WfGunshotInterval;
        Audio.PlayPredicted(sound, gunUid, user);
    }
}
