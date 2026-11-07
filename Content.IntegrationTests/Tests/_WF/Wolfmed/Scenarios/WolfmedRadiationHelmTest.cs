using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Damage;
using Content.Shared.Shuttles.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>Radiation sickness Downs a pilot without taking the helm from them, so a crew can fly out of fallout.</summary>
[TestFixture]
[TestOf(typeof(WolfmedDownedSystem))]
public sealed class WolfmedRadiationHelmTest : WolfmedGameTest
{
    [Test]
    public async Task RadiationSickPilotKeepsTheHelm()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessRadiationDown, 80f);
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid pilot = default, helm = default, bleeder = default, otherHelm = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            var consoles = SEntMan.System<ShuttleConsoleSystem>();
            pilot = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            helm = SEntMan.SpawnEntity("ComputerShuttle", map.GridCoords.Offset(new Vector2(1f, 0f)));
            SEntMan.EnsureComponent<PilotComponent>(pilot);
            consoles.AddPilot(helm, pilot, SEntMan.GetComponent<ShuttleConsoleComponent>(helm));

            bleeder = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0f, 3f)));
            otherHelm = SEntMan.SpawnEntity("ComputerShuttle", map.GridCoords.Offset(new Vector2(1f, 3f)));
            SEntMan.EnsureComponent<PilotComponent>(bleeder);
            consoles.AddPilot(otherHelm, bleeder, SEntMan.GetComponent<ShuttleConsoleComponent>(otherHelm));
        });
        await RunSeconds(1);

        await Server.WaitPost(() =>
        {
            SEntMan.System<DamageableSystem>().TryChangeDamage(pilot, WolfmedScenario.Spec("Radiation", 100), ignoreResistances: true);
            s.SetBlood(bleeder, 0.45f);
            s.Consciousness.Refresh(pilot);
            s.Consciousness.Refresh(bleeder);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(s.State(pilot), Is.EqualTo(WolfmedConsciousness.Downed), "Radiation 100 should Down the pilot.");
                Assert.That(s.Vitals(pilot).Cause, Is.EqualTo(WolfmedCause.Radiation));
                Assert.That(SEntMan.GetComponent<PilotComponent>(pilot).Console, Is.EqualTo(helm),
                    "A pilot sick with radiation lost the helm.");
                Assert.That(s.State(bleeder), Is.Not.EqualTo(WolfmedConsciousness.Up), "45% blood should put the bleeder down.");
                Assert.That(SEntMan.TryGetComponent<PilotComponent>(bleeder, out var dropped) ? dropped.Console : null, Is.Null,
                    "Only radiation sickness keeps the helm; a body down for other reasons still lets go.");
            });
        });
    }
}
