using Content.Server.Station.Components;
using Content.Shared.Station.Components;
using Content.Server.StationEvents.Components;
using Content.Shared.Physics;
using Content.Shared.Station.Components;
using Robust.Shared.Map; // WOLFGATE(LiveFixes)
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Configuration;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.GameTicking.Components;
using Content.Shared._NF.CCVar;

namespace Content.Server.StationEvents.Events;

public sealed partial class BluespaceCargoRule : StationEventSystem<BluespaceCargoRuleComponent>
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] protected IRobustRandom _random = default!;

    protected override void Added(EntityUid uid, BluespaceCargoRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
    }

    protected override void Started(EntityUid uid, BluespaceCargoRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation, HasComp<StationJobsComponent>))
            return;

        if (!TryComp<StationDataComponent>(chosenStation, out var stationData))
            return;

        var grid = StationSystem.GetLargestGrid((chosenStation!.Value, stationData));

        if (grid is null)
            return;

        var amountToSpawn = _random.Next(component.MinimumSpawns, component.MaximumSpawns + 1); // +1 required: [min, max)
        for (var i = 0; i < amountToSpawn; i++)
        {
            SpawnOnRandomGridLocation(grid.Value, component.SpawnerPrototype, component.FlashPrototype, component.RequireSafeAtmosphere);
        }
    }

    public void SpawnOnRandomGridLocation(EntityUid grid, string toSpawn, string toSpawnFlash, bool safeAtmosphere)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var xform = Transform(grid);

        // WOLFGATE(LiveFixes) START: with no free tile, the grid's own coordinates were on the map and could anchor to another grid
        // var targetCoords = xform.Coordinates;
        EntityCoordinates? targetCoords = null;
        // WOLFGATE END
        var gridBounds = gridComp.LocalAABB.Scale(_configuration.GetCVar(NFCCVars.CrateGenerationGridBoundsScale));

        for (var i = 0; i < 25; i++)
        {
            var randomX = _random.Next((int) gridBounds.Left, (int) gridBounds.Right);
            var randomY = _random.Next((int) gridBounds.Bottom, (int) gridBounds.Top);

            var tile = new Vector2i(randomX, randomY);

            // no air-blocked areas.
            if (_atmosphere.IsTileSpace(grid, xform.MapUid, tile) ||
                _atmosphere.IsTileAirBlocked(grid, tile, mapGridComp: gridComp))
            {
                continue;
            }

            // don't spawn inside of solid objects
            var physQuery = GetEntityQuery<PhysicsComponent>();
            var valid = true;
            foreach (var ent in _map.GetAnchoredEntities((grid, gridComp), tile))
            {
                if (!physQuery.TryGetComponent(ent, out var body))
                    continue;
                if (body.BodyType != BodyType.Static ||
                    !body.Hard ||
                    (body.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
                    continue;

                valid = false;
                break;
            }
            if (!valid)
                continue;

            // WOLFGATE(LiveFixes) START: pass the map, since terrain with no tracked air reads its mixture from there
            // if (safeAtmosphere && !_atmosphere.IsTileMixtureProbablySafe(grid, grid, tile))
            if (safeAtmosphere && !_atmosphere.IsTileMixtureProbablySafe(grid, xform.MapUid ?? grid, tile))
            // WOLFGATE END
            {
                continue;
            }

            targetCoords = _map.GridTileToLocal(grid, gridComp, tile);
            break;
        }

        // WOLFGATE(LiveFixes) START: check every tile before skipping the crate, since random picks can miss a sparse grid
        if (targetCoords == null)
        {
            foreach (var tileRef in _map.GetAllTiles(grid, gridComp))
            {
                if (!WGIsFreeTile(grid, gridComp, xform.MapUid, tileRef.GridIndices, safeAtmosphere))
                    continue;

                targetCoords = _map.GridTileToLocal(grid, gridComp, tileRef.GridIndices);
                break;
            }
        }

        if (targetCoords == null)
            return;

        // Spawn(toSpawn, targetCoords);
        // Spawn(toSpawnFlash, targetCoords);
        Spawn(toSpawn, targetCoords.Value);
        Spawn(toSpawnFlash, targetCoords.Value);
        // WOLFGATE END

        Sawmill.Info($"Spawning random cargo at {targetCoords}");
    }

    // WOLFGATE(LiveFixes) START: the random search's tile checks, for the search of every tile
    private bool WGIsFreeTile(EntityUid grid, MapGridComponent gridComp, EntityUid? map, Vector2i tile, bool safeAtmosphere)
    {
        if (_atmosphere.IsTileSpace(grid, map, tile) || _atmosphere.IsTileAirBlocked(grid, tile, mapGridComp: gridComp))
            return false;

        var physQuery = GetEntityQuery<PhysicsComponent>();
        foreach (var ent in _map.GetAnchoredEntities((grid, gridComp), tile))
        {
            if (physQuery.TryGetComponent(ent, out var body)
                && body.BodyType == BodyType.Static
                && body.Hard
                && (body.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
            {
                return false;
            }
        }

        return !safeAtmosphere || _atmosphere.IsTileMixtureProbablySafe(grid, map ?? grid, tile);
    }
    // WOLFGATE END
}
