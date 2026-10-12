using System.Collections.Generic;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.FtlEffects;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.FtlEffects;

/// <summary>Checks actual FTL departure, cancellation and cleanup on a reusable server.</summary>
public sealed class FtlDepartureTest
{
    [TestCase(false, 2f)]
    [TestCase(true, 2f)]
    [TestCase(false, 0f)]
    public async Task DepartureTracksJumpAndCleansUp(bool cancel, float startup)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var shuttle = source.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            maps.SetTiles(shuttle, source.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(1, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(1, 1), new Tile(1)),
            });
            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(shuttle, entities.GetComponent<ShuttleComponent>(shuttle),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: startup, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var ftl = entities.GetComponent<FTLComponent>(shuttle);
            var effect = entities.GetComponent<FtlDepartureComponent>(shuttle);
            if (startup > 0f)
            {
                Assert.That(ftl.State, Is.EqualTo(FTLState.Starting));
                Assert.That(effect.Started, Is.EqualTo(ftl.StateTime.Start));
                Assert.That(effect.Departure, Is.EqualTo(ftl.StateTime.End));
                Assert.That(effect.Entered, Is.False);
            }
            else
                Assert.That(effect.Entered, Is.True, "Instant jumps still produce a departure flash.");
            if (cancel)
                entities.RemoveComponent<FTLComponent>(shuttle);
        });

        if (!cancel)
        {
            var entered = false;
            for (var i = 0; i < 180 && !entered; i++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() => entered = entities.GetComponent<FtlDepartureComponent>(shuttle).Entered);
            }
            await server.WaitAssertion(() =>
            {
                Assert.That(entered, Is.True);
                var map = entities.GetComponent<TransformComponent>(shuttle).MapUid;
                Assert.That(entities.HasComponent<FTLMapComponent>(map), Is.True);
                Assert.That(entities.GetComponent<FTLComponent>(shuttle).State, Is.EqualTo(FTLState.Travelling));
            });
        }

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() => Assert.That(entities.HasComponent<FtlDepartureComponent>(shuttle), Is.False));

        if (!cancel)
        {
            var arrived = false;
            for (var i = 0; i < 1200 && !arrived; i++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() => arrived = entities.TryGetComponent<FtlDepartureComponent>(shuttle, out var effect)
                    && effect.Arriving);
            }
            await server.WaitAssertion(() =>
            {
                Assert.That(arrived, Is.True, "A real completed jump must trigger the arrival rush.");
                Assert.That(entities.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(target.MapUid));
                Assert.That(entities.GetComponent<FtlDepartureComponent>(shuttle).Entered, Is.True);
            });
            await pair.RunTicksSync(60);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<FtlDepartureComponent>(shuttle), Is.False);
            entities.DeleteEntity(shuttle);
            var cleanup = new List<EntityUid> { source.MapUid, target.MapUid };
            var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var uid, out _))
                cleanup.Add(uid);
            foreach (var uid in cleanup)
                entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Checks a watching client times the rush against the state it is shown, not its predicted clock.</summary>
    [Test]
    public async Task ObserverSeesWholeLaunchAndArrival()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var shuttle = source.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            maps.SetTiles(shuttle, source.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(1, 0), new Tile(1)),
            });
            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(shuttle, entities.GetComponent<ShuttleComponent>(shuttle),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: 2f, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });
        await pair.RunTicksSync(5);

        var clientEntities = client.ResolveDependency<IEntityManager>();
        var timing = client.ResolveDependency<IGameTiming>();
        var visuals = clientEntities.System<Content.Client._WF.FtlEffects.FtlDepartureSystem>();
        var clientShuttle = pair.ToClientUid(shuttle);
        var clientSource = pair.ToClientUid(source.MapUid);
        var clientTarget = pair.ToClientUid(target.MapUid);

        // The last frame before the grid leaves must still show the hull launched, never back at rest.
        var left = false;
        var launched = false;
        var launch = 0f;
        var slowest = 0f;
        for (var i = 0; i < 180 && !left; i++)
        {
            await pair.RunTicksSync(1);
            await client.WaitPost(() =>
            {
                if (clientEntities.GetComponent<TransformComponent>(clientShuttle).MapUid != clientSource)
                {
                    left = true;
                    return;
                }
                if (!clientEntities.TryGetComponent(clientShuttle, out FtlDepartureComponent effect))
                    return;
                launched = visuals.Gone(clientShuttle, effect);
                launch = visuals.Motion(clientShuttle, effect);
                slowest = FtlDepartureTiming.Motion(effect.Departure - timing.TickPeriod, effect.Departure, false, false);
            });
        }
        Assert.That(left, Is.True);
        Assert.That(launched || launch >= slowest - 0.001f, Is.True,
            $"The hull sat at its origin before leaving: motion {launch}, expected at least {slowest}.");

        // The first frame of the arrival must be the start of the rush.
        var arrived = false;
        var arrival = 0f;
        var latest = 0f;
        for (var i = 0; i < 1200 && !arrived; i++)
        {
            await pair.RunTicksSync(1);
            await client.WaitPost(() =>
            {
                if (!clientEntities.TryGetComponent(clientShuttle, out FtlDepartureComponent effect) || !effect.Arriving)
                    return;
                arrived = true;
                Assert.That(clientEntities.GetComponent<TransformComponent>(clientShuttle).MapUid, Is.EqualTo(clientTarget));
                arrival = visuals.Motion(clientShuttle, effect);
                latest = FtlDepartureTiming.Motion(effect.Departure + timing.TickPeriod, effect.Departure, true, true);
            });
        }
        Assert.That(arrived, Is.True);
        Assert.That(arrival, Is.LessThanOrEqualTo(latest + 0.001f),
            $"The arrival rush was mostly over when the ship appeared: motion {arrival}, expected at most {latest}.");

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(shuttle);
            var cleanup = new List<EntityUid> { source.MapUid, target.MapUid };
            var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var uid, out _))
                cleanup.Add(uid);
            foreach (var uid in cleanup)
                entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Checks a docked ship shares the lead ship's effect from spool-up to arrival, on both sides.</summary>
    [Test]
    public async Task DockedShipRidesTheLeadsJump()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var lead = source.Grid.Owner;
        var follower = EntityUid.Invalid;
        // The engine logs joint errors on a client whenever docked grids change map.
        var failureLevel = pair.ClientLogHandler.FailureLevel;
        pair.ClientLogHandler.FailureLevel = LogLevel.Fatal;

        await server.WaitAssertion(() =>
        {
            var tiles = new List<(Vector2i, Tile)> { (new Vector2i(0, 0), new Tile(1)), (new Vector2i(0, 1), new Tile(1)) };
            maps.SetTiles(lead, source.Grid.Comp, tiles);
            var other = mapManager.CreateGridEntity(source.MapId);
            follower = other.Owner;
            maps.SetTiles(follower, other.Comp, tiles);
            entities.System<SharedTransformSystem>().SetLocalPosition(follower, new Vector2(1f, 0f));

            var dockA = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(lead, new Vector2(0.5f, 0.5f)));
            var dockB = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(follower, new Vector2(0.5f, 0.5f)));
            entities.System<DockingSystem>().Dock((dockA, entities.GetComponent<DockingComponent>(dockA)),
                (dockB, entities.GetComponent<DockingComponent>(dockB)));
            var convoy = new HashSet<EntityUid>();
            shuttles.GetAllDockedShuttles(lead, convoy);
            Assert.That(convoy, Does.Contain(follower), "Precondition: the second grid is docked to the lead ship.");

            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(lead, entities.GetComponent<ShuttleComponent>(lead),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: 2f, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });
        await pair.RunTicksSync(5);

        var clientEntities = client.ResolveDependency<IEntityManager>();
        var visuals = clientEntities.System<Content.Client._WF.FtlEffects.FtlDepartureSystem>();
        var clientLead = pair.ToClientUid(lead);
        var clientFollower = pair.ToClientUid(follower);

        await server.WaitAssertion(() =>
        {
            var ahead = entities.GetComponent<FtlDepartureComponent>(lead);
            var behind = entities.GetComponent<FtlDepartureComponent>(follower);
            Assert.That(ahead.Lead, Is.Null);
            Assert.That(behind.Lead, Is.EqualTo(lead));
            Assert.That(behind.Started, Is.EqualTo(ahead.Started));
            Assert.That(behind.Departure, Is.EqualTo(ahead.Departure));
            Assert.That(behind.Entered, Is.False);
        });
        await client.WaitAssertion(() =>
            Assert.That(clientEntities.GetComponent<FtlDepartureComponent>(clientFollower).Lead, Is.EqualTo(clientLead)));

        var entered = false;
        for (var i = 0; i < 180 && !entered; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => entered = entities.GetComponent<FtlDepartureComponent>(lead).Entered);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(entered, Is.True);
            Assert.That(entities.HasComponent<FTLMapComponent>(entities.GetComponent<TransformComponent>(follower).MapUid), Is.True);
            Assert.That(entities.GetComponent<FtlDepartureComponent>(follower).Entered, Is.True);
        });

        // Both grids must show the same rush on the first frame of the arrival.
        var arrived = false;
        var leadMotion = 0f;
        var followerMotion = 0f;
        for (var i = 0; i < 1200 && !arrived; i++)
        {
            await pair.RunTicksSync(1);
            await client.WaitPost(() =>
            {
                if (!clientEntities.TryGetComponent(clientFollower, out FtlDepartureComponent behind) || !behind.Arriving)
                    return;
                arrived = true;
                Assert.That(behind.Lead, Is.EqualTo(clientLead));
                leadMotion = visuals.Motion(clientLead, clientEntities.GetComponent<FtlDepartureComponent>(clientLead));
                followerMotion = visuals.Motion(clientFollower, behind);
            });
        }
        Assert.That(arrived, Is.True, "The docked ship must get the arrival rush.");
        Assert.That(leadMotion, Is.LessThan(-0.5f));
        Assert.That(followerMotion, Is.EqualTo(leadMotion));
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<TransformComponent>(follower).MapUid, Is.EqualTo(target.MapUid)));

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<FtlDepartureComponent>(follower), Is.False);
            entities.DeleteEntity(follower);
            entities.DeleteEntity(lead);
            var cleanup = new List<EntityUid> { source.MapUid, target.MapUid };
            var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var uid, out _))
                cleanup.Add(uid);
            foreach (var uid in cleanup)
                entities.DeleteEntity(uid);
        });
        await pair.RunTicksSync(5);
        pair.ClientLogHandler.FailureLevel = failureLevel;
        await pair.CleanReturnAsync();
    }

    /// <summary>Checks a player waiting at the destination is sent the ship's contents before it drops out.</summary>
    [Test]
    public async Task ArrivingShipIsSentAheadToNearbyPlayers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        // Test pairs run without PVS; TestPair reverts this when the pair is returned.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var transforms = entities.System<SharedTransformSystem>();
        var shuttle = source.Grid.Owner;
        var viewer = EntityUid.Invalid;
        var cargo = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            viewer = entities.SpawnEntity(null, new EntityCoordinates(target.MapUid, new Vector2(50, 55)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            maps.SetTiles(shuttle, source.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
            });
            cargo = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttle, new Vector2(0.5f, 0.5f)));
            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(shuttle, entities.GetComponent<ShuttleComponent>(shuttle),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: 0f, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });
        var clientEntities = client.ResolveDependency<IEntityManager>();
        var netCargo = entities.GetNetEntity(cargo);

        await pair.RunTicksSync(20);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<FTLComponent>(shuttle).State, Is.EqualTo(FTLState.Travelling)));
        await client.WaitAssertion(() => Assert.That(clientEntities.TryGetEntity(netCargo, out _), Is.False,
            "Precondition: a ship far from the player is not sent to it."));

        var state = FTLState.Travelling;
        for (var i = 0; i < 300 && state != FTLState.Arriving; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => state = entities.GetComponent<FTLComponent>(shuttle).State);
        }
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FTLComponent>(shuttle).State, Is.EqualTo(FTLState.Arriving));
            Assert.That(entities.HasComponent<FTLMapComponent>(entities.GetComponent<TransformComponent>(shuttle).MapUid), Is.True);
        });
        var clientCargo = EntityUid.Invalid;
        await client.WaitAssertion(() =>
        {
            Assert.That(clientEntities.TryGetEntity(netCargo, out var found), Is.True,
                "The ship's contents must reach a nearby player while it is still in hyperspace.");
            clientCargo = found!.Value;
            Assert.That(clientEntities.GetComponent<TransformComponent>(clientCargo).GridUid, Is.EqualTo(pair.ToClientUid(shuttle)));
        });

        var landed = false;
        for (var i = 0; i < 300 && !landed; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => landed = entities.GetComponent<TransformComponent>(shuttle).MapUid == target.MapUid);
        }
        Assert.That(landed, Is.True);

        // Once the ship has arrived the override is gone, so walking away drops its contents as usual.
        await server.WaitPost(() => transforms.SetCoordinates(viewer, new EntityCoordinates(source.MapUid, new Vector2(500, 500))));
        await pair.RunTicksSync(30);
        await client.WaitAssertion(() => Assert.That(
            clientEntities.GetComponent<MetaDataComponent>(clientCargo).Flags & MetaDataFlags.Detached,
            Is.EqualTo(MetaDataFlags.Detached), "The early send must end when the ship arrives."));

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(viewer);
            entities.DeleteEntity(shuttle);
            var cleanup = new List<EntityUid> { source.MapUid, target.MapUid };
            var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var uid, out _))
                cleanup.Add(uid);
            foreach (var uid in cleanup)
                entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
}
