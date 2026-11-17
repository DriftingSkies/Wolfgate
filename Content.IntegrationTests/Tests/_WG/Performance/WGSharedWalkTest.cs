#nullable enable
using System.Linq;
using Content.Server._Mono.Radar;
using Content.Server._WG.Planets.Flight;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Radar;
using Content.Shared._WG.Planets;
using Content.Shared._WG.Planets.Flight;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Walks shared between callers must still give each caller the right answer.</summary>
[TestFixture]
public sealed class WGSharedWalkTest
{
    [Test]
    public async Task RadarBlipsAreSortedByMapAndRefreshEachTick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var radar = entMan.System<RadarBlipSystem>();
        var mapA = await pair.CreateTestMap();
        var mapB = await pair.CreateTestMap();
        EntityUid onA = default;
        EntityUid onB = default;
        EntityUid off = default;

        await server.WaitPost(() =>
        {
            onA = entMan.SpawnEntity(null, new MapCoordinates(1, 2, mapA.MapId));
            entMan.AddComponent<PhysicsComponent>(onA);
            entMan.AddComponent<RadarBlipComponent>(onA).Enabled = true;
            onB = entMan.SpawnEntity(null, new MapCoordinates(3, 4, mapB.MapId));
            entMan.AddComponent<PhysicsComponent>(onB);
            entMan.AddComponent<RadarBlipComponent>(onB).Enabled = true;
            off = entMan.SpawnEntity(null, new MapCoordinates(5, 6, mapA.MapId));
            entMan.AddComponent<PhysicsComponent>(off);
            entMan.AddComponent<RadarBlipComponent>(off).Enabled = false;
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var a = radar.WGBlipsOnMap(mapA.MapId).Select(b => b.Uid).ToList();
            Assert.That(a, Does.Contain(onA));
            Assert.That(a, Does.Not.Contain(onB), "A blip on another map isn't this radar's.");
            Assert.That(a, Does.Not.Contain(off), "A disabled blip is left out.");
            Assert.That(radar.WGBlipsOnMap(mapB.MapId).Select(b => b.Uid), Does.Contain(onB));
            Assert.That(radar.WGBlipsOnMap(mapA.MapId).Single(b => b.Uid == onA).Position.X, Is.EqualTo(1f).Within(0.01f));

            entMan.GetComponent<RadarBlipComponent>(off).Enabled = true;
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(radar.WGBlipsOnMap(mapA.MapId).Select(b => b.Uid), Does.Contain(off), "The list is rebuilt on a new tick.");
            entMan.DeleteEntity(onA);
            entMan.DeleteEntity(onB);
            entMan.DeleteEntity(off);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DocksAreSharedThenRefreshed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var console = server.EntMan.System<ShuttleConsoleSystem>();
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var first = console.GetAllDocks();
            Assert.That(console.GetAllDocks(), Is.SameAs(first), "Consoles in the same moment share one walk.");
        });

        var before = await GetDocks();
        await pair.RunTicksSync(40);
        Assert.That(await GetDocks(), Is.Not.SameAs(before), "A shared walk goes stale.");
        await pair.CleanReturnAsync();

        async Task<object> GetDocks()
        {
            object result = default!;
            await server.WaitPost(() => result = console.GetAllDocks());
            return result;
        }
    }

    [Test]
    public async Task DragHoldsHullsOnLayersOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = entMan.System<SharedPhysicsSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var plain = await pair.CreateTestMap();
        var layered = await pair.CreateTestMap();
        Entity<MapGridComponent> hull = default;

        await server.WaitPost(() =>
        {
            var layer = entMan.EnsureComponent<WGPlanetLayerComponent>(layered.MapUid);
            hull = maps.CreateGridEntity(layered.MapId);
            maps.SetTile(hull.Owner, hull.Comp, Vector2i.Zero, layered.Tile.Tile);
            // A shuttle resets its own damping every tick, which would overwrite what the test saves.
            entMan.RemoveComponent<Content.Server.Shuttles.Components.ShuttleComponent>(hull.Owner);
            var body = entMan.GetComponent<PhysicsComponent>(hull.Owner);
            physics.SetBodyType(hull.Owner, Robust.Shared.Physics.BodyType.Dynamic, body: body);
            physics.SetLinearDamping(hull.Owner, body, 0.123f);
            physics.SetLinearVelocity(hull.Owner, new System.Numerics.Vector2(layer.MaxSpeed + 50f, 0f), body: body);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var layer = entMan.GetComponent<WGPlanetLayerComponent>(layered.MapUid);
            var body = entMan.GetComponent<PhysicsComponent>(hull.Owner);
            Assert.That(entMan.HasComponent<WGPlanetDragComponent>(hull.Owner), "A hull on a layer is dragged.");
            Assert.That(entMan.GetComponent<WGPlanetDragComponent>(hull.Owner).OriginalDamping, Is.EqualTo(0.123f).Within(0.001f), "Its own damping is saved.");
            Assert.That(body.LinearVelocity.Length(), Is.LessThanOrEqualTo(layer.MaxSpeed + 0.01f), "The speed cap holds.");
            entMan.RemoveComponent<WGPlanetLayerComponent>(layered.MapUid);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WGPlanetDragComponent>(hull.Owner), Is.False, "A hull off the layer is let go.");
            Assert.That(plain.MapUid, Is.Not.EqualTo(layered.MapUid));
            entMan.DeleteEntity(hull.Owner);
        });

        await pair.CleanReturnAsync();
    }
}
