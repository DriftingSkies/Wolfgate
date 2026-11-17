using Content.Server._WG.ShipCommand;

namespace Content.Server.Cargo.Systems;

/// <summary>Cargo order consoles and sale pallets aboard a commanded ship turn the barred away.</summary>
public sealed partial class CargoSystem
{
    [Dependency] private WGShipServiceSystem _wgShipServices = default!;

    /// <summary>True, with a popup, when a cargo console aboard a commanded ship turns the customer away.</summary>
    private bool WgShipRefuses(EntityUid console, EntityUid customer)
    {
        if (!_wgShipServices.Refuses(console, customer, out var reason))
            return false;

        _popup.PopupEntity(reason, console, customer);
        return true;
    }
}
