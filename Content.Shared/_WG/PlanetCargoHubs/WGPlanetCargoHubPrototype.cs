using Content.Shared._WG.PlanetPois;
using Content.Shared._WG.Planets;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PlanetCargoHubs;

/// <summary>A cargo depot built on a planet's ground instead of in space: its site map and the worlds it may stand on.</summary>
[Prototype("wgPlanetCargoHub")]
public sealed partial class WGPlanetCargoHubPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The site as a world places it: map, footprint and road socket. Its name is set per depot letter.</summary>
    [DataField(required: true)]
    public WGPlanetPoiEntry Site = default!;

    /// <summary>Worlds the hub may be built on.</summary>
    [DataField(required: true)]
    public List<ProtoId<WGPlanetSurfacePrototype>> Surfaces = new();
}
