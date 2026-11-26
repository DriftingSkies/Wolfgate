using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.CodaSites;

/// <summary>A Coda site's grid, so each site is placed once.</summary>
[RegisterComponent]
public sealed partial class WGCodaSiteGridComponent : Component
{
    [DataField(required: true)]
    public string Site = string.Empty;
}

/// <summary>The Lighthouse: calls "safe harbour" on the common channel now and then.</summary>
[RegisterComponent]
public sealed partial class WGLighthouseComponent : Component
{
    [DataField]
    public TimeSpan BeaconEvery = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public TimeSpan NextBeacon;

    /// <summary>Players asleep aboard, so the keeper can greet them when they wake.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Sleepers = new();

    /// <summary>The sweeping lamp in its tower, lit when the site is first seen.</summary>
    [ViewVariables]
    public EntityUid? Lamp;

    [ViewVariables]
    public TimeSpan NextBlink;
}

/// <summary>The Lighthouse's keeper: polite, and wrong in small ways.</summary>
[RegisterComponent]
public sealed partial class WGLighthouseKeeperComponent : Component
{
    [DataField]
    public float Range = 7f;

    /// <summary>What the keeper gives each visitor once a round.</summary>
    [DataField]
    public Robust.Shared.Prototypes.EntProtoId Gift = "Flare";

    /// <summary>Drift his warm cup takes away.</summary>
    [DataField]
    public float Calm = 25f;

    [ViewVariables]
    public HashSet<Robust.Shared.Network.NetUserId> Given = new();

    [ViewVariables]
    public TimeSpan NextLine;
}

/// <summary>The Last Anchor: holds for a while, fails for a while, and presses Drift on everyone near while it fails.</summary>
[RegisterComponent]
public sealed partial class WGLastAnchorComponent : Component
{
    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromMinutes(3);

    [DataField]
    public TimeSpan FailTime = TimeSpan.FromMinutes(1);

    [DataField]
    public float Range = 2000f;

    /// <summary>Drift given every few seconds to each player in range while it fails.</summary>
    [DataField]
    public float Drift = 2f;

    /// <summary>Seconds of holding a power cell buys; exotic matter buys three times as much.</summary>
    [DataField]
    public float FeedSeconds = 60f;

    /// <summary>How far ahead feeding can push the next failure.</summary>
    [DataField]
    public TimeSpan MaxAhead = TimeSpan.FromMinutes(10);

    /// <summary>How long it holds after being fed back from failing.</summary>
    [ViewVariables]
    public TimeSpan? FedHold;

    [ViewVariables]
    public bool Holding = true;

    [ViewVariables]
    public TimeSpan NextSwitch;

    [ViewVariables]
    public TimeSpan NextDrift;
}

/// <summary>A Hospice cot: heals whoever lies on it, fully, for a price in Drift; the third visit brings the song.</summary>
[RegisterComponent]
public sealed partial class WGHospiceCotComponent : Component
{
    [DataField]
    public TimeSpan HealTime = TimeSpan.FromSeconds(10);

    [DataField]
    public float Drift = 25f;

    [DataField]
    public int SongOnVisit = 3;

    [ViewVariables]
    public EntityUid? Patient;

    [ViewVariables]
    public TimeSpan Since;

    [ViewVariables]
    public bool Treated;
}

/// <summary>A vine root in the Orchard, which grows vine towards anyone near.</summary>
[RegisterComponent]
public sealed partial class WGOrchardRootComponent : Component
{
    [DataField]
    public float Range = 12f;

    [DataField]
    public TimeSpan GrowEvery = TimeSpan.FromSeconds(20);

    [DataField]
    public int Cap = 120;

    [DataField]
    public EntProtoId Vine = "WGOrchardVine";

    [ViewVariables]
    public TimeSpan NextGrow;

    [ViewVariables]
    public List<EntityUid> Vines = new();
}

/// <summary>A vine. Cut, it drops the bodies hanging from it.</summary>
[RegisterComponent]
public sealed partial class WGOrchardVineComponent : Component;

