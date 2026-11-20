#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Space wind skips only tiles too weak to push, and the cached grid list still picks up new atmospheres at once.</summary>
[TestFixture]
public sealed class WGAtmosWorkCutTest
{
    [Test]
    public async Task WindTooWeakToSearchMovesNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = entMan.System<AtmosphereSystem>();
        var map = await pair.CreateTestMap();

        Assert.That(AtmosphereSystem.WGWindCanMove(1975f), Is.False);
        Assert.That(AtmosphereSystem.WGWindCanMove(1976f), Is.True);

        await server.WaitAssertion(() =>
        {
            var weak = Item();
            atmos.ExperiencePressureDifference(weak, 2, 1975f, AtmosDirection.East, 0f, EntityCoordinates.Invalid, Angle.Zero);
            Assert.That(entMan.GetComponent<PhysicsComponent>(weak).LinearVelocity, Is.EqualTo(Vector2.Zero),
                "Wind the search skips must not be able to move anything.");

            var strong = Item();
            atmos.ExperiencePressureDifference(strong, 2, 1976f, AtmosDirection.East, 0f, EntityCoordinates.Invalid, Angle.Zero);
            Assert.That(entMan.GetComponent<PhysicsComponent>(strong).LinearVelocity.Length(), Is.GreaterThan(0f),
                "Wind just past the cut-off still throws.");

            entMan.DeleteEntity(weak);
            entMan.DeleteEntity(strong);
        });

        await pair.CleanReturnAsync();

        Entity<MovedByPressureComponent> Item()
        {
            var uid = entMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            return (uid, entMan.GetComponent<MovedByPressureComponent>(uid));
        }
    }

    [Test]
    public async Task NewGridAtmosphereIsSimulatedAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();

        // Let the cached list settle so the new grid can't ride a scheduled rebuild.
        await server.WaitRunTicks(10);

        var grid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var created = maps.CreateGridEntity(map.MapId);
            grid = created.Owner;
            entMan.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(40f, 40f));
            maps.SetTiles(grid, created.Comp, new List<(Vector2i, Tile)> { (Vector2i.Zero, map.Tile.Tile), (Vector2i.Right, map.Tile.Tile) });
            entMan.EnsureComponent<GridAtmosphereComponent>(grid);
        });

        await server.WaitRunTicks(1);
#pragma warning disable RA0002
        var before = 0;
        await server.WaitPost(() => before = entMan.GetComponent<GridAtmosphereComponent>(grid).UpdateCounter);

        // Well under the 5 s safety rebuild: only the startup mark can have put it in the list.
        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GridAtmosphereComponent>(grid).UpdateCounter, Is.GreaterThan(before),
                "A grid atmosphere added mid-round must join the next run.");
        });
#pragma warning restore RA0002

        // Deleting it drops it from the list without errors.
        await server.WaitPost(() => entMan.DeleteEntity(grid));
        await server.WaitRunTicks(30);

        await pair.CleanReturnAsync();
    }
}
