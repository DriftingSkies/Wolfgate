using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Contracts;

/// <summary>Where a planetside objective stands: a free site on one of the sector's worlds.</summary>
[DataDefinition, Serializable]
public sealed partial class WFContractPlanet
{
    /// <summary>Worlds it may be posted on; empty for any world with a fauna pool. Carcinoma is never one.</summary>
    [DataField]
    public List<ProtoId<WFPlanetSurfacePrototype>> Worlds = new();

    /// <summary>Native hostiles placed on the site, from the world's pool by the contract's tier.</summary>
    [DataField]
    public Vector2i Hostiles = new(3, 5);

    /// <summary>Native hostiles in each wave of a download, from the same pool.</summary>
    [DataField]
    public Vector2i WaveHostiles = new(2, 3);
}

/// <summary>A world's hostile wildlife for planetside contracts, by how dangerous it is.</summary>
[Prototype("wfContractWorldFauna")]
public sealed partial class WFContractWorldFaunaPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> World;

    /// <summary>Common hostiles, the bulk of any site.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Light = new();

    /// <summary>Tougher hostiles, mixed in from tier 2.</summary>
    [DataField]
    public List<EntProtoId> Heavy = new();

    /// <summary>One of these joins a tier 4 or 5 site.</summary>
    [DataField]
    public List<EntProtoId> Boss = new();
}

/// <summary>Carry kits from the board to a world's site and set them down there, where each unfolds into its structure.</summary>
public sealed partial class WFContractConstruct : WFContractObjective
{
    /// <summary>Kits handed out at the board; all must be set down on the site.</summary>
    [DataField(required: true)]
    public List<WFContractSpawn> Kits = new();
}
