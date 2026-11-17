using Content.Shared._WG.PlanetNatives;
using Content.Shared._WG.Planets;

namespace Content.Server._WG.PlanetNatives;

/// <summary>Marks every NPC that wakes up on a planet's ground, caverns or sky as a native, so natives stop killing each other.</summary>
public sealed class WGPlanetNativeSystem : EntitySystem
{
    /// <summary>Called as an NPC is map-initialised; marks it if it stands on a planet layer other than orbit.</summary>
    public void TryMark(EntityUid uid)
    {
        if (Transform(uid).MapUid is not { } map
            || !HasComp<WGPlanetLayerComponent>(map)
            || HasComp<WGOrbitLayerComponent>(map))
            return;

        EnsureComp<WGPlanetNativeComponent>(uid);
    }
}
