using Content.Server.Atmos.Components;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>How many tiles a zone equalization search may collect; maps without space can never depressurize, so only the usable 200 are needed.</summary>
    private int WFMonstermosSearchLimit(TransformComponent xform)
    {
        if (TryComp<MapAtmosphereComponent>(xform.MapUid, out var map) && !map.Space)
            return Atmospherics.MonstermosTileLimit;

        return Atmospherics.MonstermosHardTileLimit;
    }
}
