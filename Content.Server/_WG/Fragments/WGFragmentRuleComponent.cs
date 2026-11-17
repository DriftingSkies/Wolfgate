using Content.Shared._Mono.Company;
using Content.Shared.Tag;
using Robust.Shared.Map;
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

    /// <summary>Share added to every faction's reward when members of each were aboard before the heart broke.</summary>
    [DataField]
    public float JointBonus = 0.5f;

    /// <summary>The fragment keeps at least this far from any faction base.</summary>
    [DataField]
    public float BaseClearance = 2500f;

    /// <summary>The furthest it lies from either of the two bases.</summary>
    [DataField]
    public float MaxDistance = 6000f;

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

    /// <summary>Players within this many metres of the heart wake it.</summary>
    [DataField]
    public float WakeRadius = 40f;

    /// <summary>Waves stop while this many of the fragment's Remade are alive.</summary>
    [DataField]
    public int MaxAlive = 16;

    /// <summary>Remade decor scattered over the fragment.</summary>
    [DataField]
    public List<EntProtoId> Decor = new();

    [DataField]
    public int DecorCount = 14;

    public EntityUid? Grid;
    public EntityUid? HeartUid;
    public MapCoordinates Site = MapCoordinates.Nullspace;
    public TimeSpan NextWave;
    public TimeSpan NextCheck;
    public int Wave;
    public bool Cleared;
    public readonly HashSet<EntityUid> Remade = new();
    public readonly HashSet<ProtoId<CompanyPrototype>> Present = new();
}
