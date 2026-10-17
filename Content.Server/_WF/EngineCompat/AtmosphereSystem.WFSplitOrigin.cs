using Content.Server._WF.EngineCompat;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private WFGridSplitOriginSystem _wfSplitOrigin = default!;
}
