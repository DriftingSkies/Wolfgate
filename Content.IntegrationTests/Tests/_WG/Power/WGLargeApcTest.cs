#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Power;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Construction.Components;
using Content.Shared.Power.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Power;

/// <summary>
/// How APCs and LAPC substations carry large loads: steady pass-through, the brownout a sudden jump causes,
/// and the headroom stacked APCs or an upgraded LAPC give against it.
/// </summary>
[TestFixture]
public sealed class WGLargeApcTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGPowerTestGenerator
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerSupplier
    supplyRate: 10000000
    supplyRampTolerance: 10000000

- type: entity
  id: WGPowerTestLoad
  components:
  - type: Transform
    anchored: true
  - type: ApcPowerReceiver
    powerLoad: 0
  - type: ExtensionCableReceiver
";

    /// <summary>A small always-on load, like the lights and lathes sharing a network with a big machine.</summary>
    private const float LampLoad = 1000f;

    /// <summary>The live entities of one test layout.</summary>
    private sealed class Rig
    {
        public readonly List<EntityUid> Providers = new();
        public ApcPowerReceiverComponent Lamp = default!;
        public ApcPowerReceiverComponent Machine = default!;
    }

    /// <summary>
    /// Generator on HV, then either a substation and <paramref name="apcs"/> basic APCs on one LV network,
    /// or (with <paramref name="apcs"/> 0) an LAPC fed straight from HV. Both loads sit on the LV network.
    /// </summary>
    private static Rig Build(IEntityManager em, EntityUid gridUid, MapGridComponent grid, Tile tile, int apcs)
    {
        var maps = em.System<SharedMapSystem>();
        var batteries = em.System<BatterySystem>();
        var rig = new Rig();
        for (var y = 0; y < 3; y++)
            maps.SetTile(gridUid, grid, new Vector2i(0, y), tile);

        EntityCoordinates At(int y) => new(gridUid, 0.5f, y + 0.5f);

        em.SpawnEntity("WGPowerTestGenerator", At(0));
        em.SpawnEntity("CableHV", At(0));
        em.SpawnEntity("CableHV", At(1));

        int lv;
        if (apcs == 0)
        {
            rig.Providers.Add(em.SpawnEntity("LargeAPC", At(1)));
            lv = 1;
        }
        else
        {
            var substation = em.SpawnEntity("SubstationBasic", At(1));
            em.SpawnEntity("CableMV", At(1));
            em.SpawnEntity("CableMV", At(2));
            var battery = em.GetComponent<BatteryComponent>(substation);
            batteries.SetCharge(substation, battery.MaxCharge, battery);
            for (var i = 0; i < apcs; i++)
                rig.Providers.Add(em.SpawnEntity("APCBasic", At(2)));
            lv = 2;
        }

        em.SpawnEntity("CableApcExtension", At(lv));
        foreach (var provider in rig.Providers)
        {
            Assert.That(em.GetComponent<TransformComponent>(provider).Anchored, $"{em.ToPrettyString(provider)} should anchor.");
            var battery = em.GetComponent<BatteryComponent>(provider);
            batteries.SetCharge(provider, battery.MaxCharge, battery);
        }

        rig.Lamp = em.GetComponent<ApcPowerReceiverComponent>(em.SpawnEntity("WGPowerTestLoad", At(lv)));
        rig.Machine = em.GetComponent<ApcPowerReceiverComponent>(em.SpawnEntity("WGPowerTestLoad", At(lv)));
        rig.Lamp.Load = LampLoad;
        return rig;
    }

    private static int Ticks(IGameTiming timing, float seconds) => (int) MathF.Ceiling(seconds * timing.TickRate);

    /// <summary>Runs for a while and reports whether the lamp ever lost power.</summary>
    private static async Task<bool> LampFlickers(TestPair pair, Rig rig, float seconds)
    {
        var server = pair.Server;
        var flicker = false;
        for (var i = 0; i < Ticks(server.Timing, seconds); i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => flicker |= !rig.Lamp.Powered);
        }

        return flicker;
    }

    /// <summary>
    /// Builds a rig, settles it with only the lamp on, switches the machine to <paramref name="step"/> and lets it
    /// settle again. Returns whether the lamp lost power on the way.
    /// </summary>
    private static async Task<(Rig Rig, bool Flicker)> StepLoad(TestPair pair, int apcs, float step, string? capacitor = null)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        Rig rig = default!;
        await server.WaitAssertion(() =>
        {
            rig = Build(server.EntMan, map.Grid.Owner, map.Grid.Comp, map.Tile.Tile, apcs);
            if (capacitor != null)
                SetCapacitors(server.EntMan, rig.Providers[0], capacitor);
        });
        await server.WaitRunTicks(Ticks(server.Timing, 5f));
        await server.WaitAssertion(() => Assert.That(rig.Lamp.Powered, "The lamp should be powered before the step."));

        await server.WaitPost(() => rig.Machine.Load = step);
        var flicker = await LampFlickers(pair, rig, 3f);
        await server.WaitRunTicks(Ticks(server.Timing, 10f));
        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(rig.Lamp.Powered, "The lamp should be powered once the network settles.");
            Assert.That(rig.Machine.Powered, $"A {step / 1000f} kW machine should run once the network settles.");
        }));
        return (rig, flicker);
    }

    /// <summary>One APC passes power from MV straight through, so it carries a steady load well past its 15 kW headroom.</summary>
    [Test]
    public async Task SingleApcCarriesSteadyLoadPastItsHeadroom()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (rig, _) = await StepLoad(pair, 1, 40000f);
        await pair.Server.WaitAssertion(() =>
        {
            var apc = pair.Server.EntMan.GetComponent<PowerNetworkBatteryComponent>(rig.Providers[0]);
            Assert.That(apc.CurrentSupply, Is.EqualTo(LampLoad + 40000f).Within(1f));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A jump past one APC's 15 kW headroom browns out every load on its network, not only the new one.</summary>
    [Test]
    public async Task StepPastApcHeadroomBrownsOutTheNetwork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (_, flicker) = await StepLoad(pair, 1, 25000f);
        Assert.That(flicker, "A 25 kW jump on one APC should briefly unpower the lamp beside it.");
        await pair.CleanReturnAsync();
    }

    /// <summary>A second APC on the same network adds its headroom, which is why players stack them.</summary>
    [Test]
    public async Task SecondApcAbsorbsTheSameStep()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (_, flicker) = await StepLoad(pair, 2, 25000f);
        Assert.That(flicker, Is.False, "Two APCs should absorb a 25 kW jump without dropping the lamp.");
        await pair.CleanReturnAsync();
    }

    /// <summary>A stock LAPC has 60 kW of supply and can add all of it at once.</summary>
    [Test]
    public async Task StockLapcAbsorbsAStepThatBrownsOutAnApc()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (rig, flicker) = await StepLoad(pair, 0, 50000f);
        await pair.Server.WaitAssertion(() =>
        {
            var battery = pair.Server.EntMan.GetComponent<PowerNetworkBatteryComponent>(rig.Providers[0]);
            Assert.Multiple(() =>
            {
                Assert.That(battery.MaxSupply, Is.EqualTo(60000f));
                Assert.That(battery.SupplyRampTolerance, Is.EqualTo(60000f));
                Assert.That(flicker, Is.False, "A stock LAPC should absorb a 50 kW jump.");
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Better capacitors raise an LAPC's supply and headroom by half the base per rating step.</summary>
    [TestCase("AdvancedCapacitorStockPart", 120000f)]
    [TestCase("SuperCapacitorStockPart", 150000f)]
    [TestCase("QuadraticCapacitorStockPart", 210000f)]
    public async Task CapacitorsRaiseLapcSupply(string capacitor, float expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var lapc = em.SpawnEntity("LargeAPC", map.GridCoords);
            SetCapacitors(em, lapc, capacitor);
            var battery = em.GetComponent<PowerNetworkBatteryComponent>(lapc);
            Assert.Multiple(() =>
            {
                Assert.That(battery.MaxSupply, Is.EqualTo(expected).Within(0.5f));
                Assert.That(battery.SupplyRampTolerance, Is.EqualTo(expected).Within(0.5f));
                Assert.That(em.GetComponent<WGNetworkBatteryUpgradeComponent>(lapc).Scalar, Is.EqualTo(expected / 60000f).Within(0.001f));
            });

            SetCapacitors(em, lapc, "CapacitorStockPart");
            Assert.That(battery.MaxSupply, Is.EqualTo(60000f).Within(0.5f), "Stock capacitors should return the LAPC to 60 kW.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An LAPC on super capacitors takes a 140 kW jump without dropping the rest of its network.</summary>
    [Test]
    public async Task UpgradedLapcAbsorbsALargeStep()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (_, flicker) = await StepLoad(pair, 0, 140000f, "SuperCapacitorStockPart");
        Assert.That(flicker, Is.False, "An LAPC on super capacitors should absorb a 140 kW jump.");
        await pair.CleanReturnAsync();
    }

    /// <summary>Replaces every capacitor in a machine and refreshes its upgrades.</summary>
    private static void SetCapacitors(IEntityManager em, EntityUid uid, string capacitor)
    {
        var machine = em.GetComponent<MachineComponent>(uid);
        var containers = em.System<SharedContainerSystem>();
        var coords = em.GetComponent<TransformComponent>(uid).Coordinates;
        var replaced = 0;
        foreach (var part in machine.PartContainer.ContainedEntities.ToList())
        {
            if (!em.TryGetComponent<MachinePartComponent>(part, out var comp) || comp.PartType.Id != "Capacitor")
                continue;

            containers.Remove(part, machine.PartContainer);
            em.DeleteEntity(part);
            containers.Insert(em.SpawnEntity(capacitor, coords), machine.PartContainer);
            replaced++;
        }

        Assert.That(replaced, Is.EqualTo(6), "An LAPC should build with six capacitors.");
        em.System<ConstructionSystem>().RefreshParts(uid, machine);
    }
}
