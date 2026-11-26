using Content.Shared._Mono.Company;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.Fragments;

/// <summary>
/// A Monolith fragment drifting in between the faction bases: a heart that sends Remade at whoever comes near, and pays
/// every listed faction when it breaks, more if both were aboard. It never holds a Scion.
/// </summary>
[RegisterComponent, Access(typeof(WGFragmentRuleSystem))]
public sealed partial class WGFragmentRuleComponent : Component
{
    /// <summary>The grids the fragment picks from.</summary>
    [DataField(required: true)]
    public List<ResPath> Grids = new();

    /// <summary>The two factions whose bases it drifts between, and that it pays.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<CompanyPrototype>, int> Rewards = new();

    /// <summary>Tags on each faction's static uplink; the fragment lies about as far from every one of them.</summary>
    [DataField(required: true)]
    public List<ProtoId<TagPrototype>> Bases = new();

    /// <summary>Thalers deposited, untaxed, to every player who took part when the heart breaks, whatever their faction.</summary>
    [DataField]
    public int ParticipantReward;

    /// <summary>Share added to every faction's reward when members of each were aboard before the heart broke.</summary>
    [DataField]
    public float JointBonus = 0.5f;

    /// <summary>The fragment keeps at least this far from any faction base.</summary>
    [DataField]
    public float BaseClearance = 2500f;

    /// <summary>How far past that clearance it may drift sideways off the line halfway between the two bases.</summary>
    [DataField]
    public float MaxOffset = 2000f;

    /// <summary>Announced as the event starts, before the fragment is placed.</summary>
    [DataField(required: true)]
    public WGFragmentLine Sighted = default!;

    /// <summary>Announced when the fragment lands.</summary>
    [DataField(required: true)]
    public WGFragmentLine Arrived = default!;

    /// <summary>Announced when the station event's end warning comes due.</summary>
    [DataField(required: true)]
    public WGFragmentLine Warning = default!;

    /// <summary>Announced when its heart breaks.</summary>
    [DataField(required: true)]
    public WGFragmentLine Broken = default!;

    /// <summary>Announced when it leaves with its heart still beating.</summary>
    [DataField(required: true)]
    public WGFragmentLine Lost = default!;

    /// <summary>Announced when it leaves after its heart was broken.</summary>
    [DataField(required: true)]
    public WGFragmentLine Departed = default!;

    /// <summary>Space-separated words the corruption may spell out.</summary>
    [DataField]
    public LocId HiddenWords = "wg-fragment-hidden-words";

    /// <summary>Colour of the whisper that follows the worst lines.</summary>
    [DataField]
    public Color VoiceColor = Color.FromHex("#A05CC8");

    /// <summary>Milliseconds between redraws of a flickering burst.</summary>
    [DataField]
    public int FlickerRate = 90;

    /// <summary>The entity whose breaking clears the fragment.</summary>
    [DataField]
    public EntProtoId Heart = "WGFragmentHeart";

    /// <summary>Mobs mapped on the fragment swapped for one of these Remade.</summary>
    [DataField]
    public Dictionary<EntProtoId, List<EntProtoId>> Replace = new();

    /// <summary>Remade a wave draws from.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Common = new();

    /// <summary>One of these joins every <see cref="EliteEvery"/>th wave.</summary>
    [DataField]
    public List<EntProtoId> Elites = new();

    [DataField]
    public int EliteEvery = 3;

    /// <summary>Time between waves while anyone is near the heart.</summary>
    [DataField]
    public TimeSpan WaveInterval = TimeSpan.FromSeconds(90);

    /// <summary>A wave's size before players are counted.</summary>
    [DataField]
    public int WaveBase = 2;

    /// <summary>One more Remade per this many players near the heart.</summary>
    [DataField]
    public int PlayersPerExtra = 3;

    /// <summary>Living players within this many metres of the heart wake it, aboard the fragment or a ship alongside.</summary>
    [DataField]
    public float WakeRadius = 40f;

    /// <summary>
    /// Waves also spawn within this many metres of the heart on grids a living player near the heart is aboard, such as
    /// a crewed ship alongside.
    /// </summary>
    [DataField]
    public float SpawnRadius = 40f;

    /// <summary>Share of a wave sent to those nearby grids when any are in range; the rest lands on the fragment.</summary>
    [DataField]
    public float NearbyShare = 0.4f;

    /// <summary>Remade placed on the fragment as it lands, beside those swapped in for its mapped mobs.</summary>
    [DataField]
    public int Garrison;

    /// <summary>Elites among the landing garrison.</summary>
    [DataField]
    public int GarrisonElites;

    /// <summary>Waves stop while this many of the fragment's Remade are alive.</summary>
    [DataField]
    public int MaxAlive = 16;

    /// <summary>Remade decor scattered over the fragment.</summary>
    [DataField]
    public List<EntProtoId> Decor = new();

    [DataField]
    public int DecorCount = 14;

    /// <summary>Scattered over the fragment besides the decor; set by a size.</summary>
    [DataField]
    public List<EntProtoId> Scatter = new();

    [DataField]
    public int ScatterCount;

    /// <summary>Larger kinds of fragment, each with its own grids; a grid not in any of them is the default size.</summary>
    [DataField]
    public List<WGFragmentSize> Sizes = new();

    /// <summary>The fragment's name on scanners.</summary>
    [DataField]
    public LocId GridName = "wg-fragment-grid-name";

    /// <summary>The grid picked as the event was added, so every announcement knows its size.</summary>
    public ResPath? Chosen;

    public EntityUid? Grid;
    public EntityUid? HeartUid;
    public MapCoordinates Site = MapCoordinates.Nullspace;
    public TimeSpan NextWave;
    public TimeSpan NextCheck;
    public int Wave;
    public bool Cleared;
    public bool WarningSent;
    /// <summary>Set when the heart breaks; <see cref="Present"/> stops taking new factions from then on.</summary>
    public bool HeartBroken;
    public readonly HashSet<EntityUid> Remade = new();
    public readonly HashSet<ProtoId<CompanyPrototype>> Present = new();
    /// <summary>
    /// Players who were aboard or near the heart while it stood, with the character slot they first took part as;
    /// frozen with <see cref="Present"/>.
    /// </summary>
    public readonly Dictionary<NetUserId, int> Participants = new();
}
