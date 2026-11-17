using Robust.Shared.Serialization;

namespace Content.Shared._WG.Mime;

/// <summary>Marks the finger gun, so firing it shows a popup to others.</summary>
[RegisterComponent]
public sealed partial class FingerGunComponent : Component;

/// <summary>Goes on the finger gun shot and sets the effect it plays on a hit.</summary>
[RegisterComponent]
public sealed partial class FingerGunBulletComponent : Component
{
    /// <summary>Colours both the flash over the target and the edge on their screen.</summary>
    [DataField(required: true)]
    public Color HitColor;

    /// <summary>How much of the screen edge one hit adds, where 1 is as red as it gets.</summary>
    [DataField(required: true)]
    public float HitIntensity;

    /// <summary>Seconds the edge takes to climb to where a hit put it.</summary>
    [DataField(required: true)]
    public float RiseSeconds;

    /// <summary>Seconds a full strength edge takes to drain back to nothing.</summary>
    [DataField(required: true)]
    public float FadeSeconds;
}

/// <summary>Sent to the target's client only, telling it to play the bloody-screen vignette.</summary>
[Serializable, NetSerializable]
public sealed class FingerGunShotEvent : EntityEventArgs
{
    /// <summary>The bullet's <see cref="FingerGunBulletComponent.HitIntensity"/>.</summary>
    public float Intensity;

    /// <summary>The bullet's <see cref="FingerGunBulletComponent.RiseSeconds"/>.</summary>
    public float RiseSeconds;

    /// <summary>The bullet's <see cref="FingerGunBulletComponent.FadeSeconds"/>.</summary>
    public float FadeSeconds;

    /// <summary>The bullet's <see cref="FingerGunBulletComponent.HitColor"/>.</summary>
    public Color Color;
}
