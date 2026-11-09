#nullable enable
using System.Numerics;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Power.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// Outpost weather power: a wind turbine makes power under open sky, more in a storm wind and none once roofed; a solar
/// string makes power by day and none at night. The power gizmos modelled on stock machines run at 0.6 of them.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostPowerSystem))]
public sealed class OutpostPowerTest
{
    private const float Scale = 0.6f;

    [Test]
    public async Task WindAndSunPowerOutposts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var power = server.System<WFOutpostPowerSystem>();
        var weather = server.System<WFPlanetWeatherSystem>();
        var wolfWeather = server.System<SharedWolfWeatherSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();

        EntityUid turbine = default;
        EntityUid solar = default;
        EntityUid world = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = found!.Value;
                var grid = entMan.GetComponent<MapGridComponent>(outpost);

                // From the corner tile: the turbine north of it, the solar string east of it.
                transform.SetCoordinates(user, maps.GridTileToLocal(outpost, grid, new Vector2i(0, 0)));
                turbine = Deploy("WFOutpostWindTurbinePack", Direction.North);
                solar = Deploy("WFOutpostSolarStringPack", Direction.East);

                // Clear skies and still air hold while this runs.
                var networks = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
                while (networks.MoveNext(out var uid, out var network))
                {
                    if (network.GroundMap == ground)
                        world = uid;
                }

                Assert.That(weather.SetWeather(world, null, TimeSpan.FromHours(1)), "Precondition: the weather is held.");
                Assert.That(weather.SetMinuteOfDay(world, 12 * 60), "Precondition: the clock is set to noon.");

