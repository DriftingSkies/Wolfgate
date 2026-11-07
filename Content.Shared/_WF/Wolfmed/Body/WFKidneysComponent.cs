using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Marks an organ as kidneys, so organ surgeries can find them the way Shitmed's Liver and Stomach markers do.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFKidneysComponent : Component;
