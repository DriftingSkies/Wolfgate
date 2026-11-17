using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;

namespace Content.Server._WG.ReaverCampaign.Systems;

public sealed partial class WGReaverCampaignSystem
{
    /// <summary>
    /// Campaign ships never take one another for attackers. Their fire is reckless and goes through hubs and docked
    /// reinforcements, and each is its own encounter, so the usual few stray hits would set them on each other.
    /// </summary>
    private void OnFriendlyFireQuery(ref WGCrewFriendlyFireQueryEvent args)
    {
        if (!args.Friendly && IsCampaignShip(args.Grid) && IsCampaignShip(args.Attacker))
            args.Friendly = true;
    }

    /// <summary>Whether a grid is a ship of a campaign encounter still in its crew's hands.</summary>
    private bool IsCampaignShip(EntityUid grid)
    {
        return NpcHull(grid) && TryComp<WGEncounterGridComponent>(grid, out var marker) && IsCampaignEncounter(marker.Encounter);
    }
}
