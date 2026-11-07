using Robust.Shared.Configuration;

namespace Content.Shared._WF.ShipPa;

/// <summary>Client settings for the ship PA.</summary>
[CVarDefs]
public sealed class ShipPaCVars
{
    /// <summary>Lowest personal PA volume, so players can't mute ship alarms outright.</summary>
    public const float MinVolume = 0.1f;

    /// <summary>The player's own PA volume, <see cref="MinVolume"/> to 1, applied on top of the pilot's ship setting.</summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wf.ship_pa.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
