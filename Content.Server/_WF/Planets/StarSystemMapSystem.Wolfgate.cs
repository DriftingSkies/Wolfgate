using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._Mono.ForceParent;
using Robust.Shared.Map;

namespace Content.Server._FarHorizons.StarSystem;

public sealed partial class StarSystemMapSystem
{
    [Dependency] private WFPlanetRegistrySystem _wfPlanets = default!;
    [Dependency] private ForceParentSystem _wfForceParent = default!;

    /// <summary>Hands a freshly spawned sector body to the Wolfgate planet registry.</summary>
    private void WfPlanetSpawned(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet)
    {
        WfPinBody(map, planetEntity, planet.Position);
        _wfPlanets.RegisterPlanet(map, planetEntity, planet);
    }

    /// <summary>
    /// Pins a star or planet to the sector map. Grid traversal otherwise parents it to a hull that arrives on top
    /// of it, which carries the body (and its orbit) off and deletes it, with the whole planet, when the hull goes.
    /// </summary>
    private void WfPinBody(EntityUid map, EntityUid body, Vector2 position)
    {
        _wfForceParent.SetForceParent(body, new EntityCoordinates(map, position));
    }
}
