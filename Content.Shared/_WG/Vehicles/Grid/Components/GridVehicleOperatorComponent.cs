using Content.Shared._WG.Vehicles.Base;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Vehicles.Grid.Components;

/// <summary>Marks the driver of a grid vehicle.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCVehicleSystem))]
public sealed partial class GridVehicleOperatorComponent : Component
{
}
