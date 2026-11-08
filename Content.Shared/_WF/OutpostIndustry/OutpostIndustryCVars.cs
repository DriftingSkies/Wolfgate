using Robust.Shared.Configuration;

namespace Content.Shared._WF.OutpostIndustry;

/// <summary>Settings for the producers an outpost runs.</summary>
[CVarDefs]
public sealed class OutpostIndustryCVars
{
    /// <summary>The most an outpost's producers make in an hour, in spesos of value, all of them together.</summary>
    public static readonly CVarDef<int> HourlyCap =
        CVarDef.Create("wf.outposts.producer_cap", 4000, CVar.SERVERONLY);
}
