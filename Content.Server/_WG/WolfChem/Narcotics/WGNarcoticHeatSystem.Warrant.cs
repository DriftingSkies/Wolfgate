using Content.Server._NF.BountyContracts;
using Content.Server._WG.Drydock;
using Content.Shared._NF.BountyContracts;
using Content.Shared._WG.WolfChem.Narcotics;

namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>
/// The warrant on a seller who went past the hot line. Customs puts one out on them (<see cref="WGShipbreakerWarrantSystem"/>):
/// it names them on the bounty board, the one place they are named, and pays whoever brings them down. Every sale adds
/// a share of what it sold past the line, up to a cap. It outlasts the heat.
/// </summary>
public sealed partial class WGNarcoticHeatSystem
{
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private WGShipbreakerWarrantSystem _warrants = default!;

    /// <summary>The warrant out on a seller, or null if none is on the board.</summary>
    public BountyContract? Warrant(EntityUid seller)
    {
        return TryComp<WGShipbreakerWarrantComponent>(seller, out var warrant)
               && warrant.Contract is { } id
               && _board.TryGetContract(id, out var contract)
            ? contract
            : null;
    }

    /// <summary>
    /// Adds what a sale sold past the hot line to the warrant on its seller, up to the cap. Returns whether the sale
    /// went past that line with a warrant out on them for selling.
    /// </summary>
    private bool RaiseWarrant(EntityUid seller, EntityUid grid, Seller record, double heat, double value, LocId? goods)
    {
        // The warrant is on the body: once it is paid, pardoned or left behind with the body, the count starts over.
        if (record.Wanted != seller || !HasComp<WGShipbreakerWarrantComponent>(seller))
        {
            record.Wanted = seller;
            record.WarrantReward = 0;
        }

        var line = Math.Max(Free(record), _cfg.GetCVar(WGNarcoticsCVars.HeatHot));
        var over = WGNarcoticHeatMath.Over(heat, value, line) * _cfg.GetCVar(WGNarcoticsCVars.WarrantShare);
        var added = (int) Math.Min(over, _cfg.GetCVar(WGNarcoticsCVars.WarrantCap) - record.WarrantReward);
        if (added > 0)
        {
            record.WarrantReward += added;
            _warrants.Issue(seller, Name(grid), added,
                Loc.GetString("wg-narcotics-warrant", ("goods", Loc.GetString(goods ?? Narcotics)), ("place", Name(grid))),
                Loc.GetString("wg-narcotics-warrant-customs"), "wg-narcotics-warrant-paid");
        }

        return over > 0 && record.WarrantReward > 0;
    }
}
