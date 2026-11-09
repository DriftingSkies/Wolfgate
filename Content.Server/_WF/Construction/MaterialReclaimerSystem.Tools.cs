using Content.Shared.Tools.Systems;
using Content.Shared.Wires;

namespace Content.Server.Materials;

public sealed partial class MaterialReclaimerSystem
{
    [Dependency] private SharedToolSystem _wfTools = default!;

    /// <summary>Whether a used item is a screwdriver or crowbar meant for the reclaimer's panel, so it isn't fed in.</summary>
    private bool WfIsPanelTool(EntityUid reclaimer, EntityUid used)
    {
        return HasComp<WiresPanelComponent>(reclaimer)
               && (_wfTools.HasQuality(used, "Screwing") || _wfTools.HasQuality(used, "Prying"));
    }
}
