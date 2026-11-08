namespace Content.Server._WF.Outposts.Components;

/// <summary>The station outpost settlers join as: one for the round, with no grid, never in the station picker.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostStationComponent : Component;
