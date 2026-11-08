using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetPois;

/// <summary>What kind of place a site is, and how an outpost builder may take it over.</summary>
[Prototype("wfPoiType")]
public sealed partial class WFPoiTypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The kind's name, such as Mine.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>What it takes to claim the site from its console.</summary>
    [DataField]
    public WFPoiClaim Claim = WFPoiClaim.None;

    /// <summary>Whether a claim covers only the site on the ground, not the levels under it.</summary>
    [DataField]
    public bool SurfaceOnly;

    /// <summary>How the site's signal shows from orbit.</summary>
    [DataField]
    public WFPoiBeacon Beacon = WFPoiBeacon.Unknown;

    /// <summary>How close someone on the ground must come to identify it; the table's reveal range when unset.</summary>
    [DataField]
    public float? RevealRange;

    /// <summary>What every site of the kind gets once its world is ready, on its free floor nearest its middle.</summary>
    [DataField]
    public List<EntProtoId> Spawns = new();
}

/// <summary>
/// How a site's signal shows: unknown until someone reaches it, known by name from the start (it broadcasts), or faint,
/// off every radar until someone reaches it or a survey scanner picks it up.
/// </summary>
public enum WFPoiBeacon : byte
{
    Unknown,
    Known,
    Faint,
}

/// <summary>How a site is claimed: not at all, freely, once repaired, or once hacked.</summary>
public enum WFPoiClaim : byte
{
    None,
    Free,
    Repair,
    Hack,
}
