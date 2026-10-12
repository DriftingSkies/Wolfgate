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

    /// <summary>Announces a cell the Reavers lost to a ship that held it, with no chime, and rescores the threat.</summary>
    private void OnRetaken(ref WFSectorCellRetakenEvent args)
    {
        if (CampaignFor(State()) is not { } campaign || args.Faction != campaign.Faction)
            return;

        Announce(campaign, Loc.GetString("wf-reaver-retaken", ("callsign", WFSectorHex.Callsign(args.Cell))), false, chime: false);
        Recompute();
    }
}
