using Content.Server._WF.SectorControl;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Keeps a cell with a standing stronghold in it from being retaken: it is freed when the hub is broken.</summary>
    private void OnRetakeAttempt(ref WFSectorRetakeAttemptEvent args)
    {
        if (Campaign is not { } campaign || args.Faction != campaign.Faction)
            return;

        foreach (var (_, stronghold, _) in Strongholds())
        {
            if (stronghold.Cell != args.Cell)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    /// <summary>Pays the ships that held a retaken cell its bounty, announces it with no chime, and rescores the threat.</summary>
    private void OnRetaken(ref WFSectorCellRetakenEvent args)
    {
        var state = State();
        if (CampaignFor(state) is not { } campaign || args.Faction != campaign.Faction)
            return;

        var callsign = WFSectorHex.Callsign(args.Cell);
        var bounty = ByTier(campaign.RetakeBounty, state?.Tier ?? 0, 0);
        var paid = 0;
        if (bounty > 0 && args.Ships is { Count: > 0 } ships)
        {
            paid = _rewards.PayCrews(Loc.GetString("wf-reaver-retake-name", ("callsign", callsign)), $"Reaver cell {callsign}", bounty, ships,
                "retaking");
        }

        if (state != null)
            state.Retaken++;

        Announce(campaign, Loc.GetString("wf-reaver-retaken", ("callsign", callsign), ("bounty", bounty), ("paid", paid)), false, chime: false);
        Recompute();
    }
}
