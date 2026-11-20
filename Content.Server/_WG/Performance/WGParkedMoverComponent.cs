namespace Content.Server._WG.Performance;

/// <summary>
/// A mover taken off MoverController's update list while it sleeps with no input, player or relay. Waking its body,
/// a move input or a player attaching puts it back.
/// </summary>
[RegisterComponent]
public sealed partial class WGParkedMoverComponent : Component;
