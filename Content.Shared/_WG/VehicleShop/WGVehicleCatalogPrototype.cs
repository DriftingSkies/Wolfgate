using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.VehicleShop;

/// <summary>The vehicles a purchase terminal sells, and their prices.</summary>
[Prototype("wgVehicleCatalog")]
public sealed partial class WGVehicleCatalogPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<WGVehicleListing> Vehicles = new();
}

/// <summary>One vehicle for sale.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WGVehicleListing
{
    [DataField(required: true)]
    public EntProtoId Vehicle;

    [DataField(required: true)]
    public int Price;

    /// <summary>Whether only someone carrying a heavy vehicle licence may buy it.</summary>
    [DataField]
    public bool Licence;
}
