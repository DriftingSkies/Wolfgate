namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// An outpost's cryopod: a player who owns the outpost can join the round in it, as its settler. An outpost holds at
/// most <see cref="WFOutpostSystem.MaxPods"/>.
/// </summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostCryopodComponent : Component;
