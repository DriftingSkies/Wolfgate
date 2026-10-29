using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How long the highest population seen is kept, so a dip at shift change does not weaken a hub being founded.</summary>
    private static readonly TimeSpan PopulationHold = TimeSpan.FromMinutes(10);

    private static readonly WFReaverPopulationBracket NoBracket = new();

    private int _populationOverride = -1;

    /// <summary>The living, non-ghost players on the sector map, or the admin override.</summary>
    public int LivePopulation()
    {
        return _populationOverride >= 0 ? _populationOverride : PlayerPositions(_territory.SectorMap).Count;
    }

    /// <summary>Keeps the highest population seen in the last ten minutes; the override takes effect at once.</summary>
    private void SamplePopulation(WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var live = LivePopulation();
        if (_populationOverride >= 0 || live >= state.Population || now - state.PopulationAt > PopulationHold)
        {
            state.Population = live;
            state.PopulationAt = now;
        }
    }

    /// <summary>The highest bracket the population reaches, or one that adds nothing.</summary>
    public WFReaverPopulationBracket Bracket(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var best = NoBracket;
        foreach (var bracket in campaign.Population)
        {
            if (state.Population >= bracket.MinPlayers && (best == NoBracket || bracket.MinPlayers > best.MinPlayers))
                best = bracket;
        }

        return best;
    }

    /// <summary>The bracket for the campaign in force, or one that adds nothing.</summary>
    private WFReaverPopulationBracket Bracket()
    {
        var state = State();
        return CampaignFor(state) is { } campaign && state != null ? Bracket(campaign, state) : NoBracket;
    }

    /// <summary>The tier campaign ships pick their hull, refit and skill by: the threat tier plus the bracket's gear.</summary>
    private int GearTier(WFReaverCampaignComponent state)
    {
        return Math.Clamp(state.Tier + Bracket().GearTiers, 0, WFEncounterThreat.MaxTier);
    }

    /// <summary>The time between reinforcements, scaled by the bracket.</summary>
    private TimeSpan ReinforcementInterval(WFReaverCampaignPrototype campaign)
    {
        return campaign.ReinforcementInterval * Math.Max(0.1f, Bracket().ReinforcementScale);
    }

    /// <summary>A bounty scaled by the bracket.</summary>
    private int Scaled(int bounty)
    {
        return (int) (bounty * Bracket().BountyScale);
    }
}
