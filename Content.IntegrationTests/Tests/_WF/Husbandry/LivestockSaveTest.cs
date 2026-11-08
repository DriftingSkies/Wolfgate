#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._NF.Bank;
using Content.Server._WF.Husbandry;
using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Animals.Components;
using Content.Server.Animals.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.NPC.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Husbandry;

/// <summary>Livestock penned on an outpost goes into its save as records and comes back at the feeder when it loads.</summary>
[TestFixture]
[TestOf(typeof(WFHusbandrySystem))]
public sealed class LivestockSaveTest
{
    internal static readonly Vector2i PenMin = new(3, 0);
    internal static readonly Vector2i PenMax = new(7, 4);
    internal static readonly Vector2i FeederTile = new(5, 3);

    /// <summary>Walls in a 3 x 3 pen east of an outpost's founding square, its feeder against the north wall.</summary>
    internal static void BuildPen(IEntityManager entMan, SharedMapSystem maps, ITileDefinitionManager tileDefs, EntityUid outpost)
    {
        var grid = entMan.GetComponent<MapGridComponent>(outpost);
        var plating = new Tile(tileDefs["Plating"].TileId);
        for (var x = PenMin.X; x <= PenMax.X; x++)
        for (var y = PenMin.Y; y <= PenMax.Y; y++)
        {
            var tile = new Vector2i(x, y);
            maps.SetTile(outpost, grid, tile, plating);
            if (x == PenMin.X || x == PenMax.X || y == PenMin.Y || y == PenMax.Y)
                entMan.SpawnEntity("WallSolid", maps.GridTileToLocal(outpost, grid, tile));
        }

        entMan.SpawnEntity("WFOutpostAnimalFeeder", maps.GridTileToLocal(outpost, grid, FeederTile));
    }

    /// <summary>Whether an entity stands inside the pen <see cref="BuildPen"/> walls in.</summary>
    internal static bool InPen(IEntityManager entMan, SharedMapSystem maps, EntityUid outpost, EntityCoordinates coords)
    {
        var tile = maps.LocalToTile(outpost, entMan.GetComponent<MapGridComponent>(outpost), coords);
        return tile.X > PenMin.X && tile.X < PenMax.X && tile.Y > PenMin.Y && tile.Y < PenMax.Y;
    }

