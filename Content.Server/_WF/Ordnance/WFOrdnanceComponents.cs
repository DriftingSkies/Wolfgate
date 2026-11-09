using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Ordnance;

/// <summary>On a faction station's grid: the ordnance terminal it gets, placed once the station is built.</summary>
[RegisterComponent]
public sealed partial class WFOrdnanceStationComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Terminal;

    [ViewVariables]
    public bool Placed;
}

/// <summary>A faction's ordnance terminal: whose name a purchase is announced under.</summary>
[RegisterComponent]
public sealed partial class WFOrdnanceTerminalComponent : Component
{
    [DataField(required: true)]
    public LocId Faction;
}

/// <summary>How a delivered warhead is fuzed.</summary>
public enum WFOrdnanceFuze : byte
{
    /// <summary>Timer only.</summary>
    Timer,

    /// <summary>Remote only, with a dead man's switch linked to it.</summary>
    Remote,

    /// <summary>Both, with the switch.</summary>
    Both,
}

/// <summary>
/// A nuclear delivery beacon. Set up with verbs (fuzing, timer, how long the drop takes), then used in hand on a grid's
/// floor or a planet's ground: it is set down, counts down and a drop pod lands on it with the warhead and its key.
/// </summary>
[RegisterComponent]
public sealed partial class WFOrdnanceBeaconComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Warhead;

    [DataField]
    public EntProtoId Key = "WFNukeKey";

    [DataField]
    public EntProtoId Switch = "WFDeadManSwitch";

    [DataField]
    public WFOrdnanceFuze Fuze = WFOrdnanceFuze.Timer;

    /// <summary>The warhead's timer on arrival, in seconds.</summary>
    [DataField]
    public float Timer = 120f;

    [DataField]
    public List<float> TimerOptions = new() { 30f, 60f, 120f, 300f, 600f };

    /// <summary>Seconds from planting to touchdown.</summary>
    [DataField]
    public float Delay = 60f;

    [DataField]
    public List<float> DelayOptions = new() { 30f, 60f, 120f, 300f };

    /// <summary>How long before touchdown the pod is heard coming.</summary>
    [DataField]
    public float RumbleLead = 8f;

    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier RumbleSound = new SoundPathSpecifier("/Audio/_WF/Planets/Flight/fall_rumble.ogg");

    [DataField]
    public SoundSpecifier LandSound = new SoundPathSpecifier("/Audio/_WF/Planets/Flight/hard_landing.ogg");

    [DataField]
    public EntProtoId LandEffect = "CEDustEffect";

    /// <summary>The faction that bought it, for the record.</summary>
    [ViewVariables]
    public string? Faction;

    [ViewVariables]
    public TimeSpan? LandsAt;

    [ViewVariables]
    public TimeSpan NextBeep;

    [ViewVariables]
    public bool Rumbled;

    [ViewVariables]
    public EntityUid? PlantedBy;
}
