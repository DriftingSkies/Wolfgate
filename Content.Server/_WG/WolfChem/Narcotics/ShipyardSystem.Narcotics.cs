using Content.Server._WG.WolfChem.Narcotics;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private WGNarcoticGoodsSystem _wgNarcoticGoods = default!;

    /// <summary>
    /// Whether a thing holds narcotics, packed, spilled or in a body. A hull sale pays nothing for it or what is
    /// inside it, so narcotics only sell at a pallet, where heat prices them.
    /// </summary>
    private bool WGHoldsNarcotics(EntityUid uid)
    {
        return _wgNarcoticGoods.StreetValue(uid) > 0;
    }
}
