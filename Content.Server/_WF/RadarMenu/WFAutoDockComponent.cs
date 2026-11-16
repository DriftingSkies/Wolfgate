using Content.Server._WF.NpcCrew.Systems;

namespace Content.Server._WF.RadarMenu;

/// <summary>On a helm flying its ship in to dock, as NPC pilots do. See <see cref="WFAutoDockSystem"/>.</summary>
[RegisterComponent]
public sealed partial class WFAutoDockComponent : Component
{
    [ViewVariables]
    public EntityUid TargetGrid;

    /// <summary>The dock the pilot picked, or null for any free one that fits.</summary>
    [ViewVariables]
    public EntityUid? RequestedDock;

    /// <summary>The port pair, docked pose and standoff being flown, from the NPC pilots' planner.</summary>
    [ViewVariables]
    public WFDockPlan Plan;

    [ViewVariables]
    public WFAutoDockPhase Phase = WFAutoDockPhase.Approach;

    /// <summary>When the current phase began.</summary>
    [ViewVariables]
    public TimeSpan PhaseStart;

    /// <summary>Seconds the approach may take, set once as it starts from the distance to fly, as an NPC pilot's.</summary>
    [ViewVariables]
    public float ApproachBudget;

    /// <summary>Failed tries so far; each goes back out to the standoff.</summary>
    [ViewVariables]
    public int Attempts;

    /// <summary>A grid the ship touched since the last check.</summary>
    [ViewVariables]
    public EntityUid? Collision;

    /// <summary>The pilot who asked, told how it went.</summary>
    [ViewVariables]
    public EntityUid? Pilot;

    [ViewVariables]
    public TimeSpan NextCheck;
}

public enum WFAutoDockPhase : byte
{
    /// <summary>The helm's autopilot was flying: waiting for it to let go of the controls before taking them.</summary>
    Release,

    /// <summary>Flying to the standoff out in front of the target's port.</summary>
    Approach,

    /// <summary>Stopping at the standoff, turned to dock.</summary>
    Settle,

    /// <summary>Easing in port to port until the docks can connect.</summary>
    Creep,
}
