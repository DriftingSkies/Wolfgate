namespace Content.Server._WF.MeridianTraits;

/// <summary>
/// Keeps <see cref="Content.Shared._WF.RMCCore.Deafness.DeafComponent"/> on the entity when a status or knockout ends.
/// </summary>
[RegisterComponent, Access(typeof(WFPermanentDeafSystem))]
public sealed partial class WFPermanentDeafComponent : Component;
