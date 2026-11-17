using Content.Server._WG.Encounters.Components;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Robust.Shared.Map;

namespace Content.Server._WG.Encounters.Systems;

// Keeps encounters from piling into one area: every ship in a fight is state sent to every player near it.
public sealed partial class WGEncounterSchedulerSystem
{
    /// <summary>How many placements are rolled before giving up on a crowded one.</summary>
    private const int CrowdTries = 4;

    /// <summary>Finds a place for an encounter that isn't already crowded with other encounters' ships.</summary>
    private bool TryPlaceUncrowded(WGEncounterPrototype prototype, out MapCoordinates origin, out List<EntityUid> stops)
    {
        for (var i = 0; i < CrowdTries; i++)
        {
            if (!TryPlace(prototype, out origin, out stops))
                return false;

            if (!Crowded(origin))
                return true;
        }

        Log.Info($"Encounter {prototype.ID} not placed: every spot tried is crowded with other encounters.");
        origin = MapCoordinates.Nullspace;
        stops = new List<EntityUid>();
        return false;
    }

    /// <summary>
    /// Whether enough unresolved encounters have a ship, real or simulated, near a point. Freight and round-long
    /// encounters don't count: they rarely fight.
    /// </summary>
    public bool Crowded(MapCoordinates point)
    {
        var radius = _config.GetCVar(EncountersCVars.CrowdRadius);
        var max = _config.GetCVar(EncountersCVars.CrowdMax);
        if (radius <= 0f || max <= 0)
            return false;

        var count = 0;
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var encounter))
        {
            if (encounter.Resolution != null || encounter.Freight || encounter.Lifetime == WGEncounterLifetime.Persistent)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (!TryComp(ship.Grid, out TransformComponent? xform))
                    continue;

                var at = _transform.GetMapCoordinates(ship.Grid, xform);
                if (at.MapId != point.MapId || (at.Position - point.Position).LengthSquared() > radius * radius)
                    continue;

                if (++count >= max)
                    return true;
                break;
            }
        }

        return false;
    }

    /// <summary>How many unresolved encounters have a ship engaging an intruder.</summary>
    public int Fights()
    {
        var fights = 0;
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Engaged.Count == 0)
                    continue;

                fights++;
                break;
            }
        }

        return fights;
    }

    /// <summary>Whether enough fights are going on that the scheduler should hold off.</summary>
    private bool Busy()
    {
        var busy = _config.GetCVar(EncountersCVars.BusyFights);
        if (busy <= 0 || Fights() < busy)
            return false;

        Log.Debug("No encounter scheduled: too many fights are going on.");
        return true;
    }
}
