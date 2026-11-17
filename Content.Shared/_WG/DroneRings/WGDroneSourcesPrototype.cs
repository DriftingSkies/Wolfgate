using Robust.Shared.Prototypes;

namespace Content.Shared._WG.DroneRings;

/// <summary>Debris-table entries that spawn drones. A space biome that lists one counts as drone space.</summary>
[Prototype("wgDroneSources")]
public sealed partial class WGDroneSourcesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<EntProtoId> Spawners = new();
}
