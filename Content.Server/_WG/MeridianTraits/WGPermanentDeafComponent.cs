namespace Content.Server._WG.MeridianTraits;

/// <summary>
/// Keeps <see cref="Content.Shared._WG.RMCCore.Deafness.DeafComponent"/> on the entity when a status or knockout ends.
/// </summary>
[RegisterComponent, Access(typeof(WGPermanentDeafSystem))]
public sealed partial class WGPermanentDeafComponent : Component;
