using Robust.Shared.GameStates;

namespace Content.Shared._WF.NeckSuits;

/// <summary>
/// An outer suit adjusted for non-functional use: it goes in a neck slot, draws over the worn suit and has no armor,
/// temperature protection or slowdown. Removed when it's adjusted back.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFNeckSuitComponent : Component;
