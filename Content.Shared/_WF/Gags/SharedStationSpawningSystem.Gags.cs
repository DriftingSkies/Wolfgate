using Content.Shared._WF.Gags;
using Content.Shared.Preferences.Loadouts;

namespace Content.Shared.Station;

public abstract partial class SharedStationSpawningSystem
{
    [Dependency] private SharedWFGagsSystem _wfGags = default!;

    /// <summary>Gives the item a loadout just put on the colours the player picked for it.</summary>
    protected void ApplyWFGagsColors(EntityUid entity, LoadoutPrototype proto, Loadout loadout)
    {
        _wfGags.ApplyLoadout(entity, proto, loadout);
    }
}
