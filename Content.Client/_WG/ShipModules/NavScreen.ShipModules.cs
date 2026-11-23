using Content.Client._WG.ShipModules;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>The module strip for the ship this screen shows, named with the same prefix as the fuel bank.</summary>
    public Control WgShipModuleBank(string prefix) => new WGShipModuleBank(() => _shuttleEntity, prefix);
}
