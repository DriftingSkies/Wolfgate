#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._WF.Husbandry;
using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.Outposts;
using Content.Server._WF.Planets;
using Content.Server.NPC.HTN;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Husbandry;

/// <summary>Every world has a tameable native, and livestock left out of a pen is hunted in the Active window.</summary>
[TestFixture]
[TestOf(typeof(WFHusbandrySystem))]
public sealed class NativeLivestockTest
{
    /// <summary>A fauna table on each world that carries its native livestock.</summary>
    private static readonly string[] WorldTables =
    {
        "WFFaunaAsclepiu",
        "WFFaunaVerdaMeadow",
        "WFFaunaMerakFlats",
        "WFFaunaThrasciasTundra",
        "WFFaunaFervidusAshfields",
        "WFFaunaAerumnaForest",
        "WFFaunaCarcinomaMarsh",
    };

    [Test]
    public async Task EachWorldHasATameableSpecies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var table in WorldTables)
            {
                var ids = new List<string>();
                Collect(protos, protos.Index<EntityTablePrototype>(table).Table, ids);
                var tameable = ids.Where(id => protos.Index<EntityPrototype>(id).Components.ContainsKey(factory.GetComponentName(typeof(WFTameableComponent)))).ToList();
                Assert.That(tameable, Is.Not.Empty, $"{table} has no tameable species among {string.Join(", ", ids)}.");
                foreach (var id in tameable)
                {
                    var proto = protos.Index<EntityPrototype>(id);
                    Assert.That(proto.Components.ContainsKey(factory.GetComponentName(typeof(Content.Shared.Animals.UdderComponent)))
                                || proto.Components.ContainsKey(factory.GetComponentName(typeof(Content.Server.Animals.Components.EggLayerComponent)))
                                || proto.Components.ContainsKey(factory.GetComponentName(typeof(Content.Shared.Animals.WoolyComponent))),
                        $"{id} gives neither milk, eggs nor wool.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Two tamed natives, one in a pen and one out on the ground: when the Active window opens the one outside is
    /// prey to a hunter and the penned one isn't, and when it closes neither is.
    /// </summary>
    [Test]
    public async Task UnpennedLivestockIsPreyInTheActiveWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false));
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var husbandry = server.System<WFHusbandrySystem>();
        var maps = server.System<SharedMapSystem>();
        var factions = server.System<NpcFactionSystem>();
        var weather = server.System<WFPlanetWeatherSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        try
        {
            EntityUid penned = default, loose = default, hunter = default, world = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = grid!.Value;
                LivestockSaveTest.BuildPen(entMan, maps, tileDefs, outpost);
                var gridComp = entMan.GetComponent<MapGridComponent>(outpost);

                penned = entMan.SpawnEntity("WFMobLivestockAsclepiu", maps.GridTileToLocal(outpost, gridComp, new Vector2i(5, 1)));
                loose = entMan.SpawnEntity("WFMobLivestockAsclepiu", new EntityCoordinates(ground, -1.5f, -4.5f));
                hunter = entMan.SpawnEntity("MobBearSpace", new EntityCoordinates(ground, -1.5f, 5.5f));
                // Only its factions are looked at; it is kept from actually hunting anyone in the test.
                server.System<HTNSystem>().SetHTNEnabled((hunter, entMan.GetComponent<HTNComponent>(hunter)), false);
                foreach (var animal in new[] { penned, loose })
                {
                    husbandry.Tame((animal, entMan.GetComponent<WFTameableComponent>(animal)));
                }

                world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
                Assert.That(weather.SetMinuteOfDay(world, 720), Is.True);
                Assert.That(Hunts(hunter, WFHusbandrySystem.LivestockFaction), Is.False, "Tame livestock isn't prey by day.");
            });
            await pair.RunTicksSync(server.Timing.TickRate * 6);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<WFLivestockComponent>(loose).Exposed, Is.False, "By day nothing is exposed.");
                Assert.That(weather.SetMinuteOfDay(world, 1080), Is.True, "Dusk opens the window.");
            });
            await pair.RunTicksSync(server.Timing.TickRate * 6);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<WFLivestockComponent>(loose).Exposed, "Out of the pen at dusk, it is exposed.");
                Assert.That(factions.IsMember(loose, WFHusbandrySystem.PreyFaction));
                Assert.That(Hunts(hunter, WFHusbandrySystem.PreyFaction), "The hunter hunts it.");
                Assert.That(entMan.GetComponent<WFLivestockComponent>(penned).Exposed, Is.False, "The penned one is safe.");
                Assert.That(factions.IsMember(penned, WFHusbandrySystem.LivestockFaction));
                Assert.That(weather.SetMinuteOfDay(world, 420), Is.True, "Dawn closes the window.");
            });
            await pair.RunTicksSync(server.Timing.TickRate * 6);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<WFLivestockComponent>(loose).Exposed, Is.False, "At dawn it is safe again.");
                Assert.That(factions.IsMember(loose, WFHusbandrySystem.LivestockFaction));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();

        bool Hunts(EntityUid hunter, string faction)
        {
            var hostile = entMan.GetComponent<NpcFactionMemberComponent>(hunter).HostileFactions;
            return hostile.Contains(faction);
        }
    }

    private static void Collect(IPrototypeManager protos, EntityTableSelector selector, List<string> ids)
    {
        switch (selector)
        {
            case EntSelector ent:
                ids.Add(ent.Id);
                break;
            case NestedSelector nested:
                Collect(protos, protos.Index(nested.TableId).Table, ids);
                break;
            case EntityTableSelectorWithChildrenBase parent:
                foreach (var child in parent.Children)
                {
                    Collect(protos, child, ids);
                }

                break;
        }
    }
}
