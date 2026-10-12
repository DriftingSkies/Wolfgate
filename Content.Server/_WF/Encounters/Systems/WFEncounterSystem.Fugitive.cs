using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>How far a run takes the ship from where it started it.</summary>
    private const float FleeLeg = 2500f;

    /// <summary>A run is flown again once the ship is this close to its end.</summary>
    private const float FleeRenew = 600f;

    /// <summary>Cosine of how far the bearing from the chaser may drift before the run is turned: about 37 degrees.</summary>
    private const float FleeTurn = 0.8f;

    /// <summary>
    /// A ship that runs from player ships flies directly away from the nearest one inside its range, and with none
    /// near goes back to its wander. It never starts a fight. A ship that turns on its attackers is left to its
    /// captain while it is alerted.
    /// </summary>
    private void Flee(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship)
    {
        if (!ship.HasOrders || TerminatingOrDeleted(ship.Grid) || IsStranded(ship) || !HasLivingCrew(ship)
            || ship.Evades && _alerts.IsAlerted(ship.Grid, ship.Group))
            return;

        if (NearestCrewedShip(ship.Grid, ship.Flees) is not { } chaser)
        {
            if (!ship.Fleeing)
                return;

            ship.Fleeing = false;
            ResumeWander(encounter, key, ship);
            return;
        }

        var here = _transform.GetWorldPosition(ship.Grid);
        var away = here - _transform.GetWorldPosition(chaser);
        var heading = away.LengthSquared() > 1f ? Vector2.Normalize(away) : _random.NextAngle().ToVec();
        if (ship.Fleeing && Vector2.Dot(heading, ship.FleeHeading) >= FleeTurn
            && (ship.FleeGoal - here).LengthSquared() > FleeRenew * FleeRenew)
            return;

        var goal = here + heading * FleeLeg;
        // The run ends in a hold with no end, so a ship that has got clear is not taken for one that has flown its orders.
        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = goal, Range = 150f },
            new() { Kind = WFCrewObjectiveKind.Hold },
        };
        if (!_objectives.SetQueue(ship.Grid, ship.Group, queue))
            return;

        ship.Fleeing = true;
        ship.FleeHeading = heading;
        ship.FleeGoal = goal;
        ship.HasOrders = true;
        ship.Flown = false;
    }

    /// <summary>Gives a ship that has got clear its prototype's orders again, its wander flown about where it is now.</summary>
    private void ResumeWander(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship)
    {
        if (!_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            || prototype.Ships.FirstOrDefault(entry => entry.Key == key) is not { } entry)
            return;

        var queue = BuildQueue(encounter.Comp, entry, ship.Grid, _transform.GetWorldPosition(ship.Grid));
        if (queue.Count == 0 || !_objectives.SetQueue(ship.Grid, ship.Group, queue))
            return;

        ship.HasOrders = true;
        ship.Flown = false;
    }
}
