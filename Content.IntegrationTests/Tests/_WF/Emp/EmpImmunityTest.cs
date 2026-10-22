using System.Numerics;
using Content.Server.Emp;
using Content.Shared.Emp;
using Content.Shared.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Emp;

/// <summary>
/// An EMP disables ordinary computers but leaves shuttle consoles and APCs running.
/// </summary>
public sealed class EmpImmunityTest
{
    [Test]
    public async Task EmpSkipsConsolesAndApcs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var emp = entManager.System<EmpSystem>();

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            var console = entManager.SpawnEntity("ComputerShuttle", coords);
            var apc = entManager.SpawnEntity("APCBasic", coords);
            var other = entManager.SpawnEntity("ComputerAlert", coords);

            var battery = entManager.GetComponent<BatteryComponent>(apc);
            var charge = battery.CurrentCharge;

            emp.EmpPulse(coords, 2f, 100000f, TimeSpan.FromSeconds(30));

            Assert.That(entManager.HasComponent<EmpDisabledComponent>(other), Is.True,
                "An ordinary computer should be disabled by the EMP.");
            Assert.That(entManager.HasComponent<EmpDisabledComponent>(console), Is.False,
                "A shuttle console should ignore the EMP.");
            Assert.That(entManager.HasComponent<EmpDisabledComponent>(apc), Is.False,
                "An APC should ignore the EMP.");
            Assert.That(battery.CurrentCharge, Is.EqualTo(charge),
                "An APC's battery should not be drained by the EMP.");
        });

        await pair.CleanReturnAsync();
    }
}
