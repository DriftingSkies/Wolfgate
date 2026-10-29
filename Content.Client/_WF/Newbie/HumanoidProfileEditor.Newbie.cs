using Content.Shared._WF.Newbie;
using Content.Shared.Traits;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    /// <summary>Whether this player has the newbie tag and the trait is closed to them, as the server checks at spawn.</summary>
    private bool WfNewbieTraitBlocked(TraitPrototype trait)
    {
        if (!trait.NewbieBlocked || !_cfgManager.GetCVar(WFNewbieCVars.ShowTag))
            return false;

        var hours = _cfgManager.GetCVar(WFNewbieCVars.Hours);
        return hours > 0 && _requirements.FetchOverallPlaytime() < TimeSpan.FromHours(hours);
    }
}
