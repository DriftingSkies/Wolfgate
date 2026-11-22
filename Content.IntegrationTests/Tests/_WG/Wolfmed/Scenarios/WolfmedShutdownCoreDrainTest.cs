using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server._WG.Wolfmed.Life;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;

/// <summary>
/// A chassis shut down without its pump no longer waits forever: its positronic core burns down over
/// wolfmed.shutdown_core_seconds and it dies, as an arrest runs a brain down.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedShutdownSystem))]
public sealed class WolfmedShutdownCoreDrainTest : WolfmedGameTest
{
    private const float CoreSeconds = 20f;

    [Test]
    public async Task PumplessChassisBurnsOutTest([Values(false, true)] bool flatCell)
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ShutdownCoreSeconds, CoreSeconds);
        var map = await CreateTestMap();
        EntityUid ipc = default;
        EntityUid core = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var organs = graph.GetBodyOrgans(ipc).ToList();
            core = organs.First(o => SEntMan.HasComponent<BrainComponent>(o.Id)).Id;
            Assert.That(graph.RemoveOrgan(organs.First(o => SEntMan.HasComponent<HeartComponent>(o.Id)).Id), Is.True);
            Assert.That(SEntMan.GetComponentOrNull<WolfmedShutdownComponent>(ipc)?.Reason, Is.EqualTo(WolfmedCauseSource.Pump),
                "pulling the pump did not shut the chassis down.");

            // A flat cell as well puts the chassis on low power; the missing pump still burns the core.
            if (flatCell)
            {
                Assert.That(SEntMan.System<PowerCellSystem>().TryGetBatteryFromSlot(ipc, out var cell, out var battery), Is.True);
                SEntMan.System<BatterySystem>().SetCharge(cell!.Value, 0f, battery);
                SEntMan.System<WolfmedShutdownSystem>().Refresh(ipc, false);
                Assert.That(SEntMan.HasComponent<Content.Shared._WG.Synthetics.WGLowPowerComponent>(ipc), Is.True,
                    "a flat cell did not put the chassis on low power.");
            }
        });

        // About two fifths of the clock: the core is going, the chassis is not dead yet.
        await RunSeconds(CoreSeconds * 0.4f);
        await Server.WaitAssertion(() =>
        {
            var health = SEntMan.GetComponent<WolfmedOrganComponent>(core);
            Assert.That(health.Health, Is.LessThan(health.MaxHealth * 0.8f), "the core did not burn down without its pump.");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.False, "the chassis died before its clock ran out.");
        });

        await RunSeconds(CoreSeconds * 0.8f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.True, "a pumpless chassis never died."));
    }
}
