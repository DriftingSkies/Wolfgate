using Content.Server._NF.Atmos.Components;
using Content.Server._WG.ShipCommand;

namespace Content.Server._NF.Atmos.Systems;

/// <summary>A gas sale console aboard a commanded ship doesn't buy from the barred.</summary>
public sealed partial class GasDepositSystem
{
    [Dependency] private WGShipServiceSystem _wgShipServices = default!;

    /// <summary>True, with a popup, when the console's commanded ship turns the seller away; the gas stays in the sale points.</summary>
    private bool WgShipRefuses(Entity<GasSaleConsoleComponent> ent, EntityUid seller)
    {
        if (!_wgShipServices.Refuses(ent, seller, out var reason))
            return false;

        _popup.PopupEntity(reason, ent, seller);
        return true;
    }
}
