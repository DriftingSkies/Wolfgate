namespace Content.Server._WF.CameraNetworks;

/// <summary>
/// A long-range camera router that only shows its subnet to monitors on its own grid. Cameras anywhere in range still
/// connect, so a faction base watches its bodycams without every TV in the sector listing them.
/// </summary>
[RegisterComponent]
public sealed partial class WFLocalCameraRouterComponent : Component;
