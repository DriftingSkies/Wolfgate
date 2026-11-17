using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>
/// Turns NpcCrew's docking events, crewed or simulated, into <see cref="WGEncounterStopReachedEvent"/> when a run's ship
/// docks at one of its route's stops, so NpcCrew never needs to know about encounters. Docks that aren't stops are ignored.
/// </summary>
public sealed class WGEncounterStopSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPilotDockedEvent>(OnPilotDocked);
        SubscribeLocalEvent<WGCrewVirtualDockedEvent>(OnVirtualDocked);
    }

    private void OnPilotDocked(ref WGPilotDockedEvent args)
    {
        Reached(args.Grid, args.TargetGrid);
    }

    private void OnVirtualDocked(ref WGCrewVirtualDockedEvent args)
    {
        Reached(args.Grid, args.TargetGrid);
    }

    private void Reached(EntityUid grid, EntityUid station)
    {
        if (!TryComp<WGEncounterGridComponent>(grid, out var ship)
            || !TryComp<WGEncounterComponent>(ship.Encounter, out var encounter)
            || !encounter.Stops.Contains(station))
            return;

        var ev = new WGEncounterStopReachedEvent(ship.Encounter, grid, station);
        RaiseLocalEvent(ref ev);
    }
}
