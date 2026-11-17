using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Robust.Client.GameStates;
using Content.Shared._WG.NetTraffic;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Serilog.Events;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

public sealed partial class WGMapScopedPvsTest
{
    private static readonly Regex FullStateSize = new(@"Received Full GameState: to=\d+, sz=(\d+)", RegexOptions.Compiled);

    /// <summary>A full state carries one system's grids instead of all of them.</summary>
    [Test]
    public async Task FullStateShrinksToTheViewedSystem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var sizes = new FullStateSizes();
        pair.Client.ResolveDependency<ILogManager>().RootSawmill.AddHandler(sizes);
        var stateMan = pair.Client.ResolveDependency<IClientGameStateManager>();

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);

        // Six systems with a 40x40 hull each.
        var home = await pair.CreateTestMap();
        var others = new List<MapId>();
        for (var i = 0; i < 5; i++)
        {
            others.Add((await pair.CreateTestMap()).MapId);
        }

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            foreach (var id in others)
            {
                var grid = maps.CreateGridEntity(id);
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = 0; x < 40; x++)
                {
                    for (var y = 0; y < 40; y++)
                    {
                        tiles.Add((new Vector2i(x + 10, y), home.Tile.Tile));
                    }
                }

                maps.SetTiles(grid, grid.Comp, tiles);
            }

            server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, home.MapId)));
        });
        await pair.RunTicksSync(10);

        try
        {
            var everyone = await MeasureFullState(pair, stateMan, sizes);
            await server.WaitPost(() => server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, true));
            await pair.RunTicksSync(20);
            var scoped = await MeasureFullState(pair, stateMan, sizes);

            TestContext.Out.WriteLine($"Full state with six systems: {everyone} bytes sent to everyone, {scoped} bytes scoped.");
            Assert.That(scoped, Is.LessThan(everyone / 2), "scoping did not cut the other systems' grids from the full state");
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, false);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
            await pair.RunTicksSync(10);
        }

        await pair.CleanReturnAsync();
    }

    // The server coalesces full-state requests within a second of real time.
    private static async Task<int> MeasureFullState(Pair.TestPair pair, IClientGameStateManager stateMan, FullStateSizes sizes)
    {
        await Task.Delay(1200);
        sizes.Sizes.Clear();
        await pair.Client.WaitPost(() => stateMan.RequestFullState());
        await pair.RunTicksSync(15);
        Assert.That(sizes.Sizes, Is.Not.Empty, "the client never logged a full state");
        return sizes.Sizes[^1];
    }

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
