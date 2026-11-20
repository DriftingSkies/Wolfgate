namespace Content.Shared._WG.TurnInPlace;

/// <summary>
/// Marks a player entity whose turn-in-place key is held. Not networked: the server and the predicting client
/// each set it from the same key input.
/// </summary>
[RegisterComponent]
public sealed partial class WGTurnInPlaceComponent : Component;
