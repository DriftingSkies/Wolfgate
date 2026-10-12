using Robust.Shared.Configuration;

namespace Content.Shared._WF.ShipArmor;

/// <summary>Tuning for armor-backed ship mounts.</summary>
[CVarDefs]
public sealed class ShipArmorCVars
{
    /// <summary>Share of explosion damage a fully covered mount hands to hull armor.</summary>
    public static readonly CVarDef<float> ExplosionTransfer =
        CVarDef.Create("wf.ship_armor.explosion_transfer", 0.8f, CVar.SERVERONLY);

    /// <summary>Share of non-penetrating projectile damage a fully covered mount hands to hull armor.</summary>
    public static readonly CVarDef<float> ProjectileTransfer =
        CVarDef.Create("wf.ship_armor.projectile_transfer", 0.4f, CVar.SERVERONLY);

    /// <summary>Share of penetrating projectile damage a fully covered mount hands to hull armor.</summary>
    public static readonly CVarDef<float> PenetratingTransfer =
        CVarDef.Create("wf.ship_armor.penetrating_transfer", 0f, CVar.SERVERONLY);

    /// <summary>Armor thickness that gives full coverage; thinner armor scales the transfer down.</summary>
    public static readonly CVarDef<int> FullThickness =
        CVarDef.Create("wf.ship_armor.full_thickness", 10, CVar.SERVERONLY);
}
