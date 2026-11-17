using Content.Server._WG.AdsIncursion.Components;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.SectorControl;

namespace Content.Server._WG.AdsIncursion.Systems;

public sealed partial class WGAdsCampaignSystem
{
    private void TickSpread(WGAdsCampaignPrototype campaign, WGAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        if (state.NextSpread is not { } next)
        {
            state.NextSpread = now + campaign.SpreadInterval;
            return;
        }

        if (now < next)
            return;

        state.NextSpread = now + campaign.SpreadInterval;
        TrySpreadOnce();
    }

    /// <summary>Takes one quiet, unvetoed frontier cell with no station in it within support range of a node; never contests one.</summary>
    public bool TrySpreadOnce()
    {
        if (Campaign is not { } campaign || State(true) is not { Finished: false })
            return false;

        var standing = Nodes();
        if (standing.Count == 0)
            return false;

        var map = _territory.SectorMap;
        var quietSince = _timing.CurTime - TimeSpan.FromMinutes(campaign.QuietMinutes);
        var candidates = new List<((WGSectorCell Cell, EntityUid Source) Pick, float Weight)>();
        foreach (var cell in _territory.Frontier(map, campaign.Faction))
        {
            if (_territory.Owner(map, cell) != null || _territory.ContestOf(map, cell) != null || _territory.IsDisputed(map, cell))
                continue;

            EntityUid? source = null;
            var nearest = int.MaxValue;
            foreach (var node in standing)
            {
                var distance = WGSectorHex.Distance(node.Comp1.Cell, cell);
                if (distance > campaign.SupportRange || distance >= nearest)
                    continue;

                nearest = distance;
                source = node.Owner;
            }

            if (source is not { } supporter || !_activity.IsQuiet(map, cell, quietSince) || HasStation(map, cell)
                || !_territory.CanClaim(map, cell, campaign.Faction, supporter))
                continue;

            var ring = WGSectorHex.Distance(cell, WGSectorCell.Origin) + 1f;
            candidates.Add(((cell, supporter), 1f / (ring * ring)));
        }

        return PickWeighted(candidates) is { } pick && _territory.TryClaim(map, pick.Cell, campaign.Faction, pick.Source);
    }
}
