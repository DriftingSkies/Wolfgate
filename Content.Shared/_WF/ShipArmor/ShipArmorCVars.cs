using Robust.Shared.Configuration;

namespace Content.Shared._WF.ShipArmor;

/// <summary>Tuning for armor-backed ship mounts.</summary>
[CVarDefs]
public sealed class ShipArmorCVars
{
    /// <summary>Share of a blast or shell hit, armor-piercing included, that a fully covered mount hands to hull armor.</summary>
    public static readonly CVarDef<float> Transfer =
        CVarDef.Create("wf.ship_armor.transfer", 1.0f, CVar.SERVERONLY);

    /// <summary>Armor thickness that gives full coverage; thinner armor scales the share down (plastitanium 10 = 80%).</summary>
    public static readonly CVarDef<float> FullThickness =
        CVarDef.Create("wf.ship_armor.full_thickness", 12.5f, CVar.SERVERONLY);
}
