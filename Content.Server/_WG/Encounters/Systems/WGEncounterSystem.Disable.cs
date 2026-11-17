using Content.Server._WG.Encounters.Components;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.Encounters.Systems;

public sealed partial class WGEncounterSystem
{
    [Dependency] private WGShipDisablerSystem _disablers = default!;

    /// <summary>Lays a raider's disabling guns on the ship it is closing on to board, and stands them down otherwise.</summary>
    private void AimDisablers(WGEncounterShipState ship, bool begun)
    {
        _disablers.SetTarget(ship.Grid, begun ? DisableTarget(ship) : null);
    }

    /// <summary>
    /// The ship a raider is closing on or docked with for its run: an abductor's mark until the grab is over, a
    /// boarder's or boarding extorter's prey while it docks and loots. Never a ship berthed at a station.
    /// </summary>
    private EntityUid? DisableTarget(WGEncounterShipState ship)
    {
        EntityUid? target = null;
        if (ship.Abduct is { Phase: WGAbductPhase.Close or WGAbductPhase.Dock or WGAbductPhase.Grab } abduct)
            target = abduct.Mark;
        else if (ship.Abduct == null && ship.Extort is null or { Phase: WGExtortPhase.Board }
            && CurrentOrder(ship) is null or WGCrewObjectiveKind.Loot or WGCrewObjectiveKind.Hold)
            target = ship.Prey;

        if (target is not { } mark || TerminatingOrDeleted(mark))
            return null;

        return !_docking.AreGridsDocked(ship.Grid, mark) && DockedAtStation(mark) ? null : mark;
    }
}
