namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's command console: its window (<see cref="WGShipConsoleSystem"/>) runs the ship it stands on.
/// Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipConsoleComponent : Component;
