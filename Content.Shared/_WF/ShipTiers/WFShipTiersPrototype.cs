using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipTiers;

/// <summary>The capital rules for the factions that fight over the sector: how many capitals each may field, how long one takes to build and how long a loss locks its slot.</summary>
[Prototype("wfShipTiers")]
public sealed partial class WFShipTiersPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The companies the cap applies to; others buy capitals as before.</summary>
    [DataField(required: true)]
    public List<ProtoId<CompanyPrototype>> Companies = new();

    /// <summary>The vessels that count as capitals.</summary>
    [DataField(required: true)]
    public List<ProtoId<VesselPrototype>> Capitals = new();

    /// <summary>Capital slots by faction members online: the highest entry whose <see cref="WFCapitalThreshold.Members"/> is met applies.</summary>
    [DataField(required: true)]
    public List<WFCapitalThreshold> Thresholds = new();

    /// <summary>How long a new capital can't fly after it is bought.</summary>
    [DataField]
    public TimeSpan BuildTime = TimeSpan.FromMinutes(10);

    /// <summary>How long a lost capital keeps its slot.</summary>
    [DataField]
    public TimeSpan Lockout = TimeSpan.FromMinutes(45);
}

/// <summary>A faction with at least <see cref="Members"/> members online may field <see cref="Slots"/> capitals.</summary>
[DataDefinition]
public partial record struct WFCapitalThreshold
{
    [DataField(required: true)]
    public int Members;

    [DataField(required: true)]
    public int Slots;
}
