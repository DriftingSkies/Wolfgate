using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server._WG.Pyramid;

/// <summary>
/// The Pyramid: a chrome war machine in the Coda that flies and fights like a ship. It hunts the nearest crewed ship,
/// paints patches of its hull and lasers them, shoves away anything that closes in, and sends out waves of warped space.
/// Every number here is tuned in its prototype, including how rarely it comes.
/// </summary>
[RegisterComponent]
public sealed partial class WGPyramidComponent : Component
{
    // Spawning.

    /// <summary>Chance a minute, per crewed ship in the Coda, that it comes; doubled at full depth.</summary>
    [DataField]
    public float ChancePerMinute = 0.003f;

    /// <summary>It never comes before the round has run this long.</summary>
    [DataField]
    public TimeSpan EarliestAt = TimeSpan.FromMinutes(45);

    /// <summary>At most this many come in a round.</summary>
    [DataField]
    public int PerRound = 1;

    /// <summary>How far from the ship it appears, in metres.</summary>
    [DataField]
    public float ArriveDistance = 650f;

    // Flying.

    /// <summary>Top speed, in metres a second.</summary>
    [DataField]
    public float Speed = 16f;

    /// <summary>How quickly it changes speed, in metres a second squared.</summary>
    [DataField]
    public float Acceleration = 4f;

    /// <summary>The distance it keeps from its target while it circles it, in metres.</summary>
    [DataField]
    public float StandOff = 90f;

    /// <summary>How far it will look for a ship to hunt, in metres.</summary>
    [DataField]
    public float SearchRange = 1500f;

    /// <summary>It sinks away after this long with nothing to hunt.</summary>
    [DataField]
    public TimeSpan LeaveAfter = TimeSpan.FromMinutes(10);

    /// <summary>Lasers it lands on its target before it has done enough there and breaks off from that grid for good.</summary>
    [DataField]
    public int BreakOffHits = 30;

    // The force bubble.

    /// <summary>Anything that comes this close to it is shoved away, in metres.</summary>
    [DataField]
    public float BubbleRadius = 35f;

    /// <summary>How hard the bubble shoves, as a change in speed a second.</summary>
    [DataField]
    public float BubblePush = 12f;

    // Barrages.

    /// <summary>It only fires on a ship this close, in metres.</summary>
    [DataField]
    public float FireRange = 320f;

    [DataField]
    public TimeSpan BarrageEvery = TimeSpan.FromSeconds(12);

    /// <summary>How long the marks glow before the lasers fire; matched to the charge sound.</summary>
    [DataField]
    public TimeSpan BarrageDelay = TimeSpan.FromSeconds(6);

    /// <summary>How many patches it marks, one of them on a crewmember.</summary>
    [DataField]
    public int Patches = 5;

    /// <summary>The explosion each laser sets off: shattered chrome and broken pixels, not fire.</summary>
    [DataField]
    public string ExplosionType = "WGPyramidShatter";

    [DataField]
    public float ExplosionIntensity = 22f;

    [DataField]
    public float ExplosionSlope = 6f;

    [DataField]
    public float ExplosionMaxTile = 7f;

    // Waves.

    [DataField]
    public TimeSpan WaveEvery = TimeSpan.FromSeconds(10);

    /// <summary>How far its waves reach, in metres.</summary>
    [DataField]
    public float WaveReach = 400f;

    [DataField]
    public float WaveSpeed = 110f;

    /// <summary>Drift a wave leaves in whoever it passes through.</summary>
    [DataField]
    public float WaveDrift = 1.5f;

    // Health.

    /// <summary>Damage it takes to bring it down.</summary>
    [DataField]
    public float Health = 9000f;

    /// <summary>Damage it mends a second once it has gone this long without being hit.</summary>
    [DataField]
    public float Regeneration = 6f;

    [DataField]
    public TimeSpan RegenerationAfter = TimeSpan.FromSeconds(20);

    /// <summary>How long an EMP stalls it.</summary>
    [DataField]
    public TimeSpan EmpStall = TimeSpan.FromSeconds(12);

    /// <summary>How long it shudders as it collapses before it folds away.</summary>
    [DataField]
    public TimeSpan CollapseTime = TimeSpan.FromSeconds(10);

    /// <summary>What it leaves behind, and how many.</summary>
    [DataField]
    public string Loot = "WGChromeShard";

    [DataField]
    public int LootCount = 3;

    // Sounds.

    /// <summary>Carried by every wave, heard as its front reaches each player.</summary>
    [DataField]
    public SoundSpecifier Ambience = new SoundPathSpecifier("/Audio/_WG/Pyramid/ambience.ogg");

    [DataField]
    public SoundSpecifier Charge = new SoundPathSpecifier("/Audio/_WG/Pyramid/laser_charge.ogg");

    [DataField]
    public SoundSpecifier Fire = new SoundPathSpecifier("/Audio/_WG/Pyramid/laser_fire.ogg");

    [DataField]
    public SoundSpecifier Collapse = new SoundPathSpecifier("/Audio/_WG/Pyramid/collapse.ogg");

    /// <summary>Played as it arrives.</summary>
    [DataField]
    public SoundSpecifier Arrive = new SoundPathSpecifier("/Audio/_WG/Fragments/seeing.ogg");

    // State.

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TimeSpan LastTarget;

    /// <summary>True when brought in by hand: it hunts any crewed ship, not just those in the Coda.</summary>
    [ViewVariables]
    public bool Anywhere;

    [ViewVariables]
    public TimeSpan NextBarrage;

    [ViewVariables]
    public TimeSpan NextWave;

    [ViewVariables]
    public TimeSpan LastHit;

    [ViewVariables]
    public TimeSpan StalledUntil;

    /// <summary>When it began to collapse; null while it fights.</summary>
    [ViewVariables]
    public TimeSpan? Collapsing;

    /// <summary>The marks of the barrage about to fire, and when it fires.</summary>
    [ViewVariables]
    public readonly List<EntityUid> Marks = new();

    [ViewVariables]
    public TimeSpan? FireAt;

    /// <summary>Seconds between one laser of a volley and the next, so their blasts don't all queue in one tick.</summary>
    [DataField]
    public float VolleyStep = 0.15f;

    /// <summary>Lasers of the current volley still to fire: where, and when.</summary>
    [ViewVariables]
    public readonly List<(EntityCoordinates At, TimeSpan When)> Volley = new();

    /// <summary>Lasers landed on each grid it has fired on.</summary>
    public readonly Dictionary<EntityUid, int> Landed = new();

    /// <summary>Grids it has broken off from; it never hunts them again.</summary>
    public readonly HashSet<EntityUid> Spared = new();

    [ViewVariables]
    public TimeSpan NextMend;

    /// <summary>Which way round its target it circles.</summary>
    [ViewVariables]
    public float Orbit = 1f;
}
