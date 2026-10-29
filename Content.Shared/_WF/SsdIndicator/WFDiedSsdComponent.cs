using Robust.Shared.GameStates;

namespace Content.Shared._WF.SsdIndicator;

/// <summary>
/// On a body that died while its player was away, so it keeps the SSD icon that dead bodies otherwise drop.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFDiedSsdComponent : Component;
