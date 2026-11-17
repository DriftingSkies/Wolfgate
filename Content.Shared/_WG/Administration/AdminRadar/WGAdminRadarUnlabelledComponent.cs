using Robust.Shared.GameStates;

namespace Content.Shared._WG.Administration.AdminRadar;

/// <summary>A grid the admin scanner never labels, even with true IFF on, such as a drifting asteroid.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGAdminRadarUnlabelledComponent : Component;
