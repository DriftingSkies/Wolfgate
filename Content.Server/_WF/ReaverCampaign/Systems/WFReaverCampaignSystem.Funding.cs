using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.TradeGoods;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    [Dependency] private WFTradeMarketSystem _market = default!;

    /// <summary>Banks a funding sale toward the next payout, and pays it out if one is due.</summary>
    private void OnTradeSale(WFTradeSaleEvent args)
    {
        if (!Running
            || Campaign is not { FundingCrates: > 0 } campaign
            || !FundsReavers(args)
            || State(true) is not { Finished: false } state)
            return;

        state.Funding = Math.Min(state.Funding + 1, campaign.FundingCrates);
        TickFunding(campaign, state);
    }

    /// <summary>Whether a sale funds the Reavers: smuggling crates at open chutes, and freight taken off NPC haulers fenced at the Underground.</summary>
    private bool FundsReavers(WFTradeSaleEvent args)
    {
        if (_market.Profile(args.Grid).ContrabandGoods.Contains(args.Good))
            return !_market.IsHiddenMarket(args.Grid);

        return _market.IsSealedOnly(args.Chute)
               && TryComp<WFTradeOriginComponent>(args.Crate, out var origin)
               && origin.Run != null;
    }

    /// <summary>Pays out a full war chest once the campaign isn't held and a stronghold stands to spend it.</summary>
    private void TickFunding(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (campaign.FundingCrates <= 0 || state.Funding < campaign.FundingCrates || Held || state.Finished)
            return;

        if (Strongholds().Count == 0)
            return;

        state.Funding = 0;
        Fund(campaign, state);
    }

    /// <summary>
    /// Spends one payout: every stronghold's next reinforcement and the next spread come sooner by
    /// <see cref="WFReaverCampaignPrototype.FundingShare"/> of their intervals, announced on the radio.
    /// </summary>
    public void Fund(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var reinforcementCut = ReinforcementInterval(campaign) * campaign.FundingShare;
        foreach (var stronghold in Strongholds())
        {
            stronghold.Comp1.NextReinforcement = MaxTime(now, stronghold.Comp1.NextReinforcement - reinforcementCut);
        }

        if (state.NextSpread is { } next)
            state.NextSpread = MaxTime(now, next - campaign.SpreadInterval * campaign.FundingShare);

        state.Funded++;
        AnnounceRadio(Loc.GetString("wf-reaver-funded"));
    }
}
