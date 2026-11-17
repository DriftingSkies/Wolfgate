using Content.Server._WG.ReaverCampaign.Components;
using Content.Shared._WG.ReaverCampaign;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.ReaverCampaign.Systems;

public sealed partial class WGReaverCampaignSystem
{
    /// <summary>How long a station cell is contested before it is taken.</summary>
    private static readonly TimeSpan BlockadeTime = TimeSpan.FromMinutes(10);

    private void TickSpread(WGReaverCampaignPrototype campaign, WGReaverCampaignComponent state)
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

    /// <summary>Takes one quiet, unvetoed frontier cell within support range of a stronghold; a station cell is contested as a blockade.</summary>
    public bool TrySpreadOnce()
    {
        if (Campaign is not { } campaign || State(true) is not { Finished: false })
            return false;

        var standing = Strongholds();
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
            foreach (var stronghold in standing)
            {
                var distance = WGSectorHex.Distance(stronghold.Comp1.Cell, cell);
                if (distance > campaign.SupportRange || distance >= nearest)
                    continue;

                nearest = distance;
                source = stronghold.Owner;
            }

            if (source is not { } supporter || !_activity.IsQuiet(map, cell, quietSince)
                || !_territory.CanClaim(map, cell, campaign.Faction, supporter))
                continue;

            var ring = WGSectorHex.Distance(cell, WGSectorCell.Origin) + 1f;
            candidates.Add(((cell, supporter), 1f / (ring * ring)));
        }

        if (PickWeighted(candidates) is not { } pick)
            return false;

        if (BlockadeTargets(campaign, map, pick.Cell) is { Count: > 0 } stations)
            return TryBlockade(campaign, map, pick.Cell, pick.Source, stations[0]);

        return _territory.TryClaim(map, pick.Cell, campaign.Faction, pick.Source);
    }

    /// <summary>Contests a station's cell and warns the sector, naming the station.</summary>
    private bool TryBlockade(WGReaverCampaignPrototype campaign, MapId map, WGSectorCell cell, EntityUid source,
        Entity<MapGridComponent> station)
    {
        if (!_territory.Contest(map, cell, campaign.Faction, _timing.CurTime + BlockadeTime, source))
            return false;

        AnnounceRadio(Loc.GetString("wg-reaver-blockade",
            ("station", Name(station)),
            ("callsign", WGSectorHex.Callsign(cell)),
            ("minutes", (int) BlockadeTime.TotalMinutes)));
        return true;
    }
}
