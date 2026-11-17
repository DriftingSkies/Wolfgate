#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// A fix brings back a missing part and hull tile and heals damage without touching a part a player moved or a
/// floor a player laid; a restore to the snapshot does replace the moved part.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminRepairTest
{
    private const string VesselProto = "Guppy";
    private const string DamageType = "Blunt";

    [Test]
    public async Task FixKeepsWhatMovedAndRestoreDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid grid = default, moved = default, hurt = default;
        Vector2 goneAt = default;
        Vector2i tile = default, laid = default, pried = default, late = default;
        int tileType = 0, laidType = 0, priedType = 0;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var wasAdmin = false;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned), Is.True);
            grid = spawned!.Value;
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            var data = entMan.GetComponent<ShipRepairDataComponent>(grid);

            // Three parts from the snapshot that are still where it has them: one to delete, one to move, one to damage.
            var parts = data.Chunks.Values
                .SelectMany(chunk => chunk.Entities.Values)
                .Where(spec => entMan.TryGetEntity(spec.OriginalEntity, out var uid)
                               && entMan.HasComponent<DamageableComponent>(uid)
                               && entMan.GetComponent<TransformComponent>(uid.Value).Anchored)
                .Take(3)
                .ToList();
            Assert.That(parts, Has.Count.EqualTo(3), "Precondition: the design has parts to break.");

            var gone = entMan.GetEntity(parts[0].OriginalEntity!.Value);
            moved = entMan.GetEntity(parts[1].OriginalEntity!.Value);
            hurt = entMan.GetEntity(parts[2].OriginalEntity!.Value);
            goneAt = parts[0].LocalPosition;

            entMan.DeleteEntity(gone);
            tile = maps.LocalToTile(grid, gridComp, new EntityCoordinates(grid, goneAt));
            tileType = maps.GetTileRef(grid, gridComp, tile).Tile.TypeId;
            maps.SetTile(grid, gridComp, tile, Tile.Empty);

            // Three floor tiles over a sub-floor: a player lays another floor on one, and the others are pried up.
            laidType = tileDefs["FloorWood"].TileId;
            var floors = maps.GetAllTiles(grid, gridComp)
                .Where(floor => floor.Tile.TypeId != laidType
                                && tileDefs[floor.Tile.TypeId] is ContentTileDefinition def
                                && !string.IsNullOrEmpty(def.BaseTurf)
                                && tileDefs[def.BaseTurf].TileId != Tile.Empty.TypeId)
                .Take(3)
                .ToList();
            Assert.That(floors, Has.Count.EqualTo(3), "Precondition: the design has floor over a sub-floor.");
            laid = floors[0].GridIndices;
            pried = floors[1].GridIndices;
            priedType = floors[1].Tile.TypeId;
            late = floors[2].GridIndices;
            maps.SetTile(grid, gridComp, laid, new Tile(laidType));
            maps.SetTile(grid, gridComp, pried, new Tile(tileDefs[((ContentTileDefinition) tileDefs[priedType]).BaseTurf].TileId));
            maps.SetTile(grid, gridComp, late, new Tile(tileDefs[((ContentTileDefinition) tileDefs[floors[2].Tile.TypeId]).BaseTurf].TileId));

            xforms.Unanchor(moved);
            xforms.SetLocalPosition(moved, parts[1].LocalPosition + new Vector2(0.3f, 0.3f));

            var blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>(DamageType), FixedPoint2.New(5));
            damageable.TryChangeDamage(hurt, blunt, ignoreResistances: true);
            Assert.That(entMan.GetComponent<DamageableComponent>(hurt).TotalDamage, Is.GreaterThan(FixedPoint2.Zero), "Precondition: the part is damaged.");

            var damage = ships.Survey(grid);
            Assert.Multiple(() =>
            {
                Assert.That(damage.Tiles, Is.GreaterThanOrEqualTo(1));
                Assert.That(damage.Parts, Is.GreaterThanOrEqualTo(1));
                Assert.That(damage.Damaged, Is.GreaterThanOrEqualTo(1));
            });

            Assert.That(ships.StartFix(session, grid, WGShipAdminFixParts.None, out var result), Is.False, "A fix of nothing is refused.");
            Assert.That(ships.StartFix(session, grid, WGShipAdminFixParts.All, out result), Is.True, result);
            Assert.That(ships.StartFix(session, grid, WGShipAdminFixParts.All, out result), Is.False, "One fix at a time on a ship.");
            Assert.That(ships.Restore(session, grid, out result), Is.False, "No restore while a fix runs.");
            Assert.That(ships.Snapshot(session, grid, out result), Is.False, "No new snapshot while a fix runs.");
            Assert.That(ships.TryGetFix(grid, out _, out var total), Is.True);
            Assert.That(total, Is.GreaterThanOrEqualTo(3));

            // A player floors one of the pried tiles over while its hull is still queued.
            maps.SetTile(grid, gridComp, late, new Tile(laidType));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            var data = entMan.GetComponent<ShipRepairDataComponent>(grid);
            var rebuilt = data.Chunks.Values.SelectMany(chunk => chunk.Entities.Values).Single(spec => spec.LocalPosition == goneAt);
            var damage = ships.Survey(grid);

            Assert.Multiple(() =>
            {
                Assert.That(ships.TryGetFix(grid, out _, out _), Is.False, "The fix has finished.");
                Assert.That(maps.GetTileRef(grid, gridComp, tile).Tile.TypeId, Is.EqualTo(tileType), "The hull tile is back.");
                Assert.That(maps.GetTileRef(grid, gridComp, pried).Tile.TypeId, Is.EqualTo(priedType), "A floor pried up to its sub-floor is laid again.");
                Assert.That(maps.GetTileRef(grid, gridComp, laid).Tile.TypeId, Is.EqualTo(laidType), "A floor a player laid in the hull's place is left alone.");
                Assert.That(maps.GetTileRef(grid, gridComp, late).Tile.TypeId, Is.EqualTo(laidType), "So is one laid after the fix was queued.");
                Assert.That(entMan.TryGetEntity(rebuilt.OriginalEntity, out var again) && !entMan.Deleted(again), Is.True, "The missing part is rebuilt.");
                Assert.That(entMan.Deleted(moved), Is.False, "A part a player moved is left alone.");
                Assert.That(entMan.GetComponent<TransformComponent>(moved).Anchored, Is.False, "It is not put back either.");
                Assert.That(entMan.GetComponent<DamageableComponent>(hurt).TotalDamage, Is.EqualTo(FixedPoint2.Zero), "Damage is healed.");
                Assert.That(damage.Tiles, Is.Zero);
                Assert.That(damage.Parts, Is.Zero);
                Assert.That(damage.Damaged, Is.Zero);
            });

            Assert.That(ships.Restore(session, grid, out var result), Is.True, result);
            Assert.That(ships.Snapshot(session, grid, out result), Is.True, result);
            Assert.That(ships.Restore(session, map.Grid.Owner, out result), Is.False, "A grid with no snapshot has nothing to restore.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(moved), Is.True, "A restore replaces the part that moved.");
            entMan.DeleteEntity(grid);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }
}
