using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipShields;

/// <summary>Marks a hull whose field has overloaded or whose guns are locked by a collapse, so every radar that can see it draws the overload.</summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent]
public sealed partial class WGShipOverloadedComponent : Component;
