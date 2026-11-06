namespace Content.Shared._WF.Lattice;

/// <summary>A reality anchor: adds its hold to the Lattice field on its map while active.</summary>
[RegisterComponent]
public sealed partial class WFLatticeAnchorComponent : Component
{
    /// <summary>Hold at the anchor itself; a primary anchor is 1.</summary>
    [DataField]
    public float Strength = 1f;

    /// <summary>Whether it holds the field now. A relay holds it only while powered and parked.</summary>
    [DataField]
    public bool Active = true;

    /// <summary>One of the Lattice's original anchors, whose loss is a breach.</summary>
    [DataField]
    public bool Primary;
}

/// <summary>A player-built anchor: holds the field only while powered, anchored and parked.</summary>
[RegisterComponent]
public sealed partial class WFLatticeRelayComponent : Component
{
    /// <summary>Hold while working.</summary>
    [DataField]
    public float Strength = 0.4f;

    /// <summary>Seconds its grid must sit still before it takes hold.</summary>
    [DataField]
    public float SettleSeconds = 60f;

    /// <summary>Fastest its grid may drift and still count as parked, in metres a second.</summary>
    [DataField]
    public float MaxSpeed = 1f;

    /// <summary>Seconds its grid has sat still so far.</summary>
    [ViewVariables]
    public float Settled;
}

/// <summary>One of an anchor station's own guns: it fires only from an anchor station.</summary>
[RegisterComponent]
public sealed partial class WFLatticeDefenceWeaponComponent : Component;

/// <summary>An anchor station's shield generator, whose field is drawn as a tinted, rooted Ward.</summary>
[RegisterComponent]
public sealed partial class WFLatticeAnchorShieldComponent : Component;

/// <summary>On a primary anchor's station grid: when its drones last came out to defend it.</summary>
[RegisterComponent]
public sealed partial class WFLatticeAnchorStationComponent : Component
{
    [ViewVariables]
    public TimeSpan NextDefence;

    /// <summary>The anchor core on it.</summary>
    [ViewVariables]
    public EntityUid Core;

    /// <summary>Hits each attacking grid has landed lately, and when the last one came.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, (float Strikes, TimeSpan Last)> Strikes = new();

    /// <summary>Grids the station is shooting back at, until when.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Hostiles = new();

    /// <summary>When the station may next announce that it is under fire.</summary>
    [ViewVariables]
    public TimeSpan NextAlert;

    /// <summary>Whether the station is fighting back now, its shield down.</summary>
    [ViewVariables]
    public bool Engaged;
}
