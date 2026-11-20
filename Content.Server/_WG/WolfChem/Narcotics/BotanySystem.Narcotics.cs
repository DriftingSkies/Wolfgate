using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared.Chemistry.Components;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySystem
{
    [Dependency] private WGNarcoticGoodsSystem _wgNarcoticGoods = default!;

    /// <summary>Marks a grown produce that holds a street drug; harvest fills it without the solution change event.</summary>
    private void WGMarkProduce(EntityUid uid, Solution solution)
    {
        if (_wgNarcoticGoods.HoldsNarcotics(solution))
            _wgNarcoticGoods.SetNarcotic(uid, true);
    }
}
