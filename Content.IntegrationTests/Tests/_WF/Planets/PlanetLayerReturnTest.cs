#nullable enable
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Reports 646/695/718/719/774/930: a client that leaves a planet layer and comes back sees its floor but not its
/// walls and objects. Leaving a range-limited layer deletes it on the client; its children must come back with it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetPvsSystem))]
public sealed class PlanetLayerReturnTest
{
    // Range-limited, the returning client keeps the layer's entities in nullspace: the engine deletes the layer there
    // and detaches its children, then never re-attaches them. Engine fix pending; wf.planet_pvs_range_limited is off.
    [TestCase(true, 30, Ignore = "Lupine engine: an evicted range-limited layer's children stay detached on the client")]
    [TestCase(true, 3, Ignore = "Lupine engine: an evicted range-limited layer's children stay detached on the client")]
    [TestCase(false, 30)]
    public async Task ReturningClientGetsTheLayersEntitiesBack(bool rangeLimited, int awayTicks)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var clientLogs = client.ResolveDependency<ILogManager>();
        var clientMapLog = clientLogs.GetSawmill("system.map");
        var clientMapLogLevel = clientMapLog.Level;
        clientMapLog.Level = LogLevel.Error;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            server.CfgMan.SetCVar(PlanetCVars.PvsRangeLimited, rangeLimited);
        });
        await pair.RunTicksSync(2);
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            var viewer = await AttachViewer(pair, ground, Vector2.Zero);
            await pair.RunTicksSync(30);

            NetEntity wall = default, item = default, groundNet = default;
            await server.WaitPost(() =>
            {
                groundNet = sEntMan.GetNetEntity(ground);
                wall = sEntMan.GetNetEntity(sEntMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 0.5f))));
                item = sEntMan.GetNetEntity(sEntMan.SpawnEntity("Crowbar", new EntityCoordinates(ground, new Vector2(1.5f, 0.5f))));
            });
            await pair.RunTicksSync(15);
            Assert.That(cEntMan.TryGetEntity(wall, out _), "The client never received the wall.");

            var farMap = await pair.CreateTestMap();
            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            viewer = await AttachViewer(pair, farMap.MapUid, Vector2.Zero);
            await pair.RunTicksSync(awayTicks);

            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            viewer = await AttachViewer(pair, ground, Vector2.Zero);
            await pair.RunTicksSync(40);

            await client.WaitAssertion(() =>
            {
                Assert.That(cEntMan.TryGetEntity(groundNet, out var cGround), "The returning client has no ground layer.");
                foreach (var (net, what) in new[] { (wall, "wall"), (item, "crowbar") })
                {
                    Assert.That(cEntMan.TryGetEntity(net, out var uid), $"The returning client lost the {what}.");
                    var xform = cEntMan.GetComponent<TransformComponent>(uid!.Value);
                    Assert.That(xform.MapUid, Is.EqualTo(cGround), $"The {what} isn't on the ground layer for the returning client.");
                    Assert.That(xform.ParentUid, Is.EqualTo(cGround), $"The {what} isn't parented to the returning ground.");
                }

                Assert.That(cEntMan.GetComponent<TransformComponent>(cEntMan.GetEntity(wall)).Anchored,
                    "The wall isn't anchored for the returning client.");
            });
        }
        finally
        {
            clientMapLog.Level = clientMapLogLevel;
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(PlanetCVars.PvsRangeLimited, false);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Reports 857/921/736: vehicle interiors show their floor but none of their entities. The engine evicts a grid
    /// a client hasn't seen for net.pvs_evict_seconds; coming back must bring its entities back attached.
    /// </summary>
    [Test]
    public async Task EvictedGridComesBackWithItsEntities()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;
        var clientMapLog = pair.Client.ResolveDependency<ILogManager>().GetSawmill("system.map");
        var clientMapLogLevel = clientMapLog.Level;
        clientMapLog.Level = LogLevel.Error;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            server.CfgMan.SetCVar(CVars.NetPvsEvictSeconds, 1f);
        });

        try
        {
            var room = await pair.CreateTestMap();
            NetEntity wall = default, item = default, grid = default;
            await server.WaitPost(() =>
            {
                grid = sEntMan.GetNetEntity(room.Grid.Owner);
                wall = sEntMan.GetNetEntity(sEntMan.SpawnEntity("WallSolid", room.GridCoords));
                item = sEntMan.GetNetEntity(sEntMan.SpawnEntity("Crowbar", room.GridCoords));
            });
            var viewer = await AttachViewer(pair, room.MapUid, Vector2.Zero);
            await pair.RunTicksSync(15);
            Assert.That(cEntMan.TryGetEntity(grid, out _), "The client never received the grid.");
            Assert.That(cEntMan.TryGetEntity(item, out _), "The client never received the crowbar.");
            Assert.That(cEntMan.TryGetEntity(wall, out _), "The client never received the wall.");

            var far = await pair.CreateTestMap();
            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            viewer = await AttachViewer(pair, far.MapUid, Vector2.Zero);
            await pair.RunTicksSync(150);
            Assert.That(cEntMan.TryGetEntity(wall, out _), Is.False,
                "The client still holds the wall, so its return isn't tested.");

            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            await AttachViewer(pair, room.MapUid, Vector2.Zero);
            await pair.RunTicksSync(40);

            NetEntity wallParent = default, itemParent = default;
            await server.WaitPost(() =>
            {
                wallParent = sEntMan.GetNetEntity(sEntMan.GetComponent<TransformComponent>(sEntMan.GetEntity(wall)).ParentUid);
                itemParent = sEntMan.GetNetEntity(sEntMan.GetComponent<TransformComponent>(sEntMan.GetEntity(item)).ParentUid);
            });

            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(cEntMan.TryGetEntity(grid, out _), "The returning client has no grid.");
                foreach (var (net, parent, what) in new[] { (wall, wallParent, "wall"), (item, itemParent, "crowbar") })
                {
                    Assert.That(cEntMan.TryGetEntity(net, out var uid), $"The returning client lost the {what}.");
                    Assert.That(cEntMan.GetComponent<TransformComponent>(uid!.Value).ParentUid,
                        Is.EqualTo(cEntMan.GetEntity(parent)), $"The {what} isn't where the server has it.");
                }
            });
        }
        finally
        {
            clientMapLog.Level = clientMapLogLevel;
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CVars.NetPvsEvictSeconds, 300f);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
        }

        await pair.CleanReturnAsync();
    }
}
