using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Parasite;

/// <summary>A facehugger that knocks down and bites but never infects, for admin events.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFParasiteBiteOnlyComponent : Component;
