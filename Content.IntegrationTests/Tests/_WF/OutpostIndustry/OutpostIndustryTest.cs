#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._WF.OutpostIndustry;
using Content.Server._WF.OutpostIndustry.Components;
using Content.Server._WF.Outposts;
using Content.Server._WF.Planets;
using Content.Server.Power.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.OutpostIndustry;
using Content.Shared.Materials;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.OutpostIndustry;

/// <summary>The F10.2 producers: the parts press, the power exporter and the mining bots, and the gate they share.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostIndustrySystem))]
public sealed class OutpostIndustryTest
{
    /// <summary>
    /// On an outpost with its founder home, the press makes a box from its sheets and the exporter builds credit that
    /// cashes out; away from home, over the cap or off an outpost, they stand idle.
    /// </summary>
    [Test]
    public async Task ProducersRunOnlyAtHomeUnderTheCap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var industry = server.System<WFOutpostIndustrySystem>();
        var maps = server.System<SharedMapSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();

        EntityUid press = default, exporter = default, outpost = default;
        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = grid!.Value;
                var gridComp = entMan.GetComponent<MapGridComponent>(outpost);
                press = Powered(entMan, server, "WFOutpostPartsPress", maps.GridTileToLocal(outpost, gridComp, new Vector2i(0, 2)));
                exporter = Powered(entMan, server, "WFOutpostPowerExporter", maps.GridTileToLocal(outpost, gridComp, new Vector2i(2, 2)));
                Assert.That(materials.TryChangeMaterialAmount(press, "Steel", 500), Is.True);
                Assert.That(materials.TryChangeMaterialAmount(press, "Plastic", 200), Is.True);
                Assert.That(industry.Gate(press, out _), Is.Null, "The press may run with its founder home.");
            });
            await pair.RunTicksSync(server.Timing.TickRate * 3);

            await server.WaitAssertion(() =>
            {
                Assert.That(Count(entMan, "WFPressedMachineParts"), Is.EqualTo(1), "The press made one box.");
                Assert.That(materials.GetMaterialAmount(press, "Steel"), Is.Zero, "It used its steel.");

                var credit = entMan.GetComponent<WFPowerExporterComponent>(exporter).Credit;
                Assert.That(credit, Is.GreaterThan(0.5), "The exporter built credit.");
                var cash = industry.CashOut((exporter, entMan.GetComponent<WFPowerExporterComponent>(exporter)), user);
                Assert.That(cash, Is.Not.Null);
                Assert.That(entMan.GetComponent<StackComponent>(cash!.Value).Count, Is.EqualTo((int) Math.Floor(credit)));
                Assert.That(entMan.GetComponent<WFPowerExporterComponent>(exporter).Credit, Is.LessThan(1));

                server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(ground, -60f, 0.5f));
                Assert.That(industry.Gate(exporter, out _), Is.EqualTo("wf-industry-gate-home"), "Nobody is home.");

                server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(ground, 0.5f, 0.5f));
                Assert.That(industry.Gate(exporter, out _), Is.Null, "The founder is home again.");
                server.CfgMan.SetCVar(OutpostIndustryCVars.HourlyCap, 0);
                Assert.That(industry.Gate(exporter, out _), Is.EqualTo("wf-industry-gate-cap"), "Over the hourly cap.");
                server.CfgMan.SetCVar(OutpostIndustryCVars.HourlyCap, OutpostIndustryCVars.HourlyCap.DefaultValue);

                var offOutpost = Powered(entMan, server, "WFOutpostPowerExporter", new EntityCoordinates(ground, 12.5f, 4.5f));
                Assert.That(industry.Gate(offOutpost, out _), Is.EqualTo("wf-industry-gate-outpost"), "Off an outpost it never runs.");
                entMan.DeleteEntity(offOutpost);
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostIndustryCVars.HourlyCap, OutpostIndustryCVars.HourlyCap.DefaultValue));
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>By day a dock's bots drill an ore-bearing rock near the outpost and bring the ore back into the dock.</summary>
    [Test]
    public async Task MiningBotsFillTheDock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false));
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var maps = server.System<SharedMapSystem>();

        EntityUid dock = default;
        try
        {
            await server.WaitAssertion(() =>
            {
                var world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
                Assert.That(server.System<WFPlanetWeatherSystem>().SetMinuteOfDay(world, 720), Is.True, "Noon.");

                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = grid!.Value;
                var gridComp = entMan.GetComponent<MapGridComponent>(outpost);
                dock = Powered(entMan, server, "WFOutpostMiningBotDock", maps.GridTileToLocal(outpost, gridComp, new Vector2i(2, 2)));
                entMan.SpawnEntity("WallRockGold", maps.GridTileToLocal(ground, entMan.GetComponent<MapGridComponent>(ground), new Vector2i(12, 0)));
            });

            var filled = false;
            for (var second = 0; second < 60 && !filled; second++)
            {
                await pair.RunTicksSync(server.Timing.TickRate);
                await server.WaitPost(() => filled = entMan.GetComponent<StorageComponent>(dock).Container.ContainedEntities.Count > 0);
            }

            await server.WaitAssertion(() =>
            {
                var bots = entMan.GetComponent<WFMiningBotDockComponent>(dock).Spawned;
                Assert.That(bots, Has.Count.EqualTo(2), "The dock made its two bots.");
                Assert.That(filled, $"No ore reached the dock in a minute; bots at stages {string.Join(", ", bots.Select(bot => entMan.GetComponent<WFMiningBotComponent>(bot).Stage))}.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static EntityUid Powered(IEntityManager entMan, Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, string id, EntityCoordinates at)
    {
        var uid = entMan.SpawnEntity(id, at);
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
        entMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
        return uid;
    }

    private static int Count(IEntityManager entMan, string proto)
    {
        return entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == proto);
    }
}
