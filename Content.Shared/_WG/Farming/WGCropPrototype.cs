using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Farming;

/// <summary>
/// How a seed farms on an outpost: the real minutes of growing it takes to ripen (minutes it is lit and watered), and the
/// outdoor climate it grows in. The ID is the botany seed's ID.
/// </summary>
[Prototype("wgCrop")]
public sealed partial class WGCropPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Real minutes from planting to ripe, counting only time it was lit and watered.</summary>
    [DataField]
    public float Minutes = 60f;

    /// <summary>Outdoor temperature band in Celsius; outside it the crop stalls unless it is under a roof and a grow lamp.</summary>
    [DataField]
    public float MinTemp = 5f;

    [DataField]
    public float MaxTemp = 35f;
}

/// <summary>Harvest quality, after Stardew: each step raises the price.</summary>
public enum WGCropQuality : byte
{
    Normal,
    Silver,
    Gold,
    Iridium,
}
