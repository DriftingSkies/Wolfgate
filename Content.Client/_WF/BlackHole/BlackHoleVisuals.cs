using System.Numerics;

namespace Content.Client._WF.BlackHole;

/// <summary>Shares the black hole's apparent geometry between rendering and visibility checks.</summary>
public static class BlackHoleVisuals
{
    /// <summary>Fraction of camera movement retained by the distant star.</summary>
    public const float ParallaxFactor = 0.05f;

    /// <summary>World-space shadow radius per stellar radius.</summary>
    public const float SolarRadiusFactor = 12f;

    /// <summary>Maximum lensing and emission radius relative to the shadow.</summary>
    public const float EffectRadius = 5.2f;

    /// <summary>Includes any visible part of the parallaxed disk, halo or lensing field.</summary>
    public static bool IsVisible(Vector2 starPosition, float starRadius, Vector2 eyePosition, Box2 viewport)
    {
        var center = starPosition + (eyePosition - starPosition) * (1f - ParallaxFactor);
        var radius = starRadius * SolarRadiusFactor * EffectRadius;
        return Vector2.DistanceSquared(center, viewport.ClosestPoint(center)) <= radius * radius;
    }
}
