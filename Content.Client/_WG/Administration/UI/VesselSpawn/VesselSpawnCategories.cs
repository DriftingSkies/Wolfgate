using System.Linq;
using Content.Client._WG.OldVessels;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Client._WG.Administration.UI.VesselSpawn;

/// <summary>
/// Buckets a vessel into one broad shopping category, from its classes and shipyard group.
/// </summary>
public enum VesselSpawnCategory : byte
{
    Salvage,
    Science,
    CargoEngineering,
    Medical,
    Civilian,
    Expedition,
    Escort,
    Security,
    Military,
    Antagonist,
    Other,
    AdminOnly,
}

/// <summary>
/// Sorts vessels into <see cref="VesselSpawnCategory"/> buckets for the spawn window's grouped list.
/// </summary>
public static class VesselSpawnCategories
{
    private static readonly VesselClass[] AntagClasses = { VesselClass.Syndicate, VesselClass.Pirate, VesselClass.Mercenary };
    private static readonly VesselClass[] SecurityClasses = { VesselClass.Patrol, VesselClass.Detainment, VesselClass.Detective };
    private static readonly VesselClass[] MilitaryClasses = { VesselClass.Corvette, VesselClass.Frigate, VesselClass.Destroyer, VesselClass.Cruiser, VesselClass.Capital, VesselClass.Fighter };
    private static readonly VesselClass[] SalvageClasses = { VesselClass.Salvage, VesselClass.Scrapyard };
    private static readonly VesselClass[] CargoEngineeringClasses = { VesselClass.Cargo, VesselClass.Engineering, VesselClass.Atmospherics };
    private static readonly VesselClass[] CivilianClasses = { VesselClass.Civilian, VesselClass.Kitchen, VesselClass.Botany, VesselClass.Chemistry };

    /// <summary>Display order for both the grouped list and the category filter dropdown.</summary>
    public static readonly VesselSpawnCategory[] DisplayOrder =
    {
        VesselSpawnCategory.Salvage,
        VesselSpawnCategory.Science,
        VesselSpawnCategory.CargoEngineering,
        VesselSpawnCategory.Medical,
        VesselSpawnCategory.Civilian,
        VesselSpawnCategory.Expedition,
        VesselSpawnCategory.Escort,
        VesselSpawnCategory.Security,
        VesselSpawnCategory.Military,
        VesselSpawnCategory.Antagonist,
        VesselSpawnCategory.Other,
        VesselSpawnCategory.AdminOnly,
    };

    /// <summary>
    /// Picks the vessel's category. Admin-only vessels go in their own bucket; otherwise rules are evaluated in order
    /// over all of its classes plus its shipyard group, and the first match wins.
    /// </summary>
    public static VesselSpawnCategory Get(VesselPrototype vessel)
    {
        var classes = vessel.Classes;
        var group = vessel.Group;

        if (ClassicVessels.IsAdminOnly(vessel))
            return VesselSpawnCategory.AdminOnly;
        if (group is ShipyardConsoleUiKey.Syndicate or ShipyardConsoleUiKey.BlackMarket || classes.Any(c => AntagClasses.Contains(c)))
            return VesselSpawnCategory.Antagonist;
        if (group == ShipyardConsoleUiKey.Security || classes.Any(c => SecurityClasses.Contains(c)))
            return VesselSpawnCategory.Security;
        if (classes.Any(c => MilitaryClasses.Contains(c)))
            return VesselSpawnCategory.Military;
        if (group == ShipyardConsoleUiKey.Expedition || classes.Contains(VesselClass.Expedition))
            return VesselSpawnCategory.Expedition;
        if (classes.Any(c => SalvageClasses.Contains(c)) || group == ShipyardConsoleUiKey.Scrap)
            return VesselSpawnCategory.Salvage;
        if (group == ShipyardConsoleUiKey.Medical || classes.Contains(VesselClass.Medical))
            return VesselSpawnCategory.Medical;
        if (classes.Contains(VesselClass.Science))
            return VesselSpawnCategory.Science;
        if (classes.Any(c => CargoEngineeringClasses.Contains(c)))
            return VesselSpawnCategory.CargoEngineering;
        if (classes.Any(c => CivilianClasses.Contains(c)))
            return VesselSpawnCategory.Civilian;
        if (classes.Contains(VesselClass.Pursuit))
            return VesselSpawnCategory.Escort;

        return VesselSpawnCategory.Other;
    }
}
