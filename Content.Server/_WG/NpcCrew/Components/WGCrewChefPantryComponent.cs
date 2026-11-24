namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// A galley's pantry crate. A chef aboard cooks from it like any other store, and while he is at work it is slowly
/// restocked with his menu's ingredients (<see cref="WGCrewChefComponent.RestockInterval"/>).
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewChefPantryComponent : Component;
