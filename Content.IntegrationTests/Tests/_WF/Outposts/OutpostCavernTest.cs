#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Server.Parallax;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Outposts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Outposts over caverns: foundation rock under a claim, ground there kept shut, and raids that tunnel in.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostCavernTest
{
    private const string Surface = "WFSurfaceMerak";

    /// <summary>Far from the gate mouth near the planet's centre.</summary>
    internal static readonly Vector2i Site = new(96, 96);

    /// <summary>
    /// A cavern chunk under a claim loads as foundation rock and nothing else, the ground over it won't open (a hole cut
    /// in it fills again), and a tunnelling raid finds a way up just outside the claim.
    /// </summary>
    [Test]
    public async Task TheCavernUnderAClaimIsFoundationRock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var world = await Prepare(pair);

        try
        {
            var (outpost, user) = await Found(pair, world);
            (Vector2i Min, Vector2i Max) zone = default;
            await server.WaitPost(() =>
            {
                outposts.RefreshFoundations();
                zone = outposts.GetClaimZone((outpost, entMan.GetComponent<WFOutpostComponent>(outpost), entMan.GetComponent<MapGridComponent>(outpost)));
            });

            await LoadChunks(pair, world.Cavern, zone.Min - new Vector2i(ChunkSize, ChunkSize), zone.Max + new Vector2i(ChunkSize, ChunkSize));

            var margin = new Vector2i(zone.Min.X + 1, Site.Y);
            await server.WaitAssertion(() =>
            {
                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var rocks = 0;
                for (var x = zone.Min.X; x <= zone.Max.X; x++)
                for (var y = zone.Min.Y; y <= zone.Max.Y; y++)
                {
                    var index = new Vector2i(x, y);
                    if (!maps.TryGetTileRef(world.Cavern, cavern, index, out var tile) || tile.Tile.IsEmpty)
                        continue;

                    var anchored = maps.GetAnchoredEntities(world.Cavern, cavern, index).ToList();
                    Assert.That(anchored.Select(e => Proto(entMan, e)), Is.EqualTo(new[] { BiomeSystem.WfFoundationRock.Id }),
                        $"The cavern at {index}, under the claim, isn't foundation rock alone.");
                    rocks++;
                }

                Assert.That(rocks, Is.GreaterThan(0));

                var outside = zone.Max + new Vector2i(2, 2);
                Assert.That(maps.GetAnchoredEntities(world.Cavern, cavern, outside).Select(e => Proto(entMan, e)),
                    Does.Not.Contain(BiomeSystem.WfFoundationRock.Id), "Foundation rock reached past the claim.");

                var mouths = server.System<WFCavernMouthSystem>();
                Assert.That(mouths.MayOpen(world.Ground, Site, Site), Is.False, "The ground over a foundation may open.");
                Assert.That(mouths.MayOpen(world.Ground, outside, outside), Is.True);

                // A hole cut in the claim's margin.
                var ground = entMan.GetComponent<MapGridComponent>(world.Ground);
                maps.SetTile(world.Ground, ground, margin, Tile.Empty);
            });

            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<MapGridComponent>(world.Ground);
                Assert.That(maps.TryGetTileRef(world.Ground, ground, margin, out var tile) && !tile.Tile.IsEmpty, Is.True,
                    "A hole over the foundation stayed open.");
                Assert.That(entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades, Does.Not.ContainKey(margin));

                // Tunnellers come up within a few tiles of the claim, outside it.
                var raids = server.System<WFRaidDirectorSystem>();
                var entity = (outpost, entMan.GetComponent<WFOutpostComponent>(outpost), entMan.GetComponent<MapGridComponent>(outpost));
                Assert.That(raids.TryFindTunnelExit(entity, out var spot, out var edge), Is.True, "A tunnelling raid found no way up.");
                var exit = maps.TileIndicesFor(world.Ground, ground, spot);
                Assert.That(outposts.TryGetClaim(world.Ground, exit, null, out _), Is.False, "The raid came up inside the claim.");
                var gap = Math.Max(Math.Max(zone.Min.X - exit.X, exit.X - zone.Max.X), Math.Max(zone.Min.Y - exit.Y, exit.Y - zone.Max.Y));
                Assert.That(gap, Is.InRange(1, 6), "The raid came up far from the claim.");
                Assert.That(edge, Is.EqualTo(spot));
            });

            // Every world's raid table has a tunnelling pack.
            await server.WaitAssertion(() =>
            {
                foreach (var table in server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<WFRaidTablePrototype>())
                    Assert.That(table.Packs.Any(p => p.Tunnel), Is.True, $"{table.ID} sends no tunnellers.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An outpost founded over a tunnel already loaded leaves the tunnel as it is, and its save is the same before and
    /// after the cavern under it loads: nothing below the ground is in it.
    /// </summary>
    [Test]
    public async Task AnOutpostOverATunnelKeepsItAndSavesTheSame()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!.UserId;
        var world = await Prepare(pair);
        var from = Site - new Vector2i(16, 16);
        var to = Site + new Vector2i(16, 16);
        await LoadChunks(pair, world.Cavern, from, to);

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }

        try
        {
            var tunnel = new HashSet<EntityUid>();
            var open = 0;
            await server.WaitPost(() =>
            {
                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                for (var x = from.X; x <= to.X; x++)
                for (var y = from.Y; y <= to.Y; y++)
                {
                    var anchored = maps.GetAnchoredEntities(world.Cavern, cavern, new Vector2i(x, y)).ToList();
                    tunnel.UnionWith(anchored);
                    if (anchored.Count == 0)
                        open++;
                }
            });
            Assert.That(open, Is.GreaterThan(0), "The cavern under the site has no tunnel to keep.");

            var (outpost, user) = await Found(pair, world);
            await server.WaitPost(() => outposts.GetSlots(owner));
            await Settle();
            await server.WaitAssertion(() =>
                Assert.That(outposts.TrySave(user, (outpost, entMan.GetComponent<WFOutpostComponent>(outpost)), 0, out var reason), Is.True, reason));
            await Settle();

            await server.WaitPost(() => outposts.RefreshFoundations());
            await LoadChunks(pair, world.Cavern, from, to);
            await pair.RunTicksSync(30);

            await server.WaitAssertion(() =>
            {
                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var after = new HashSet<EntityUid>();
                for (var x = from.X; x <= to.X; x++)
                for (var y = from.Y; y <= to.Y; y++)
                    after.UnionWith(maps.GetAnchoredEntities(world.Cavern, cavern, new Vector2i(x, y)));
                Assert.That(after, Is.EquivalentTo(tunnel), "A tunnel loaded before the claim changed under it.");
                Assert.That(outposts.TrySave(user, (outpost, entMan.GetComponent<WFOutpostComponent>(outpost)), 1, out var reason), Is.True, reason);
            });
            await Settle();

            var first = await db.GetOutpostSaveDataAsync(owner, 0);
            var second = await db.GetOutpostSaveDataAsync(owner, 1);
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.Value.Save.Tiles, Is.EqualTo(first!.Value.Save.Tiles));
            Assert.That(second.Value.Save.Appraisal, Is.EqualTo(first.Value.Save.Appraisal));
            // Its air and weather move on between the two; what it holds must not.
            var firstProtos = Protos(WFOutpostSystem.Unpack(first.Value.Data));
            var secondProtos = Protos(WFOutpostSystem.Unpack(second.Value.Data));
            Assert.That(secondProtos, Is.EqualTo(firstProtos), "The save took in something from below.");
            Assert.That(secondProtos, Does.Not.Contain(BiomeSystem.WfFoundationRock.Id));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A cavern world with outposts on and the lazy mouth claims off, so only the gate is cut.</summary>
    internal static async Task<World> Prepare(TestPair pair)
    {
        await EnableCaverns(pair);
        await DisableClaims(pair);
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(OutpostCVars.Enabled, true));
        var world = await BuildWorld(pair, Surface);
        Assert.That(world.Cavern, Is.Not.EqualTo(EntityUid.Invalid));
        return world;
    }

    /// <summary>Puts the session's mob at the site on a cleared, floored patch and founds an outpost there.</summary>
    internal static async Task<(EntityUid Outpost, EntityUid User)> Found(TestPair pair, World world)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var user = await AttachViewer(pair, world.Ground, Site + new Vector2(0.5f, -4.5f));
        await pair.RunTicksSync(30);
        await LayTiles(pair, world.Ground, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));

        EntityUid outpost = default;
        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
            {
                foreach (var ent in maps.GetAnchoredEntities(world.Ground, grid, Site + new Vector2i(x, y)).ToList())
                    entMan.DeleteEntity(ent);
            }

            var at = new EntityCoordinates(world.Ground, Site + new Vector2(0.5f));
            var kit = entMan.SpawnEntity("WFOutpostConsoleKit", at);
            Assert.That(server.System<WFOutpostSystem>().TryFound(user, (kit, entMan.GetComponent<WFOutpostKitComponent>(kit)), at,
                out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
            outpost = found!.Value;
        });

        await pair.RunTicksSync(5);
        return (outpost, user);
    }

    /// <summary>The prototypes a save's entities are grouped under, in order.</summary>
    private static List<string> Protos(string yaml)
    {
        return yaml.Split('\n')
            .Where(line => line.StartsWith("- proto:"))
            .Select(line => line["- proto:".Length..].Trim())
            .ToList();
    }

    private static string? Proto(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
    }
}
