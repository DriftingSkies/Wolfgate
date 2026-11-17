using Content.Client._WG.Newbie;
using Content.Shared.Traits;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    /// <summary>Whether this player has the newbie tag and the trait is closed to them, as the server checks at spawn.</summary>
    private bool WgNewbieTraitBlocked(TraitPrototype trait)
    {
        return WGNewbieTraitRules.TraitBlocked(trait, _cfgManager, _requirements);
    }
}
