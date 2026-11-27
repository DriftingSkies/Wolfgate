namespace Content.Server._WG.NpcCrew.Components;

/// <summary>What a crewman on a break is after.</summary>
public enum WGCrewBreakKind : byte
{
    Snack,
    Drink,
    Rest,
}

/// <summary>Where a break has got to.</summary>
public enum WGCrewBreakStage : byte
{
    None,

    /// <summary>Walking to a vending machine.</summary>
    ToVendor,

    /// <summary>At the machine, waiting for what he bought to drop.</summary>
    Collect,

    /// <summary>Walking to a free seat.</summary>
    ToSeat,

    /// <summary>Sitting (or standing, with no seat free), eating, drinking or resting.</summary>
    Seated,

    /// <summary>Taking the wrapper or the empty to a disposal unit.</summary>
    ToBin,
}

/// <summary>
/// A crewman who takes breaks between duties: a snack or a drink from a vending machine aboard, eaten in a free seat,
/// or just a sit-down, and the rubbish in a disposal unit after. Only at Code Green with nothing else to do.
/// <see cref="Systems.WGCrewDowntimeSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewDowntimeComponent : Component
{
    /// <summary>Shortest time between breaks.</summary>
    [DataField]
    public TimeSpan MinGap = TimeSpan.FromMinutes(8);

    /// <summary>Longest time between breaks.</summary>
    [DataField]
    public TimeSpan MaxGap = TimeSpan.FromMinutes(16);

    /// <summary>Shortest sit-down.</summary>
    [DataField]
    public TimeSpan MinRest = TimeSpan.FromSeconds(40);

    /// <summary>Longest sit-down.</summary>
    [DataField]
    public TimeSpan MaxRest = TimeSpan.FromSeconds(120);

    /// <summary>A break that has gone on this long is over, wherever it got to.</summary>
    [DataField]
    public TimeSpan MaxBreak = TimeSpan.FromMinutes(5);

    /// <summary>How far, in metres, he goes for a machine, a seat or a bin.</summary>
    [DataField]
    public float Range = 40f;

    [ViewVariables]
    public WGCrewBreakStage Stage;

    [ViewVariables]
    public WGCrewBreakKind Kind;

    /// <summary>The machine, seat or bin he is headed for.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The seat he means to sit in once he has his snack.</summary>
    [ViewVariables]
    public EntityUid? Seat;

    /// <summary>What he bought, then holds.</summary>
    [ViewVariables]
    public EntityUid? Item;

    /// <summary>The prototype he bought, to find it once it drops.</summary>
    [ViewVariables]
    public string? Vended;

    /// <summary>Bites or sips taken.</summary>
    [ViewVariables]
    public int Uses;

    [ViewVariables]
    public TimeSpan NextBreak;

    [ViewVariables]
    public TimeSpan Started;

    /// <summary>When the sit-down ends.</summary>
    [ViewVariables]
    public TimeSpan Until;

    /// <summary>When he next takes a bite or a sip, or looks for what dropped.</summary>
    [ViewVariables]
    public TimeSpan NextUse;
}
