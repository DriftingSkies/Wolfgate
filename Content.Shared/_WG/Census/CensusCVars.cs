using Robust.Shared.Configuration;

namespace Content.Shared._WG.Census;

/// <summary>Settings for player censuses.</summary>
[CVarDefs]
public sealed class CensusCVars
{
    /// <summary>Whether players see censuses and may answer them. The panel's endpoints work either way.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.census.enabled", true, CVar.SERVER | CVar.REPLICATED);
}
