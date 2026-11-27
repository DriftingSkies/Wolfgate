using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipShields;

/// <summary>Marks a hull that is venting its heat, so every radar that can see it draws the vent.</summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent]
public sealed partial class WGShipVentingComponent : Component;
