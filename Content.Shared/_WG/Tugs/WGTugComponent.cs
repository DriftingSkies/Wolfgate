using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Tugs;

/// <summary>A sit-on luggage tug: whoever buckles into it drives it, and it tows a train of trolleys along its path.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGTugComponent : Component
{
    /// <summary>Most trolleys the tug can tow.</summary>
    [DataField]
    public int MaxTrolleys = 5;

    /// <summary>Distance along the path from the tug's centre to the first trolley's.</summary>
    [DataField]
    public float HitchDistance = 0.9f;

    /// <summary>Distance along the path between one trolley's centre and the next.</summary>
    [DataField]
    public float TrolleySpacing = 0.9f;

    /// <summary>How far a trolley may stand from the back of the train to be coupled.</summary>
    [DataField]
    public float CoupleRange = 1.6f;

    /// <summary>Share of top speed lost per towed trolley.</summary>
    [DataField]
    public float SpeedPenaltyPerTrolley = 0.06f;

    /// <summary>Share of acceleration lost per towed trolley.</summary>
    [DataField]
    public float AccelerationPenaltyPerTrolley = 0.1f;

    /// <summary>Towed trolleys, nearest first.</summary>
    [AutoNetworkedField]
    public List<EntityUid> Trolleys = new();

    /// <summary>The tug's past positions in its grid's frame, oldest first (<see cref="WGTugTrail"/>). Each side keeps its own.</summary>
    [ViewVariables]
    public List<Vector2> Trail = new();

    /// <summary>How many cars the trail was last laid through, so the client relays it when a car is coupled.</summary>
    [ViewVariables]
    public int TrailCars;

    /// <summary>The grid the trail is measured on.</summary>
    [ViewVariables]
    public EntityUid? TrailGrid;

    /// <summary>The way the oldest part of the trail ran, so backing up past its start goes on straight.</summary>
    [ViewVariables]
    public Vector2 TailForward;

    /// <summary>Where the tug last stood with its train on free floor, to back out of a wall to.</summary>
    [ViewVariables]
    public Vector2 LastFreePosition;

    /// <summary>The mover's tuning before towing penalties, read once at map init and saved, since the mover's own fields hold the penalised values while towing.</summary>
    [DataField]
    public float BaseMaxSpeed;

    [DataField]
    public float BaseMaxReverseSpeed;

    [DataField]
    public float BaseAcceleration;
}

/// <summary>A luggage trolley: couples behind a tug and carries one crate strapped to its bed.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGTugTrolleyComponent : Component
{
    [DataField]
    public string ContainerId = "wg-trolley-cargo";

    /// <summary>A closed entity storage whose prototype is, or inherits from, one of these can be strapped to the bed.</summary>
    [DataField]
    public List<EntProtoId> CrateParents = new() { "CrateGeneric" };

    /// <summary>Seconds to strap a crate on or take it off.</summary>
    [DataField]
    public float CargoDelay = 1.5f;

    /// <summary>The tug towing this trolley.</summary>
    [AutoNetworkedField]
    public EntityUid? Tug;

    /// <summary>Set while towed and saved with the trolley, so one saved mid-tow gets its physics back on load.</summary>
    [DataField]
    public bool Towed;

    [ViewVariables]
    public ContainerSlot? Cargo;
}