    /// <summary>
    /// Two chickens in a walled pen with a feeder, one renamed and slow to lay, and a third loose outside it: the appraisal
    /// counts the two, the save records them, and after a load the same two stand in the pen with their name and cooldown.
    /// </summary>
    [Test]
    public async Task PennedChickensComeBackWithTheSave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var husbandry = server.System<WFHusbandrySystem>();
        var maps = server.System<SharedMapSystem>();
        var pricing = server.System<PricingSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var ticker = server.System<GameTicker>();
        var timing = server.ResolveDependency<IGameTiming>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var owner = pair.Player!.UserId;

        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(OutpostCVars.LoadDelay, 1f);
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(1_000_000));
            entMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }

        Entity<WFOutpostComponent> Found()
        {
            var site = OutpostFoundingTest.Site;
            Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
            return (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
        }

        List<EntityUid> Chickens(EntityUid grid)
        {
            var found = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "MobChicken")
                    found.Add(child);
            }

            return found;
        }

        bool InPen(EntityUid grid, EntityUid animal) =>
            LivestockSaveTest.InPen(entMan, maps, grid, entMan.GetComponent<TransformComponent>(animal).Coordinates);

        try
        {
            EntityUid first = default;
            await server.WaitAssertion(() =>
            {
                first = Found().Owner;
                BuildPen(entMan, maps, tileDefs, first);
            });
            await pair.RunTicksSync(5);

            var without = 0;
            var withChickens = 0;
            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(first);
                without = outposts.Appraise((first, grid));
                var pens = husbandry.GetPens((first, grid));
                Assert.That(pens, Has.Count.EqualTo(1), "The walled square round the feeder is a pen.");
                Assert.That(pens[0].Open, Has.Count.EqualTo(8));

                var hen = entMan.SpawnEntity("MobChicken", maps.GridTileToLocal(first, grid, new Vector2i(4, 1)));
                var other = entMan.SpawnEntity("MobChicken", maps.GridTileToLocal(first, grid, new Vector2i(6, 1)));
                entMan.SpawnEntity("MobChicken", maps.GridTileToLocal(first, grid, new Vector2i(0, 2)));
                server.System<MetaDataSystem>().SetEntityName(hen, "Henrietta");
                server.System<EggLayerSystem>().WfSetNextGrowth(entMan.GetComponent<EggLayerComponent>(hen), timing.CurTime + TimeSpan.FromSeconds(500));

                var price = pricing.GetPrice(hen) + pricing.GetPrice(other);
                Assert.That(price, Is.GreaterThan(0));
                withChickens = outposts.Appraise((first, grid));
                Assert.That(withChickens, Is.EqualTo(without + price).Within(1), "The appraisal counts the two penned chickens and not the loose one.");
                Assert.That(husbandry.GetPens((first, grid))[0].Livestock, Is.EquivalentTo(new[] { hen, other }));
                outposts.GetSlots(owner);
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TrySave(user, (first, entMan.GetComponent<WFOutpostComponent>(first)), 0, out var reason), Is.True,
                    $"Saving was refused: {reason}.");
                Assert.That(entMan.HasComponent<WFLivestockRecordsComponent>(first), Is.False, "The records live only in the save.");
                foreach (var chicken in Chickens(first).Where(c => InPen(first, c)))
                {
                    Assert.That(entMan.HasComponent<WFLivestockComponent>(chicken), "A penned chicken is livestock.");
                    Assert.That(server.System<NpcFactionSystem>().IsMember(chicken, WFHusbandrySystem.LivestockFaction));
                }
            });
            await Settle();

            var stored = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Value.Save.Appraisal, Is.EqualTo(withChickens));
            var yaml = WFOutpostSystem.Unpack(stored.Value.Data);
            Assert.That(Regex.Matches(yaml, @"prototype: MobChicken").Count, Is.EqualTo(2), "The two penned chickens are recorded.");
            Assert.That(yaml, Does.Contain("name: Henrietta"));

            await server.WaitAssertion(() => entMan.DeleteEntity(first));
            await db.SetOutpostSaveAsync(owner, stored.Value.Save with { SavedRound = ticker.RoundId - 1000, LoadedRound = ticker.RoundId - 1000 }, stored.Value.Data);
            await server.WaitPost(() => outposts.ForgetSlots(owner));
            await server.WaitPost(() => outposts.GetSlots(owner));
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryStartLoad(user, Found(), 0, out var reason), Is.True, $"The load was refused: {reason}.");
            });
            await Settle();
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryGetOutpost(owner, out var loaded), Is.True, "The save is the owner's outpost.");
                var chickens = Chickens(loaded);
                Assert.That(chickens, Has.Count.EqualTo(2), "The two penned chickens came back, and the loose one didn't.");
                Assert.That(chickens.All(c => InPen(loaded, c)), "They came back in the pen.");
                Assert.That(chickens.All(c => entMan.HasComponent<WFLivestockComponent>(c)));
                Assert.That(chickens.All(c => server.System<NpcFactionSystem>().IsMember(c, WFHusbandrySystem.LivestockFaction)));
                Assert.That(entMan.HasComponent<WFLivestockRecordsComponent>(loaded), Is.False, "The records were spent.");

                var hen = chickens.Single(c => entMan.GetComponent<MetaDataComponent>(c).EntityName == "Henrietta");
                Assert.That(entMan.GetComponent<EggLayerComponent>(hen).NextGrowth - timing.CurTime, Is.GreaterThan(TimeSpan.FromSeconds(300)),
                    "The hen kept her cooldown.");
                Assert.That(husbandry.GetAge((hen, entMan.GetComponent<WFLivestockComponent>(hen))), Is.GreaterThan(TimeSpan.Zero));
                Assert.That(husbandry.GetPens((loaded.Owner, entMan.GetComponent<MapGridComponent>(loaded)))[0].Livestock, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