/// <summary>A body hanging from the vines, which rises as Remade when its vine is cut.</summary>
[RegisterComponent]
public sealed partial class WGOrchardHangedComponent : Component
{
    [DataField]
    public List<EntProtoId> Rises = new();
}

/// <summary>A rock in the Graveyard Field carved with the name of someone who died this round.</summary>
[RegisterComponent]
public sealed partial class WGGraveRockComponent : Component
{
    /// <summary>Drift tending this grave takes away.</summary>
    [DataField]
    public float Calm = 10f;

    [DataField]
    public int Index;

    [DataField]
    public float Drift = 20f;
}

/// <summary>The Waiting Room's board: it calls a player's number and their Drift spikes.</summary>
[RegisterComponent]
public sealed partial class WGWaitingBoardComponent : Component
{
    [DataField]
    public TimeSpan CallEvery = TimeSpan.FromMinutes(3);

    [DataField]
    public float Drift = 25f;

    [ViewVariables]
    public TimeSpan NextCall;

    [ViewVariables]
    public int Number;

    /// <summary>Everyone who has been aboard and taken a ticket; only they are ever called.</summary>
    [ViewVariables]
    public HashSet<NetUserId> Ticketed = new();
}

/// <summary>The Broadcast Tower's transmitter: it replays the round's radio late, altered, on the common channel.</summary>
[RegisterComponent]
public sealed partial class WGBroadcastTowerComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromMinutes(20);

    [ViewVariables]
    public int Replayed;
}

/// <summary>The Echo Station: Central, loaded again in the Coda and decayed once it is placed.</summary>
[RegisterComponent]
public sealed partial class WGEchoStationComponent : Component
{
    [DataField]
    public float DeadBulbs = 0.75f;

    [DataField]
    public int FleshKnots = 6;

    [DataField]
    public float Debris = 0.02f;

    [DataField]
    public int Echoes = 12;

    [DataField]
    public int Boards = 3;

    /// <summary>Drift given every five seconds to each player aboard.</summary>
    [DataField]
    public float Drift = 2f;

    /// <summary>The Echo's own sound, played to everyone aboard on a loop. Unset until the ambience is in.</summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? Ambience;

    [DataField]
    public TimeSpan AmbienceEvery = TimeSpan.FromSeconds(60);

    [ViewVariables]
    public bool Decayed;

    [ViewVariables]
    public List<EntityUid> Flicker = new();

    [ViewVariables]
    public TimeSpan NextDrift;

    [ViewVariables]
    public TimeSpan NextAmbience;
}

/// <summary>One of the Echo's figures, going through the motions of a shift years gone.</summary>
[RegisterComponent]
public sealed partial class WGEchoFigureComponent : Component
{
    [DataField]
    public float Range = 8f;

    [ViewVariables]
    public TimeSpan NextLine;
}

/// <summary>A noticeboard in the Echo, carrying this round's dead as news from long ago.</summary>
[RegisterComponent]
public sealed partial class WGEchoNewsBoardComponent : Component;

/// <summary>The Echo's dead anchor, which twitches whenever a real anchor falls.</summary>
[RegisterComponent]
public sealed partial class WGEchoAnchorComponent : Component
{
    [DataField]
    public float Drift = 5f;
}

/// <summary>Something from the Echo. Taken off its grid, it crumbles to dust after a while.</summary>
[RegisterComponent]
public sealed partial class WGEchoItemComponent : Component
{
    [DataField]
    public TimeSpan CrumbleAfter = TimeSpan.FromSeconds(30);

    [ViewVariables]
    public EntityUid Home;

    [ViewVariables]
    public TimeSpan? LeftAt;
}

/// <summary>A radio message the tower heard: who, what, and when.</summary>
public sealed record WGHeardMessage(string Speaker, string Message, TimeSpan When, NetUserId? User);

/// <summary>A Hospice attendant, who speaks to the healed.</summary>
[RegisterComponent]
public sealed partial class WGHospiceAttendantComponent : Component;

/// <summary>Someone a Hospice cot has healed: it shows, a little.</summary>
[RegisterComponent]
public sealed partial class WGHospiceMarkComponent : Component;
