using Robust.Shared.Configuration;

namespace Content.Shared._WF.ReaverCampaign;

/// <summary>Settings for the Ashfall Reaver campaign.</summary>
[CVarDefs]
public sealed class ReaverCampaignCVars
{
    /// <summary>Admin override: forces the campaign on whether or not the Contested Sector rule runs. It also needs the storyteller and sector control on.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.reavers.enabled", false, CVar.SERVERONLY);

    /// <summary>Admin and test override for the population the campaign scales by; below zero it counts the living players.</summary>
    public static readonly CVarDef<int> PopulationOverride =
        CVarDef.Create("wf.reavers.population_override", -1, CVar.SERVERONLY);
}
