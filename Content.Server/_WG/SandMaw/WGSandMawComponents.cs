using Content.Shared._WG.Tether;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.SandMaw;

/// <summary>
/// A maw in the sand. It lashes a tentacle round whoever comes near and reels them in, sucks in loose things, and
/// swallows what reaches its throat into a belly of flesh, made (with the loot of earlier meals) the first time it
/// eats. Slashing the tentacle frees its catch; breaking a belly wall tears a way back up.
/// </summary>
[RegisterComponent]
public sealed partial class WGSandMawComponent : Component
{
    /// <summary>Metres within which it lashes out.</summary>
    [DataField]
    public float GrabRange = 6f;

    [DataField]
    public TimeSpan GrabCooldown = TimeSpan.FromSeconds(4);

    /// <summary>Metres a second the tentacle reels in at first; it pulls harder the longer it holds.</summary>
    [DataField]
    public float ReelSpeed = 1f;

    [DataField]
    public float MaxReelSpeed = 3f;

    /// <summary>Speed left to a fresh catch, falling to <see cref="MinGrippedSpeed"/> the longer it's held.</summary>
    [DataField]
    public float GrippedSpeed = 0.65f;

    [DataField]
    public float MinGrippedSpeed = 0.1f;

    [DataField]
    public ProtoId<RopeTypePrototype> Tentacle = "WGSandMawTentacle";

    [DataField]
    public EntProtoId Grip = "WGSandMawTentacleGrip";

    /// <summary>Metres within which loose things slide in.</summary>
    [DataField]
    public float SuctionRadius = 3f;

    /// <summary>Pull on loose things at the throat, in m/s².</summary>
    [DataField]
    public float Pull = 4f;

    /// <summary>Metres from the centre at which it swallows.</summary>
    [DataField]
    public float SwallowRadius = 0.7f;

    /// <summary>Laid in the belly for whoever is swallowed if none is lying there already.</summary>
    [DataField]
    public EntProtoId EscapeTool = "Pickaxe";

    /// <summary>Floor radius of the belly, in tiles.</summary>
    [DataField]
    public int BellyRadius = 4;

    [DataField]
    public EntProtoId BellyWall = "WGSandMawBellyWall";

    /// <summary>The belly's floor tile.</summary>
    [DataField]
    public string BellyFloor = "FloorFlesh";

    [DataField]
    public EntProtoId Acid = "WGSandMawAcid";

    [DataField]
    public EntProtoId Glow = "WGSandMawGlow";

    /// <summary>What earlier meals left in the belly.</summary>
    [DataField]
    public EntityTableSelector? Loot;

    [DataField]
    public SoundSpecifier? SwallowSound = new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg");

    [DataField]
    public SoundSpecifier? LashSound = new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    /// <summary>The belly's grid: the layer under the maw, or a pocket map on a world without one.</summary>
    [ViewVariables]
    public EntityUid? Belly;

    [ViewVariables]
    public Vector2i BellyCentre;

    /// <summary>Whether the belly is a pocket map, left by a tear back up beside the maw.</summary>
    [ViewVariables]
    public bool PocketBelly;

    [ViewVariables]
    public TimeSpan NextGrab;

    /// <summary>Who it has hold of, with the rope and the coil on them.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, WGSandMawGrab> Grabs = new();
}

public sealed class WGSandMawGrab
{
    public EntityUid Rope;
    public EntityUid Grip;
    public TimeSpan Since;

    /// <summary>The tentacle's length now; the catch can't get further away than this.</summary>
    public float Length;

    /// <summary>Whether the victim got its rope point from the maw, to take away again.</summary>
    public bool AddedPoint;
}

/// <summary>On a creature in a maw's grip: it slows the longer it's held.</summary>
[RegisterComponent]
public sealed partial class WGSandMawGrabbedComponent : Component
{
    [ViewVariables]
    public float Speed = 1f;
}

/// <summary>The tentacle's coil round a victim; slash it apart to get free.</summary>
[RegisterComponent]
public sealed partial class WGSandMawGripComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;

    [ViewVariables]
    public EntityUid Victim;
}

/// <summary>A wall of a maw's belly; breaking it opens a way back to the surface.</summary>
[RegisterComponent]
public sealed partial class WGSandMawBellyWallComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;

    [DataField]
    public EntProtoId Exit = "WGSandMawExit";
}

/// <summary>A tear in a belly wall: whoever steps into it climbs out beside the maw.</summary>
[RegisterComponent]
public sealed partial class WGSandMawExitComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;
}
