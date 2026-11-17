using Content.Shared._WG.Gags;
using Content.Shared.Preferences.Loadouts;

namespace Content.Shared.Station;

public abstract partial class SharedStationSpawningSystem
{
    [Dependency] private SharedWGGagsSystem _wgGags = default!;

    /// <summary>Gives the item a loadout just put on the colours the player picked for it.</summary>
    protected void ApplyWGGagsColors(EntityUid entity, LoadoutPrototype proto, Loadout loadout)
    {
        _wgGags.ApplyLoadout(entity, proto, loadout);
    }
}
