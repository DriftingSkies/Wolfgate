using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WF.ShipMissiles;

/// <summary>
/// Flies a ship missile on proportional navigation with a finite divert budget. The missile also carries a
/// <see cref="Content.Server._Mono.Projectiles.TargetSeeking.TargetSeekingComponent"/> that only seeks, so lock
/// alerts, radar arcs and flares keep working.
/// </summary>
[RegisterComponent]
public sealed partial class WFMissileFlightComponent : Component
{
    /// <summary>Speed added along the nose on the first tick, in m/s.</summary>
    [DataField]
    public float LaunchSpeed = 90f;

    /// <summary>Speed cap, in m/s.</summary>
    [DataField]
    public float MaxSpeed = 240f;

    /// <summary>Motor thrust while boosting, in m/s².</summary>
    [DataField]
    public float BoostAccel = 200f;

    /// <summary>Seconds of motor burn.</summary>
    [DataField]
    public float BoostTime = 3f;

    /// <summary>Most lateral acceleration the diverters can hold, in m/s².</summary>
    [DataField]
    public float LateralAccel = 110f;

    /// <summary>Proportional-navigation constant.</summary>
    [DataField]
    public float NavigationConstant = 4f;

    /// <summary>Total divert impulse, as the velocity change it can buy in m/s.</summary>
    [DataField]
    public float DivertBudget = 120f;

    /// <summary>Most impulse the diverters can spend in one second, in m/s².</summary>
    [DataField]
    public float DivertCap = 60f;

    /// <summary>Share of the divert price paid before the terminal phase.</summary>
    [DataField]
    public float MidcourseDrain = 0.15f;

    /// <summary>Closure rate around zero, in m/s, that counts as beaming.</summary>
    [DataField]
    public float NotchBand = 4f;

    /// <summary>Seconds of beaming that break the lock.</summary>
    [DataField]
    public float NotchMemory = 1.5f;

    /// <summary>Seconds the seeker flies on its last good track after losing the lock.</summary>
    [DataField]
    public float MemoryTime = 2f;

    /// <summary>Share of the launch time to go that counts as the terminal phase.</summary>
    [DataField]
    public float TerminalFraction = 0.15f;

    /// <summary>Shortest terminal phase, in seconds.</summary>
    [DataField]
    public float MinTerminal = 1.5f;

    /// <summary>Seconds of opening flight, with the diverters dry, before the missile is a dud.</summary>
    [DataField]
    public float DudOpeningTime = 0.4f;

    /// <summary>Seconds after release the missile steers on free divert authority while it stabilises.</summary>
    [DataField]
    public float LaunchTransient = 0.35f;

    /// <summary>Turn authority multiplier at the start of the launch transient, easing to one by its end.</summary>
    [DataField]
    public float LaunchTurnMultiplier = 2.5f;

    /// <summary>Share of the proportional-navigation command applied at release, easing to one by its end.</summary>
    [DataField]
    public float LaunchDamping = 0.15f;

    /// <summary>Half-angle, in degrees, the seeker may lock within during the launch transient.</summary>
    [DataField]
    public float LaunchAcquireCone = 45f;

    /// <summary>Range, in metres, the seeker may lock within during the launch transient.</summary>
    [DataField]
    public float LaunchAcquireRange = 1600f;

    /// <summary>Half-angle, in degrees, off the nose the missile may be handed at release.</summary>
    [DataField]
    public float LaunchOffBoresight = 90f;

    /// <summary>How far the release impulse is turned onto its bearing, one snapping the whole velocity over.</summary>
    [DataField]
    public float SnapFraction;

    /// <summary>Whether the launch impulse has been applied.</summary>
    public bool Launched;

    /// <summary>Seconds of motor burn left.</summary>
    public float BoostRemaining;

    /// <summary>Divert impulse left, as a velocity change in m/s.</summary>
    public float Divert;

    /// <summary>Divert impulse the missile launched with.</summary>
    public float Divert0;

    /// <summary>Seconds the target has sat in the notch.</summary>
    public float NotchTimer;

    /// <summary>Seconds flown on memory.</summary>
    public float MemoryTimer;

    /// <summary>Seconds flown opening with the diverters dry.</summary>
    public float DudTimer;

    /// <summary>Seconds of launch transient left.</summary>
    public float LaunchTimer;

    /// <summary>Where the launcher was told to send the missile, before the seeker has locked anything.</summary>
    public MapCoordinates? Designated;

    /// <summary>Time to go at lock, in seconds.</summary>
    public float LaunchTof;

    /// <summary>What the seeker is doing.</summary>
    public WFMissileSeekerState Seeker = WFMissileSeekerState.Tracking;

    /// <summary>Last position the seeker actually saw the target at.</summary>
    public Vector2 KnownPosition;

    /// <summary>Last velocity the seeker actually saw the target with.</summary>
    public Vector2 KnownVelocity;
}

/// <summary>Seeker state of a <see cref="WFMissileFlightComponent"/> missile.</summary>
public enum WFMissileSeekerState : byte
{
    /// <summary>Guiding on live seeker data.</summary>
    Tracking,

    /// <summary>Notched: flying the last good track until the geometry opens again.</summary>
    Blind,

    /// <summary>Lock lost for good.</summary>
    Lost,
}
