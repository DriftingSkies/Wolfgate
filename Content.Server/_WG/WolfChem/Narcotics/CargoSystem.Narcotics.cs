using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private WGNarcoticGoodsSystem _wgNarcoticGoods = default!;

    private (EntityUid Console, GameTick Tick) _wgNarcoticRefusal;

    /// <summary>
    /// Whether a console on a grid with <see cref="WGRefusesNarcoticsComponent"/> turns this away because it, or
    /// anything packed inside it, holds narcotics. The buyer says so once per appraisal or sale.
    /// </summary>
    private bool WgRefusesNarcotics(EntityUid console, EntityUid grid, EntityUid ent)
    {
        if (!HasComp<WGRefusesNarcoticsComponent>(grid) || !WgHoldsNarcotics(ent))
            return false;

        if (_wgNarcoticRefusal != (console, _timing.CurTick))
        {
            _wgNarcoticRefusal = (console, _timing.CurTick);
            _popup.PopupEntity(Loc.GetString("wg-narcotics-buyer-refuses"), console, PopupType.MediumCaution);
        }

        return true;
    }

    private bool WgHoldsNarcotics(EntityUid ent)
    {
        if (_wgNarcoticGoods.StreetValue(ent) > 0)
            return true;

        if (!TryComp<ContainerManagerComponent>(ent, out var containers))
            return false;

        var contents = new HashSet<EntityUid>();
        GatherContainedEntities(ent, containers, contents);
        foreach (var contained in contents)
        {
            if (_wgNarcoticGoods.StreetValue(contained) > 0)
                return true;
        }

        return false;
    }
}
