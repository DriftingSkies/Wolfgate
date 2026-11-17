using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WG.Xenonids.Parasite;

/// <summary>RMC's parasite cvars, which Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WGXenoParasiteCVars : CVars
{
    public static readonly CVarDef<float> RMCParasiteSpawnInitialDelayMinutes =
        CVarDef.Create("rmc.parasite_spawn_initial_delay_minutes", 15f, CVar.REPLICATED | CVar.SERVER);
}
