using Content.Shared._WG.ShipCommand;
using Robust.Shared.Audio;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A recovery pad: its console recalls a chosen person onto it from anywhere on the pad's map, alive, critical or dead,
/// after a short charge-up, then cools down. Admins only by default; the ship's owner (or an admin while admins hold the
/// console) can open it to officers or staff. Anyone alive and awake needs the operator's confirmation, and gets a
/// countdown before they go. Every recall is logged. Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WGRecoveryPadComponent : Component
{
    /// <summary>How long the pad charges before the person arrives.</summary>
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(5);

    /// <summary>How long after a recall the pad is ready again.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The lowest standing with the ship that may use the pad. Admins always may, and so does the owner of a commanded
    /// ship; off a commanded ship only admins may.
    /// </summary>
    [DataField]
    public WGShipAuthority Access = WGShipAuthority.Admin;

    /// <summary>How far from the pad, in metres, it reaches; null for anywhere on its map.</summary>
    [DataField]
    public float? Range;

    /// <summary>Played where the person was when they leave.</summary>
    [DataField]
    public SoundSpecifier? DepartSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    /// <summary>Played at the pad when they arrive.</summary>
    [DataField]
    public SoundSpecifier? ArriveSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    /// <summary>Who is being recalled, while the pad charges.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>Who started the recall.</summary>
    [ViewVariables]
    public EntityUid? Operator;

    /// <summary>When the charge finishes and the target arrives.</summary>
    [ViewVariables]
    public TimeSpan ChargeEnds;

    /// <summary>When the pad is ready again after its last recall.</summary>
    [ViewVariables]
    public TimeSpan ReadyAt;

    /// <summary>The target is alive and awake: they get a countdown.</summary>
    [ViewVariables]
    public bool Warn;

    /// <summary>The last countdown second shown to the target.</summary>
    [ViewVariables]
    public int Warned = -1;
}

/// <summary>A recovery pad's console. It works the nearest pad on its own grid within <see cref="PadRange"/>.</summary>
[RegisterComponent]
public sealed partial class WGRecoveryPadConsoleComponent : Component
{
    /// <summary>How far from the console, in metres, its pad may stand.</summary>
    [DataField]
    public float PadRange = 10f;
}
