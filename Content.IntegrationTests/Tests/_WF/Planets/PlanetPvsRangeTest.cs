#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Robust.Client.GameStates;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map.Components;
using Serilog.Events;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Planet layers other than orbit are range-limited in PVS: a client that walks away loses the ground, a client far
/// from the planet never receives it, a viewer standing on it does, and a far client's full state shrinks by the
/// loaded terrain.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetPvsSystem))]
public sealed class PlanetPvsRangeTest
{
    private static readonly Regex FullStateSize = new(@"Received Full GameState: to=\d+, sz=(\d+)", RegexOptions.Compiled);

    [Test]
    public async Task FarClientGetsOrbitOnlyAndGroundWhenStandingOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var pvs = server.System<Robust.Server.GameStates.PvsOverrideSystem>();
        var maps = server.System<SharedMapSystem>();
        var stateMan = client.ResolveDependency<IClientGameStateManager>();
        var sizes = new FullStateSizes();
        var clientLogs = client.ResolveDependency<ILogManager>();
        clientLogs.RootSawmill.AddHandler(sizes);

        // A layer that comes back is a new map entity with its old map id; the stock client warns about the re-use
        // and carries on. The pair fails tests on warnings, so the map sawmill is quietened for this test.
        var clientMapLog = clientLogs.GetSawmill("system.map");
        var clientMapLogLevel = clientMapLog.Level;
        clientMapLog.Level = LogLevel.Error;

        // The pool runs without PVS culling, which would send every planet layer to everyone regardless.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            server.CfgMan.SetCVar(PlanetCVars.PvsRangeLimited, true); // off by default until the engine re-sends a returning layer whole
        });
        await pair.RunTicksSync(2);

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var groundNet = sEntMan.GetNetEntity(ground);
        var orbitNet = sEntMan.GetNetEntity(orbit);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(pvs.IsRangeLimited(orbit), Is.False, "orbit layer must stay force-sent");
                foreach (var layer in layers.Where(layer => layer != orbit))
                {
                    Assert.That(pvs.IsRangeLimited(layer), Is.True, $"layer {sEntMan.ToPrettyString(layer)} is not range-limited");
                }
            });

            // Stand on the ground so terrain loads around the viewer and the client receives the layer.
            var viewer = await AttachViewer(pair, ground, Vector2.Zero);
            await pair.RunTicksSync(30);
            var tiles = 0;
            await server.WaitPost(() => tiles = maps.GetAllTiles(ground, sEntMan.GetComponent<MapGridComponent>(ground)).Count());
            Assert.That(tiles, Is.GreaterThan(0), "no terrain loaded around the ground viewer");
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.True, "viewer on the ground did not receive it");

            // Walk off to another map: the ground leaves view and the client is told to delete it.
            var farMap = await pair.CreateTestMap();
            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            viewer = await AttachViewer(pair, farMap.MapUid, Vector2.Zero);
            await pair.RunTicksSync(20);
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.False, "far client kept the ground layer");
            Assert.That(cEntMan.TryGetEntity(orbitNet, out _), Is.True, "far client lost the orbit layer");

            // Full state for the far client with the layers range-limited. The server coalesces full-state requests
            // within a second of real time, so each one waits that out.
            await Task.Delay(1200);
            sizes.Sizes.Clear();
            await client.WaitPost(() => stateMan.RequestFullState());
            await pair.RunTicksSync(15);
            Assert.That(sizes.Sizes, Is.Not.Empty, "client never logged a full state");
            var limitedSize = sizes.Sizes[^1];
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.False, "full state carried the ground layer");

            // The same full state with the engine's usual force-send restored.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PvsRangeLimited, false));
            await pair.RunTicksSync(15);
            Assert.That(pvs.IsRangeLimited(ground), Is.False, "disabling the cvar did not restore the ground layer");
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.True, "restored ground layer did not reach the far client");

            await Task.Delay(1200);
            sizes.Sizes.Clear();
            await client.WaitPost(() => stateMan.RequestFullState());
            await pair.RunTicksSync(15);
            Assert.That(sizes.Sizes, Is.Not.Empty, "client never logged the second full state");
            var forceSentSize = sizes.Sizes[^1];
            TestContext.Out.WriteLine(
                $"Far-client full state with {tiles} ground tiles loaded: {limitedSize} bytes range-limited, {forceSentSize} bytes force-sent.");
            Assert.That(limitedSize, Is.LessThan(forceSentSize), "range-limiting did not shrink the far client's full state");

            // Back on, the far client drops the ground again; standing on it brings it back.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PvsRangeLimited, true));
            await pair.RunTicksSync(20);
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.False, "far client kept the ground after re-limiting");

            await server.WaitPost(() => sEntMan.DeleteEntity(viewer));
            await AttachViewer(pair, ground, Vector2.Zero);
            await pair.RunTicksSync(15);
            Assert.That(cEntMan.TryGetEntity(groundNet, out _), Is.True, "returning viewer did not receive the ground");
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

    /// <summary>Collects the sizes the client logs for each full game state it applies.</summary>
    private sealed class FullStateSizes : ILogHandler
    {
        public readonly List<int> Sizes = new();

        public void Log(string sawmillName, LogEvent message)
        {
            var match = FullStateSize.Match(message.RenderMessage());
            if (match.Success)
                Sizes.Add(int.Parse(match.Groups[1].Value));
        }
    }
}
