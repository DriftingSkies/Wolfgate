#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server._Mono.Cleanup;
using Content.Shared.Buckle;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Gameplay reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121GameTest
{
    /// <summary>
    /// #1157: a handled solution state gives the component its own copy. Prediction used to write into the cached
    /// server state, so a re-predicted 5u fill stacked up to a full bottle for a moment.
    /// </summary>
    [Test]
    public async Task SolutionStateIsCopied()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var solutions = server.System<SharedSolutionContainerSystem>();
            var from = em.SpawnEntity("Beaker", map.GridCoords);
            var to = em.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(from, "beaker", out var fromSoln), Is.True);
            Assert.That(solutions.TryGetSolution(to, "beaker", out var toSoln), Is.True);
            solutions.TryAddReagent(fromSoln!.Value, "Water", 5);

            var state = em.GetComponentState(em.EventBus, fromSoln.Value.Comp, null, GameTick.Zero);
            var stateSolution = (Solution) state!.GetType().GetField(nameof(SolutionComponent.Solution))!.GetValue(state)!;
            var handle = new ComponentHandleState(state, null);
            em.EventBus.RaiseComponentEvent(toSoln!.Value.Owner, toSoln.Value.Comp, ref handle);

            Assert.That(toSoln.Value.Comp.Solution, Is.Not.SameAs(stateSolution));
            Assert.That(toSoln.Value.Comp.Solution.Name, Is.EqualTo("beaker"));
            toSoln.Value.Comp.Solution.AddReagent("Water", 5);
            Assert.That(stateSolution.Volume.Int(), Is.EqualTo(5));

            em.DeleteEntity(from);
            em.DeleteEntity(to);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// #1164: the server centres a rider's view on their wheelchair and takes it back off when they leave it, so the
    /// client never has to change the networked eye on its own.
    /// </summary>
    [Test]
    public async Task WheelchairViewFollowsTheRider()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid mob = default, chair = default;

        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            chair = server.EntMan.SpawnEntity("VehicleWheelchair", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(mob, mob, chair, popup: false), Is.True);
            Assert.That(server.EntMan.GetComponent<EyeComponent>(mob).Target, Is.EqualTo(chair));
        });

        // Past the buckle's unbuckle delay.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryUnbuckle(mob, mob, popup: false), Is.True);
            Assert.That(server.EntMan.GetComponent<EyeComponent>(mob).Target, Is.Null);

            server.EntMan.DeleteEntity(mob);
            server.EntMan.DeleteEntity(chair);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// #1272: trade crates bought one after another spawn inside their vendor and jam into the walls round it. The space
    /// cleanup's wall-stuck sweep used to delete those at their 100-thaler static price; now it keeps them.
    /// </summary>
    [Test]
    public async Task VendedCratesJammedInWallsAreKept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var crates = new List<EntityUid>();
        EntityUid vendor = default;

        // MMC Hokkaido's trade vendor stands in a nook: walled in on three sides.
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x < 5; x++)
            for (var y = -4; y < 5; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            vendor = server.EntMan.SpawnEntity("WGVendingMachineTraderHokkaido", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(-0.5f, 0.5f)));
            server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 1.5f)));
            server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, -0.5f)));
        });

        // A crate where the vendor ejects one, every half second.
        for (var i = 0; i < 8; i++)
        {
            await server.WaitPost(() => crates.Add(server.EntMan.SpawnEntity("TradeGoodLuxuries",
                server.EntMan.GetComponent<TransformComponent>(vendor).Coordinates)));
            await pair.RunTicksSync(30);
        }
        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            var cleanup = server.System<SpaceCleanupSystem>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var stuck = typeof(SpaceCleanupSystem).GetMethod("GetWallStuck", flags)!;
            var should = typeof(SpaceCleanupSystem).GetMethod("ShouldEntityCleanup", flags, new[] { typeof(EntityUid) })!;
            var jammed = 0;
            foreach (var crate in crates)
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(crate);
                if ((bool) stuck.Invoke(cleanup, new object[] { new Entity<TransformComponent>(crate, xform) })!)
                    jammed++;
                Assert.That((bool) should.Invoke(cleanup, new object[] { crate })!, Is.False, $"crate at {xform.LocalPosition} would be deleted");
            }

            Assert.That(jammed, Is.GreaterThan(0), "no crate jammed into a wall, so the test proves nothing");
        });

        await pair.CleanReturnAsync();
    }
}
