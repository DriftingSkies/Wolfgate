using Content.Shared._WG.Farming;

namespace Content.Server._WG.Farming;

/// <summary>A plant holder grown by <see cref="WGFarmingSystem"/>: light, water, climate, fertilizer and care.</summary>
[RegisterComponent, Access(typeof(WGFarmingSystem))]
public sealed partial class WGFarmPlotComponent : Component
{
    /// <summary>Whether the farm is what holds the plant back this cycle, so it lets go once growing resumes.</summary>
    [DataField]
    public bool Stalled;

    /// <summary>The SkipAging value the stall last set, raising it from 0 to 1; 0 if it added none. Taken back when growing resumes.</summary>
    [DataField]
    public int StallSkip;

    /// <summary>Checks this plant was lit, and of those how many it was also watered and fed; sets harvest quality.</summary>
    [DataField]
    public int LitChecks;

    [DataField]
    public int CaredChecks;

    /// <summary>Fertilizer in the soil until the plant is gone: quality steps, growth speed share, and retaining soil.</summary>
    [DataField]
    public int FertQuality;

    [DataField]
    public float FertSpeed;

    [DataField]
    public bool Retain;

    /// <summary>Whether a plant grew here at the last check; the soil's fertilizer goes when the plant does.</summary>
    [DataField]
    public bool HadSeed;

    /// <summary>The planet day retaining soil last watered the plant.</summary>
    [DataField]
    public int RetainDay = -1;

    /// <summary>Why it isn't growing, for examine; null while it grows.</summary>
    [ViewVariables]
    public string? Reason;
}

/// <summary>A lamp that lets plants within its range grow at night and indoors while it has power.</summary>
[RegisterComponent]
public sealed partial class WGGrowLampComponent : Component
{
    [DataField]
    public float Range = 2.5f;
}

/// <summary>The quality a harvest rolled; multiplies the produce's price.</summary>
[RegisterComponent]
public sealed partial class WGProduceQualityComponent : Component
{
    [DataField]
    public WGCropQuality Quality;
}

/// <summary>Fertilizer: used on a plant holder, it stays in the soil until the plant is gone.</summary>
[RegisterComponent]
public sealed partial class WGFertilizerComponent : Component
{
    /// <summary>Quality steps it may add at harvest; two reach iridium.</summary>
    [DataField]
    public int Quality;

    /// <summary>Extra growth speed, 0.25 for 25% faster.</summary>
    [DataField]
    public float Speed;

    /// <summary>Retaining soil: a dry plant is watered once a planet day.</summary>
    [DataField]
    public bool Retain;
}

/// <summary>Waters the plant holders round it while it has power, or always if it needs none.</summary>
[RegisterComponent]
public sealed partial class WGSprinklerComponent : Component
{
    /// <summary>Tiles it reaches.</summary>
    [DataField]
    public int Range = 1;

    /// <summary>A square of that range; otherwise a plus shape.</summary>
    [DataField]
    public bool Square;

    /// <summary>The solution it waters from; fill it with water from a bucket or the water synthesizer.</summary>
    [DataField]
    public string Solution = "tank";

    /// <summary>Units of water used for each plant topped up.</summary>
    [DataField]
    public float UsePerWatering = 1f;
}

/// <summary>Keeps birds off the outdoor crops within its range.</summary>
[RegisterComponent]
public sealed partial class WGScarecrowComponent : Component
{
    [DataField]
    public float Range = 8f;
}
