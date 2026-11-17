using Content.Client._WG.CargoLoad;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>The cargo strip for the ship this screen shows, named with the same prefix as the fuel bank.</summary>
    public Control WgCargoGauge(string prefix) => new WGCargoGauge(() => _shuttleEntity, prefix);
}
