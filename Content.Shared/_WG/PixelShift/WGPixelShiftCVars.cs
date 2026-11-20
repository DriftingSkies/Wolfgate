using Robust.Shared.Configuration;

namespace Content.Shared._WG.PixelShift;

/// <summary>Pixel shift limits, replicated so the client predicts the same clamp, and each player's speeds.</summary>
[CVarDefs]
public sealed class WGPixelShiftCVars
{
    /// <summary>
    /// Furthest a sprite can be shifted on each axis, in pixels. 16 is half a tile, so a shifted mob still reads as
    /// standing on its own tile.
    /// </summary>
    public static readonly CVarDef<int> MaxOffset =
        CVarDef.Create("wg.pixel_shift_max_offset", 16, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Furthest a sprite can be tilted either way, in degrees. 45 leans well short of lying down at 90.</summary>
    public static readonly CVarDef<int> MaxTilt =
        CVarDef.Create("wg.pixel_shift_max_tilt", 45, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// How fast a held movement key shifts the sprite, in pixels per second. Replicated so the server knows each
    /// player's choice; clamped by <see cref="WGPixelShiftSystem.ClampShiftSpeed"/>.
    /// </summary>
    public static readonly CVarDef<int> ShiftSpeed =
        CVarDef.Create("wg.pixel_shift_speed", 20, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// How fast a held movement key tilts the sprite, in degrees per second. Clamped by
    /// <see cref="WGPixelShiftSystem.ClampTiltSpeed"/>.
    /// </summary>
    public static readonly CVarDef<int> TiltSpeed =
        CVarDef.Create("wg.pixel_tilt_speed", 100, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
