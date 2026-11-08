namespace Content.Shared._WF.Outposts;

/// <summary>
/// On anything that has been saved with an outpost. It comes back with the outpost, so it is never kept anywhere else
/// between rounds: safety deposit boxes and round-end persistent items refuse it.
/// </summary>
[RegisterComponent]
public sealed partial class WFSaveOriginComponent : Component;
