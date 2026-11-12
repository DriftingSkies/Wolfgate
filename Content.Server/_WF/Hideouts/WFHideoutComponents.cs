using Content.Shared._WF.Hideouts;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server._WF.Hideouts;

/// <summary>An outpost's modules: the level each stands at and the one being built. Saved with the outpost.</summary>
[RegisterComponent, Access(typeof(WFHideoutSystem))]
public sealed partial class WFHideoutComponent : Component
{
    /// <summary>The level each module stands at, by module id; a module not listed isn't built.</summary>
    [DataField]
    public Dictionary<string, int> Levels = new();

    /// <summary>The module whose next level is being built.</summary>
    [DataField]
    public string? Building;

    /// <summary>Seconds of building left; it only runs while the outpost stands in a round.</summary>
    [DataField]
    public float BuildLeft;

    /// <summary>The planet surface the outpost lands on each round, set where it was founded; moving it costs a fee.</summary>
    [DataField]
    public string? HomeWorld;
}

/// <summary>A survey stake: used on the ground in its owner's claim, it lays a module's prefab there.</summary>
[RegisterComponent, Access(typeof(WFHideoutSystem))]
public sealed partial class WFHideoutStakeComponent : Component
{
    [DataField]
    public ResPath? Prefab;

    /// <summary>Whether the stake lays its module as a shell: floors, walls, doors, power and lights, to furnish by hand.</summary>
    [DataField]
    public bool Shell;

    /// <summary>The layouts the stake can be set to; picking one sets <see cref="Prefab"/>.</summary>
    [DataField]
    public List<WFHideoutLayout> Layouts = new();

    /// <summary>The theme the stake lays its module in; null lays the prefab as drawn.</summary>
    [DataField]
    public string? Theme;
}

/// <summary>The landing pad of an outpost's hangar: the owner's registered ship is saved whenever it lands here.</summary>
[RegisterComponent]
public sealed partial class WFHideoutHangarComponent : Component;

/// <summary>A ship's strongbox: what it holds is saved with a hangar ship, unlike the rest of the ship's cargo.</summary>
[RegisterComponent]
public sealed partial class WFShipStashComponent : Component;

/// <summary>A hull registered as an account's hangar ship this round.</summary>
[RegisterComponent, UnsavedComponent, Access(typeof(WFHideoutSystem))]
public sealed partial class WFHideoutShipComponent : Component
{
    [ViewVariables]
    public NetUserId Owner;
}

/// <summary>
/// Raised on a hangar ship before it is saved, as it lands or is insured; a system that sets <see cref="Refusal"/> to a
/// loc id keeps the last save instead.
/// </summary>
[ByRefEvent]
public record struct WFHideoutShipSaveAttemptEvent
{
    public string? Refusal;
}
