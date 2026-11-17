namespace Content.Server._WG.Encounters.Components;

/// <summary>A raider's guns for stopping a ship before it boards, and the ship they are laid on.</summary>
[RegisterComponent]
public sealed partial class WGShipDisablerComponent : Component
{
    /// <summary>The ion disruptors and hacking harpoons mounted for it.</summary>
    [ViewVariables]
    public List<EntityUid> Guns = new();

    /// <summary>The ship it is about to board. Null stands the guns down and lets go of any cable.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>When the disruptors first fired on the target without knocking its engines out.</summary>
    [ViewVariables]
    public TimeSpan? DisruptingSince;
}
