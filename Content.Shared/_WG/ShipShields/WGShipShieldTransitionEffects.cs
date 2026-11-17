using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipShields;

/// <summary>The visual transition of a ship shield field.</summary>
[Serializable, NetSerializable]
public enum WGShipShieldTransition : byte
{
    None,
    Forming,
    Collapsing,
    Lowering,
}

/// <summary>Controls field transition timing independently of interception.</summary>
public static class WGShipShieldTransitionEffects
{
    /// <summary>Returns the visual transition duration in seconds.</summary>
    public static float Duration(WGShipShieldTransition transition)
    {
        return transition switch
        {
            WGShipShieldTransition.Forming => 1f,
            WGShipShieldTransition.Collapsing => 0.9f,
            WGShipShieldTransition.Lowering => 0.7f,
            _ => 0f,
        };
    }

    /// <summary>Returns clamped transition progress for an elapsed server time.</summary>
    public static float Progress(WGShipShieldTransition transition, float elapsed)
    {
        var duration = Duration(transition);
        return duration > 0f ? Math.Clamp(elapsed / duration, 0f, 1f) : 1f;
    }
}
