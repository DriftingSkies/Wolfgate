using Robust.Shared.Configuration;

namespace Content.Shared._WF.Hideouts;

[CVarDefs]
public sealed class HideoutCVars
{
    /// <summary>Whether outposts take modules and keep a hangar ship.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.hideouts.enabled", true, CVar.SERVERONLY);
}
