namespace Content.Shared._WF.Hideouts;

/// <summary>
/// On anything saved in a hangar ship's strongbox. It comes back with the ship, so outposts, safety deposit boxes and
/// round-end persistent items don't keep it as well.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipSaveOriginComponent : Component;
