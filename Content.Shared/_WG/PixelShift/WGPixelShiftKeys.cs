using Robust.Shared.Input;

namespace Content.Shared._WG.PixelShift;

/// <summary>Pixel shift keys, bound in the human input context.</summary>
[KeyFunctions]
public static class WGPixelShiftKeys
{
    /// <summary>While held, the movement keys nudge your sprite a pixel instead of moving you.</summary>
    public static readonly BoundKeyFunction WGPixelShift = "WGPixelShift";

    /// <summary>While held, left and right tilt your sprite instead of moving you.</summary>
    public static readonly BoundKeyFunction WGPixelTilt = "WGPixelTilt";

    /// <summary>Puts your sprite back where it started, upright.</summary>
    public static readonly BoundKeyFunction WGPixelShiftReset = "WGPixelShiftReset";
}