                EntityUid Deploy(string packId, Direction facing)
                {
                    transform.SetWorldRotation(user, facing.ToAngle());
                    var pack = entMan.SpawnEntity(packId, entMan.GetComponent<TransformComponent>(user).Coordinates);
                    Assert.That(outposts.TryDeploy(user, (pack, entMan.GetComponent<WFDeployableComponent>(pack)), out var machine, out var why), Is.True,
                        $"{packId} wasn't deployed: {why}.");
                    return machine!.Value;
                }
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() => wolfWeather.SetWind(ground, Vector2.Zero));
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                var calm = entMan.GetComponent<WFWindTurbineComponent>(turbine).Supply;
                Assert.That(entMan.GetComponent<PowerSupplierComponent>(turbine).MaxSupply, Is.EqualTo(calm).Within(1f),
                    "The turbine makes its calm power under open sky.");
                Assert.That(entMan.GetComponent<WFPlanetEnvironmentComponent>(ground).IsNight, Is.False, "Precondition: it is day.");
                Assert.That(entMan.GetComponent<PowerSupplierComponent>(solar).MaxSupply,
                    Is.EqualTo(entMan.GetComponent<WFSolarStringComponent>(solar).Supply).Within(1f), "The solar string peaks at noon.");

                wolfWeather.SetWind(ground, new Vector2(3f, 0f));
            });
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                var comp = entMan.GetComponent<WFWindTurbineComponent>(turbine);
                Assert.That(entMan.GetComponent<PowerSupplierComponent>(turbine).MaxSupply,
                    Is.EqualTo(comp.Supply * (1 + comp.StormBonus)).Within(1f), "A storm wind earns the full bonus.");

                Assert.That(outposts.TryRoof(user, entMan.GetComponent<TransformComponent>(turbine).Coordinates, out var reason), Is.True,
                    $"Roofing was refused: {reason}.");
                Assert.That(weather.SetMinuteOfDay(world, 23 * 60));
            });
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<PowerSupplierComponent>(turbine).MaxSupply, Is.Zero, "A roofed turbine makes nothing.");
                Assert.That(entMan.GetComponent<WFPlanetEnvironmentComponent>(ground).IsNight, Is.True, "Precondition: it is night.");
                Assert.That(entMan.GetComponent<PowerSupplierComponent>(solar).MaxSupply, Is.Zero, "A solar string makes nothing at night.");
                Assert.That(power.GetSolarSupply((solar, entMan.GetComponent<WFSolarStringComponent>(solar), entMan.GetComponent<TransformComponent>(solar))),
                    Is.Zero);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Each power gizmo modelled on a stock machine supplies and stores 0.6 of what that machine does.</summary>
    [Test]
    public async Task ScaleRule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            T Comp<T>(string id) where T : IComponent, new()
            {
                Assert.That(protos.Index(id).TryGetComponent<T>(out var comp, factory), $"{id} has no {typeof(T).Name}.");
                return comp!;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Comp<PowerSupplierComponent>("WFOutpostCompactRTG").MaxSupply,
                    Is.EqualTo(Scale * Comp<PowerSupplierComponent>("GeneratorRTG").MaxSupply).Within(1f), "Compact RTG supply");

                var fuel = Comp<FuelGeneratorComponent>("WFOutpostFuelGenerator");
                var superPacman = Comp<FuelGeneratorComponent>("PortableGeneratorSuperPacman");
                Assert.That(fuel.TargetPower, Is.EqualTo(Scale * superPacman.TargetPower).Within(1f), "Fuel generator target power");
                Assert.That(fuel.MinTargetPower, Is.EqualTo(Scale * superPacman.MinTargetPower).Within(1f), "Fuel generator minimum power");
                Assert.That(fuel.MaxTargetPower, Is.EqualTo(Scale * superPacman.MaxTargetPower).Within(1f), "Fuel generator maximum power");
                Assert.That(fuel.OptimalPower, Is.EqualTo(Scale * superPacman.OptimalPower).Within(1f), "Fuel generator optimal power");

                foreach (var (outpost, stock) in new[] { ("WFOutpostBatteryLarge", "SMESBasic"), ("WFOutpostBatterySmall", "SubstationBasic") })
                {
                    Assert.That(Comp<Content.Shared.Power.Components.BatteryComponent>(outpost).MaxCharge,
                        Is.EqualTo(Scale * Comp<Content.Shared.Power.Components.BatteryComponent>(stock).MaxCharge).Within(1f), $"{outpost} capacity");
                    var ours = Comp<PowerNetworkBatteryComponent>(outpost);
                    var theirs = Comp<PowerNetworkBatteryComponent>(stock);
                    Assert.That(ours.MaxSupply, Is.EqualTo(Scale * theirs.MaxSupply).Within(1f), $"{outpost} supply");
                    Assert.That(ours.MaxChargeRate, Is.EqualTo(Scale * theirs.MaxChargeRate).Within(1f), $"{outpost} charge rate");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Outpost generators and batteries sit on MV, as on a ship, so an outpost's machines need an APC to turn it into LV.
    /// </summary>
    [Test]
    public async Task GeneratorsAndBatteriesFeedMediumVoltage([Values(
        "WFOutpostWindTurbine", "WFOutpostSolarString", "WFOutpostCompactRTG", "WFOutpostFuelGenerator",
        "WFOutpostBatterySmall", "WFOutpostBatteryLarge")] string id)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index(id);
            Assert.That(proto.TryGetComponent<NodeContainerComponent>(out var nodes, factory), $"{id} has no nodes.");
            Assert.Multiple(() =>
            {
                foreach (var (name, node) in nodes!.Nodes)
                {
                    Assert.That(node.NodeGroupID, Is.EqualTo(NodeGroupID.MVPower), $"{id}'s {name} node isn't on MV.");
                }

                if (proto.TryGetComponent<PowerSupplierComponent>(out var supplier, factory))
                    Assert.That(supplier.Voltage, Is.EqualTo(Voltage.Medium), $"{id} doesn't supply MV.");
                if (proto.TryGetComponent<BatteryDischargerComponent>(out var discharger, factory))
                    Assert.That(discharger.Voltage, Is.EqualTo(Voltage.Medium), $"{id} doesn't discharge onto MV.");
                if (proto.TryGetComponent<BatteryChargerComponent>(out var charger, factory))
                    Assert.That(charger.Voltage, Is.EqualTo(Voltage.Medium), $"{id} doesn't charge from MV.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
