using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>
    /// Campaign ships never take one another for attackers. Their fire is reckless and goes through hubs and docked
    /// reinforcements, and each is its own encounter, so the usual few stray hits would set them on each other.
    /// </summary>
    private void OnFriendlyFireQuery(ref WFCrewFriendlyFireQueryEvent args)
    {
        if (!args.Friendly && IsCampaignShip(args.Grid) && IsCampaignShip(args.Attacker))
            args.Friendly = true;
    }

    /// <summary>Whether a grid is a ship of a campaign encounter.</summary>
    private bool IsCampaignShip(EntityUid grid)
    {
        return TryComp<WFEncounterGridComponent>(grid, out var marker) && IsCampaignEncounter(marker.Encounter);
    }
}
