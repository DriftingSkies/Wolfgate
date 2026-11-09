using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lattice;

/// <summary>Something only one player sees or hears out past the Lattice's hold.</summary>
[Serializable, NetSerializable]
public enum WFLatticeHallucinationKind : byte
{
    /// <summary>A line in their head, as a popup.</summary>
    Whisper,

    /// <summary>A voice on the common channel that nobody sent.</summary>
    Radio,

    /// <summary>A ship hailing them that isn't there.</summary>
    Hail,

    /// <summary>A radar contact that isn't there.</summary>
    Blip,

    /// <summary>Footsteps, knocks or breathing close by.</summary>
    Sound,

    /// <summary>A figure seen for a moment.</summary>
    Glimpse,

    /// <summary>A Remade standing there until they come close.</summary>
    Phantom,

    /// <summary>A bad turn: vision narrows and throbs for a few seconds, glyphs crawl round its edge, and the bass hits.</summary>
    Pulse,

    /// <summary>Blood spattered round them that isn't there.</summary>
    Bleed,

    /// <summary>Someone near them looks like a Remade for a few seconds. The coordinates are on that person.</summary>
    Turn,

    /// <summary>They see themselves as a Remade for a few seconds.</summary>
    Mirror,

    /// <summary>Remade all round them, for a moment.</summary>
    Swarm,

    /// <summary>Someone near them says something they never said.</summary>
    Mimic,

    /// <summary>Something near them, a door, a wall or a person, isn't there for a few seconds. The coordinates are on it.</summary>
    Vanish,

    /// <summary>Their own ship shows on the radar a second time, somewhere close.</summary>
    Double,

    /// <summary>A warning on their screen that isn't true: low oxygen, pressure or heat.</summary>
    FalseAlert,

    /// <summary>Sounds round them play again a moment later, from the wrong side.</summary>
    Echo,

    /// <summary>Every sound stops for a while, then everything they missed plays at once.</summary>
    Silence,

    /// <summary>They just know where something is out in the dark. Sometimes they're right.</summary>
    Knowing,
}

/// <summary>Tells one client to show a hallucination at a place.</summary>
[Serializable, NetSerializable]
public sealed class WFLatticeHallucinationEvent(WFLatticeHallucinationKind kind, NetCoordinates coordinates, float seconds, int seed) : EntityEventArgs
{
    public readonly WFLatticeHallucinationKind Kind = kind;
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly float Seconds = seconds;
    public readonly int Seed = seed;
}

/// <summary>One kind of hallucination a profile can roll, and how much Drift it needs.</summary>
[DataDefinition]
public sealed partial class WFLatticeEffect
{
    [DataField(required: true)]
    public WFLatticeHallucinationKind Kind;

    /// <summary>Least Drift before it can happen.</summary>
    [DataField]
    public float MinDrift;

    [DataField]
    public float Weight = 1f;
}
