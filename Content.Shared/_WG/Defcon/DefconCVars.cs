using Robust.Shared.Configuration;

namespace Content.Shared._WG.Defcon;

/// <summary>Settings for the sector DEFCON.</summary>
[CVarDefs]
public sealed class DefconCVars
{
    /// <summary>Whether the sector DEFCON follows the threat.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.defcon.enabled", true, CVar.SERVERONLY);

    /// <summary>The <see cref="WGDefconProfilePrototype"/> in force.</summary>
    public static readonly CVarDef<string> Profile =
        CVarDef.Create("wg.defcon.profile", "WGDefault", CVar.SERVERONLY);
}
