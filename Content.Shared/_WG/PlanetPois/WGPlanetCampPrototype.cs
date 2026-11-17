using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.NpcCrew;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PlanetPois;

/// <summary>
/// An armed camp round a ship set down on a planet: a Reaver encampment, an ADS control site. Its crew stand guard on
/// the deck and round the hull, and fight anyone their faction counts hostile.
/// </summary>
[Prototype("wgPlanetCamp")]
public sealed partial class WGPlanetCampPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What the site's signal is called once found.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The ships it may be built round, one picked per camp; none makes a camp in the open, round its fire.</summary>
    [DataField]
    public List<ProtoId<VesselPrototype>> Vessels = new();

    /// <summary>Bodies, names and loadouts of the guards; none posts no guards.</summary>
    [DataField]
    public ProtoId<WGCrewProfilePrototype>? Profile;

    [DataField]
    public ProtoId<NpcFactionPrototype> Faction = "SimpleNeutral";

    /// <summary>
    /// Whether the camp is pitched beside a road, just off the shoulder on a straight stretch and facing it, where
    /// whoever drives by sees it; out in the open instead when the world has no roads or no stretch has room.
    /// </summary>
    [DataField]
    public bool Roadside = true;

    /// <summary>Whoever lives there instead of, or as well as, a crew: one pick each, set round the fire.</summary>
    [DataField]
    public EntityTableSelector? Residents;

    [DataField]
    public int MinResidents;

    [DataField]
    public int MaxResidents;

    /// <summary>Marks the camp as its residents' home, which they guard; sized to the camp when it's set down.</summary>
    [DataField]
    public EntProtoId? Home;

    /// <summary>The guards' role: its duty and engagement, and the profile's loadout for it.</summary>
    [DataField]
    public ProtoId<WGCrewRolePrototype> Role = "WGCrewMarine";

    [DataField]
    public WGCrewSkill Skill = WGCrewSkill.Regular;

    /// <summary>Guards aboard, on the deck.</summary>
    [DataField]
    public int MinDeckGuards = 2;

    [DataField]
    public int MaxDeckGuards = 3;

    /// <summary>Guards outside, round the hull.</summary>
    [DataField]
    public int MinGroundGuards = 3;

    [DataField]
    public int MaxGroundGuards = 5;

    /// <summary>Cover set out in a ring round the ship, such as sandbags.</summary>
    [DataField]
    public List<EntProtoId> Cover = new();

    [DataField]
    public int MinCover = 4;

    [DataField]
    public int MaxCover = 8;

    /// <summary>The camp itself, set out round the hull: tents, fires, lights.</summary>
    [DataField]
    public List<WGPlanetCampProp> Props = new();

    /// <summary>What's stowed aboard; one pick.</summary>
    [DataField]
    public EntityTableSelector? Loot;
}

/// <summary>One kind of thing set out round a camp: a pick from <see cref="Ids"/>, some number of times.</summary>
[DataDefinition]
public sealed partial class WGPlanetCampProp
{
    [DataField(required: true)]
    public List<EntProtoId> Ids = new();

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;

    /// <summary>Tiles out from the hull's edge.</summary>
    [DataField]
    public float Distance = 4f;

    /// <summary>The ground it covers round where it's put, as left, bottom, right, top; a single tile when unset.</summary>
    [DataField]
    public Box2? Footprint;
}

/// <summary>How many camps of one kind a world gets.</summary>
[DataDefinition]
public sealed partial class WGPlanetCampEntry
{
    [DataField(required: true)]
    public ProtoId<WGPlanetCampPrototype> Camp;

    [DataField]
    public int MinCount;

    [DataField]
    public int MaxCount = 1;
}
