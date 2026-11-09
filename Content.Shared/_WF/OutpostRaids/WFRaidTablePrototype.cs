using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.OutpostRaids;

/// <summary>A step on the raid ladder and what a raid of it costs the director in threat points.</summary>
[Prototype("wfRaidTier")]
public sealed partial class WFRaidTierPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The ladder step: an outpost only gets raids up to the step after the last one it beat.</summary>
    [DataField(required: true)]
    public int Step;

    /// <summary>Threat points a raid of this tier spends.</summary>
    [DataField]
    public float Cost;
}

/// <summary>What raids a world sends, tier by tier. One with no surfaces is the default for every world without its own.</summary>
[Prototype("wfRaidTable")]
public sealed partial class WFRaidTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<ProtoId<WFPlanetSurfacePrototype>> Surfaces = new();

    [DataField(required: true)]
    public List<WFRaidPack> Packs = new();
}

/// <summary>One raid a table can send: its tier and what walks in.</summary>
[DataDefinition]
public sealed partial class WFRaidPack
{
    [DataField(required: true)]
    public ProtoId<WFRaidTierPrototype> Tier;

    [DataField]
    public float Weight = 1f;

    [DataField(required: true)]
    public List<WFRaidSpawn> Spawns = new();

    /// <summary>What the raid rode in on, left standing where it walked in until dawn.</summary>
    [DataField]
    public EntProtoId? Arrival;

    /// <summary>A shuttle grid the raid sets down in, outside the claim beside where it walks in; it leaves at dawn.</summary>
    [DataField]
    public ResPath? Shuttle;

    /// <summary>
    /// Whether the raid tunnels in from the cavern and comes up just outside the claim, rather than walking in from
    /// afar. On a world with no cavern it walks in.
    /// </summary>
    [DataField]
    public bool Tunnel;

    /// <summary>
    /// Where the raid comes from. A raid with a source is only sent while that source stands, so removing it ends
    /// those raids: see <see cref="WFRaidSource"/>.
    /// </summary>
    [DataField]
    public WFRaidSource Source = WFRaidSource.None;

    /// <summary>Who the early warning names, in place of plain raiders.</summary>
    [DataField]
    public LocId? Name;

    /// <summary>How many times the spawns are sent: more than one is a siege.</summary>
    [DataField]
    public int Waves = 1;

    /// <summary>Time between a siege's waves.</summary>
    [DataField]
    public TimeSpan WaveInterval = TimeSpan.FromSeconds(150);
}

/// <summary>Where a raid pack comes from, and so what has to stand for it to be sent.</summary>
public enum WFRaidSource : byte
{
    /// <summary>The world's own wildlife and tunnels: always there.</summary>
    None,

    /// <summary>A hostile camp or hideout on the world, with someone left alive in it. The raid walks in from its side.</summary>
    Camp,

    /// <summary>The Ashfall Reavers, while their campaign has a stronghold within reach of the world.</summary>
    Reavers,

    /// <summary>A gang from off-world, only in a round where no Reaver campaign runs.</summary>
    Drifters,
}

/// <summary>A kind of mob in a raid pack and how many of it.</summary>
[DataDefinition]
public sealed partial class WFRaidSpawn
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;
}
