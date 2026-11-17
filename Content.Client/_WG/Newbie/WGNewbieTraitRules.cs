using Content.Client.Players.PlayTimeTracking;
using Content.Shared._WG.Newbie;
using Content.Shared.Traits;
using Robust.Shared.Configuration;

namespace Content.Client._WG.Newbie;

/// <summary>Whether the local player's newbie state closes a trait to them.</summary>
public static class WGNewbieTraitRules
{
    public static bool TraitBlocked(TraitPrototype trait, IConfigurationManager cfg, JobRequirementsManager requirements)
    {
        if (!trait.NewbieBlocked || !cfg.GetCVar(WGNewbieCVars.ShowTag))
            return false;

        var hours = cfg.GetCVar(WGNewbieCVars.Hours);
        return hours > 0 && requirements.FetchOverallPlaytime() < TimeSpan.FromHours(hours);
    }
}
