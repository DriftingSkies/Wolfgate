using Content.Server._WG.EngineCompat;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private WGGridSplitOriginSystem _wgSplitOrigin = default!;
}
