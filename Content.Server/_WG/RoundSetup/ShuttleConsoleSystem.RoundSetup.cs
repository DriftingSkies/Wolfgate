using Content.Server._WG.RoundSetup;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Scenario hosts and reserved crew roles control admission even when the helm loses power.</summary>
    private bool WGHasCrewAdmission(EntityUid station) =>
        HasComp<WGCrashScenarioStationComponent>(station) || HasComp<WGShipScenarioStationComponent>(station) ||
        HasComp<WGDoomedScenarioStationComponent>(station);
}
