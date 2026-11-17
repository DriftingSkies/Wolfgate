using Content.Server._WG.PlanetCargoHubs;
using Content.Shared.Examine;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private WGPlanetCargoHubSystem _wgCargoHubs = default!;

    /// <summary>A trade crate bound for a planetside depot says which world the depot stands on.</summary>
    private void WgExamineCrateHub(EntityUid destination, ExaminedEvent ev)
    {
        if (_wgCargoHubs.CrateNote(destination) is { } note)
            ev.PushMarkup(note);
    }
}
