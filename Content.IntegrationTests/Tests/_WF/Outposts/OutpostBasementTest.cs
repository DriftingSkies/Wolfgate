#nullable enable
using System.Linq;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>F10.4: an outpost's basement, one depth down on the cavern, reached by a hatch and kept by its saves.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostBasementTest
{
    /// <summary>The hatch, east of the console so the tile north of it, where the ladder comes up, is clear.</summary>
    private static readonly Vector2i Hatch = new(2, 0);

    /// <summary>
    /// A hatch kit on a 5 x 5 outpost digs a 3 x 3 room on the cavern below, lined up with the floor, opens the floor over
    /// a ladder and binds the two grids with a connector; a plate against the room's rock digs a tile further. The save
    /// carries the basement under its depth, deleting the outpost puts the cavern back, and the load brings it all back.
    /// </summary>
    [Test]
    public async Task HatchDigsABasementThatSavesAndLoads()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var ticker = server.System<GameTicker>();
        var owner = pair.Player!.UserId;
        var world = await OutpostCavernTest.Prepare(pair);

        try
        {
            var (first, user) = await OutpostCavernTest.Found(pair, world);
            var funding = Task.CompletedTask;
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(OutpostCVars.LoadDelay, 1f);
                var prefs = server.ResolveDependency<IServerPreferencesManager>();
                var p = prefs.GetPreferences(owner);
                funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, ((HumanoidCharacterProfile) p.SelectedCharacter).WithBankBalance(1_000_000));
                entMan.EnsureComponent<BankAccountComponent>(user);
            });
            await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);

            EntityUid basement = default, connector = default;
            Vector2i under = default;
            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(first);
                var plating = new Tile(tileDefs["Plating"].TileId);
                for (var x = -1; x <= 3; x++)
                for (var y = -1; y <= 3; y++)
                    maps.SetTile(first, grid, new Vector2i(x, y), plating);

                var outpost = entMan.GetComponent<WFOutpostComponent>(first);
                Assert.That(outposts.TryDigHatch(user, Kit(entMan, first), maps.GridTileToLocal(first, grid, new Vector2i(0, 3)), out var edge),
                    Is.False, "A hatch on the edge was dug.");
                Assert.That(edge, Is.EqualTo("wf-outpost-hatch-edge"));
                Assert.That(outposts.TryDigHatch(user, Kit(entMan, first), maps.GridTileToLocal(first, grid, Hatch), out var reason),
                    Is.True, $"The hatch was refused: {reason}.");

                Assert.That(outposts.TryGetBasement(outpost, out var below), Is.True, "No basement was dug.");
                basement = below.Owner;
                Assert.That(entMan.GetComponent<TransformComponent>(basement).MapUid, Is.EqualTo(world.Cavern), "The basement isn't on the cavern.");
                Assert.That(outposts.CountTiles((basement, below.Comp2)), Is.EqualTo(9));
                Assert.That(maps.GridTileToWorldPos(basement, below.Comp2, Hatch), Is.EqualTo(maps.GridTileToWorldPos(first, grid, Hatch)),
                    "The basement doesn't line up with the floor above.");
                Assert.That(maps.TryGetTileRef(first, grid, Hatch, out var hole) && !hole.Tile.IsEmpty, Is.False, "The hatch is still floored.");
                Assert.That(outpost.Hatches, Is.EqualTo(new[] { Hatch }));
                Assert.That(Protos(entMan, maps, (basement, below.Comp2), Hatch), Is.EqualTo(new[] { "WFOutpostBasementLadder" }));
                connector = maps.GetAnchoredEntities(basement, below.Comp2, Hatch + Vector2i.One).Single();
                Assert.That(Proto(entMan, connector), Is.EqualTo("CEZGridConnector"));

                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                under = maps.TileIndicesFor(world.Cavern, cavern, new MapCoordinates(maps.GridTileToWorldPos(first, grid, Hatch),
                    entMan.GetComponent<TransformComponent>(world.Cavern).MapID));
                Assert.That(maps.TryGetTileRef(world.Cavern, cavern, under, out var dug) && !dug.Tile.IsEmpty, Is.False,
                    "The cavern still has its own floor under the basement.");

                Assert.That(outposts.TryDigHatch(user, Kit(entMan, first), maps.GridTileToLocal(first, grid, Hatch), out _), Is.False,
                    "A hatch was dug where one is open.");

                var east = new EntityCoordinates(world.Cavern, maps.GridTileToWorldPos(basement, below.Comp2, Hatch + new Vector2i(2, 0)));
                Assert.That(outposts.TryExtend(user, "Plating", east, out var plate), Is.True, $"The plate didn't dig the basement out: {plate}.");
                Assert.That(outposts.CountTiles((basement, below.Comp2)), Is.EqualTo(10));
            });

            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<CEZGridConnectorSystem>().TryGetConnectorLink(connector,
                    entMan.GetComponent<TransformComponent>(connector), out var lower, out var upper), Is.True, "The connector binds nothing.");
                Assert.That((lower, upper), Is.EqualTo((basement, first)));
                outposts.GetSlots(owner);
            });
            await Settle(pair, outposts, owner);

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TrySave(user, (first, entMan.GetComponent<WFOutpostComponent>(first)), 0, out var reason), Is.True,
                    $"Saving was refused: {reason}.");
            });
            await Settle(pair, outposts, owner);

            var stored = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(stored, Is.Not.Null);
            var yaml = WFOutpostSystem.Unpack(stored!.Value.Data);
            Assert.That(yaml, Does.Contain(WFOutpostSystem.DepthsKey), "The save has no grids below the outpost.");
            Assert.That(yaml, Does.Contain("WFOutpostBasementLadder"), "The basement's ladder isn't in the save.");
            Assert.That(stored.Value.Save.Tiles, Is.EqualTo(25 - 1 + 10), "The save's floor doesn't count both depths.");

            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(first);
                Assert.That(entMan.Deleted(basement), "The basement outlived its outpost.");
            });
            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                Assert.That(maps.TryGetTileRef(world.Cavern, cavern, under, out var back) && !back.Tile.IsEmpty, Is.True,
                    "The cavern wasn't put back under the basement.");
            });
            await db.SetOutpostSaveAsync(owner, stored.Value.Save with { SavedRound = ticker.RoundId - 1000, LoadedRound = ticker.RoundId - 1000 },
                stored.Value.Data);
            await server.WaitPost(() => outposts.ForgetSlots(owner));
            await server.WaitPost(() => outposts.GetSlots(owner));
            await Settle(pair, outposts, owner);

            var (second, loader) = await OutpostCavernTest.Found(pair, world);
            await server.WaitAssertion(() =>
            {
                entMan.EnsureComponent<BankAccountComponent>(loader);
                Assert.That(outposts.TryStartLoad(loader, (second, entMan.GetComponent<WFOutpostComponent>(second)), 0, out var reason), Is.True,
                    $"The load was refused: {reason}.");
            });
            await Settle(pair, outposts, owner);
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryGetOutpost(owner, out var loaded), Is.True, "The save is the owner's outpost.");
                Assert.That(outposts.TryGetBasement(loaded.Comp, out var below), Is.True, "The basement didn't come back with the save.");
                Assert.That(entMan.GetComponent<TransformComponent>(below.Owner).MapUid, Is.EqualTo(world.Cavern));
                Assert.That(outposts.CountTiles((below.Owner, below.Comp2)), Is.EqualTo(10));
                var grid = entMan.GetComponent<MapGridComponent>(loaded);
                Assert.That(maps.GridTileToWorldPos(below.Owner, below.Comp2, Hatch), Is.EqualTo(maps.GridTileToWorldPos(loaded, grid, Hatch)),
                    "The loaded basement doesn't line up with the floor above.");
                Assert.That(Protos(entMan, maps, (below.Owner, below.Comp2), Hatch), Is.EqualTo(new[] { "WFOutpostBasementLadder" }));
                Assert.That(maps.TryGetTileRef(loaded, grid, Hatch, out var hole) && !hole.Tile.IsEmpty, Is.False, "The hatch came back floored.");
                var cavern = entMan.GetComponent<MapGridComponent>(world.Cavern);
                Assert.That(maps.TryGetTileRef(world.Cavern, cavern, under, out var dug) && !dug.Tile.IsEmpty, Is.False,
                    "The cavern wasn't dug out under the loaded basement.");
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                Assert.That(maps.TryGetTileRef(world.Ground, groundGrid, under, out var shaft) && !shaft.Tile.IsEmpty, Is.False,
                    "The ground under the hatch filled in.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    private static Entity<WFOutpostHatchKitComponent> Kit(IEntityManager entMan, EntityUid outpost)
    {
        var kit = entMan.SpawnEntity("WFOutpostBasementHatchKit", new EntityCoordinates(outpost, 1.5f, 1.5f));
        return (kit, entMan.GetComponent<WFOutpostHatchKitComponent>(kit));
    }

    private static async Task Settle(Pair.TestPair pair, WFOutpostSystem outposts, Robust.Shared.Network.NetUserId owner)
    {
        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(pair.Server, () => !outposts.IsBusy(owner), maxTicks: 600);
    }

    private static string?[] Protos(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i index)
    {
        return maps.GetAnchoredEntities(grid, grid.Comp, index).Select(uid => Proto(entMan, uid)).ToArray();
    }

    private static string? Proto(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
    }
}
