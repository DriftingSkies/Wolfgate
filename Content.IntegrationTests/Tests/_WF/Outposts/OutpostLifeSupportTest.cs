#nullable enable
using Content.Server._WF.Outposts;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>The atmos and kitchen pack: the CO2 cracker and the synthesizers.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostLifeSupportSystem))]
public sealed class OutpostLifeSupportTest
{
    /// <summary>
    /// A powered cracker turns carbon dioxide into the same moles of oxygen, a mole a second at most, and only what is
    /// there; unpowered, it does nothing.
    /// </summary>
    [Test]
    public async Task CrackerConvertsCo2UpToItsCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var power = server.System<SharedPowerReceiverSystem>();
        EntityUid cracker = default;
        EntityUid unpowered = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetTile(grid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            entMan.EnsureComponent<GridAtmosphereComponent>(grid);
            entMan.EnsureComponent<GasTileOverlayComponent>(grid);
            cracker = entMan.SpawnEntity("WFOutpostCo2Cracker", new EntityCoordinates(grid, 0.5f, 0.5f));
            unpowered = entMan.SpawnEntity("WFOutpostCo2Cracker", new EntityCoordinates(grid, 1.5f, 0.5f));
            power.SetNeedsPower(cracker, false);
            power.SetPowerDisabled(unpowered, true);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(cracker).Anchored, Is.True, "Precondition: the cracker stands anchored.");
            var atmos = server.System<AtmosphereSystem>();
            var off = atmos.GetContainingMixture(unpowered, excite: false);
            var air = atmos.GetContainingMixture(cracker, excite: false);
            Assert.That(air, Is.Not.Null, "Precondition: the cracker has air around it.");
            Assert.That(off, Is.Not.Null, "Precondition: the unpowered cracker has air around it.");

            SetAir(off!, 50f);
            Crack(unpowered, 1f);
            Assert.That(off!.GetMoles(Gas.CarbonDioxide), Is.EqualTo(50f).Within(0.001f), "An unpowered cracker cracked.");

            SetAir(air!, 50f);
            Crack(cracker, 1f);
            Assert.That(air!.GetMoles(Gas.CarbonDioxide), Is.EqualTo(49f).Within(0.001f), "The cracker didn't take a mole in a second.");
            Assert.That(air.GetMoles(Gas.Oxygen), Is.EqualTo(1f).Within(0.001f), "The cracked CO2 didn't become as much oxygen.");

            Crack(cracker, 0.5f);
            Assert.That(air.GetMoles(Gas.CarbonDioxide), Is.EqualTo(48.5f).Within(0.001f), "The rate doesn't follow the time step.");

            SetAir(air, 0.3f);
            Crack(cracker, 1f);
            Assert.That(air.GetMoles(Gas.CarbonDioxide), Is.EqualTo(0f).Within(0.001f), "The last of the CO2 wasn't cracked.");
            Assert.That(air.GetMoles(Gas.Oxygen), Is.EqualTo(0.3f).Within(0.001f), "More oxygen came out than CO2 went in.");
        });

        await pair.CleanReturnAsync();

        void SetAir(GasMixture air, float co2)
        {
            air.Clear();
            air.AdjustMoles(Gas.CarbonDioxide, co2);
        }

        void Crack(EntityUid uid, float dt)
        {
            var ev = new AtmosDeviceUpdateEvent(dt,
                (grid, entMan.GetComponent<GridAtmosphereComponent>(grid), entMan.GetComponent<GasTileOverlayComponent>(grid)), null);
            entMan.EventBus.RaiseLocalEvent(uid, ref ev);
        }
    }

    /// <summary>A powered water synthesizer fills its tank; an unpowered one doesn't.</summary>
    [Test]
    public async Task SynthesizerFillsItsTankWhilePowered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var solutions = server.System<SolutionContainerSystem>();
        var power = server.System<SharedPowerReceiverSystem>();
        EntityUid powered = default;
        EntityUid unpowered = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            powered = entMan.SpawnEntity("WFOutpostWaterSynthesizer", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            unpowered = entMan.SpawnEntity("WFOutpostWaterSynthesizer", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            power.SetNeedsPower(powered, false);
            power.SetPowerDisabled(unpowered, true);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(Water(powered), Is.GreaterThanOrEqualTo(FixedPoint2.New(2)), "The powered synthesizer made too little water.");
            Assert.That(Water(unpowered), Is.EqualTo(FixedPoint2.Zero), "The unpowered synthesizer made water.");
        });

        await pair.CleanReturnAsync();

        FixedPoint2 Water(EntityUid synth)
        {
            Assert.That(solutions.TryGetSolution(synth, "tank", out _, out var tank), Is.True);
            return tank!.GetTotalPrototypeQuantity("Water");
        }
    }
}
