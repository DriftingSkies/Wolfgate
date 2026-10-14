using Content.Shared.Procedural;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Salvage;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>On a ground layer: every point of interest placed on it this round.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetPoiComponent : Component
{
    [ViewVariables]
    public List<WFPlanetPoiSite> Sites = new();

    [ViewVariables]
    public bool NetworkReady;

    [ViewVariables]
    public string? GenerationError;

    [ViewVariables]
    public HashSet<Vector2i> RoadTiles = new();

    [ViewVariables]
    public HashSet<Vector2i> ShoulderTiles = new();

    /// <summary>The tiles down the middle of the roads, and everything the roads were furnished with.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Centreline = new();

    [ViewVariables]
    public List<EntityUid> Furniture = new();
}

/// <summary>One placed site: what was generated, where, how big it came out and the signal that marks it.</summary>
public sealed class WFPlanetPoiSite
{
    public ProtoId<DungeonConfigPrototype>? Dungeon;
    public string? MapPath;
    public WFPlanetPoiConnection Connection;
    public List<WFPlanetRoadSocket> Sockets = new();
    public Dictionary<WFPlanetRoadSocket, List<Vector2i>> AccessPaths = new();
    public LocId Name;
    /// <summary>Where the generator was pointed; the site spreads round it.</summary>
    public Vector2i Origin;

    /// <summary>The generated tile count and reserved bounds with exclusive upper edges.</summary>
    public int TileCount;
    public Box2i Bounds;
    public EntityUid Signal;

    /// <summary>Whether an expedition console may offer the site, and at what rating.</summary>
    public bool Expeditions = true;
    public DifficultyRating Difficulty = DifficultyRating.Moderate;

    /// <summary>The station running an expedition on the site, and whether one has already cleared it this round.</summary>
    public EntityUid? ExpeditionClaim;
    public bool Cleared;
}

/// <summary>
/// On an orbit-layer marker over a site: an unidentified signal until someone on the ground gets close, then the
/// site's name.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPoiSignalComponent : Component
{
    [ViewVariables]
    public EntityUid Ground;

    [ViewVariables]
    public Vector2i Origin;

    [ViewVariables]
    public LocId Name;

    [ViewVariables]
    public float RevealRange = 48f;

    [ViewVariables]
    public bool Revealed;
}
