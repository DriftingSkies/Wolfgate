#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WG.Wolfmed.Life;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;

/// <summary>
/// A stopped heart leaves the patient on their feet for wolfmed.arrest_collapse_seconds (45): slowed with no sprint,
/// Downed about two thirds in, Unconscious and Dying at the end. The brain clock runs from the stop, not the collapse.
/// A shock or a pulled heart still drops the body at once.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedArrestCollapseTest : WolfmedGameTest
{
    /// <summary>The shipped window; the test pool turns the collapse off for every other arrest test.</summary>
    private const float CollapseSeconds = 45f;

    [SetUp]
    public async Task CollapseOn() =>
        await Server.WaitPost(() => Server.CfgMan.SetCVar(WolfmedCVars.ArrestCollapseSeconds, CollapseSeconds));

    [TearDown]
    public async Task CollapseOff() =>
        await Server.WaitPost(() => Server.CfgMan.SetCVar(WolfmedCVars.ArrestCollapseSeconds, 0f));

    private async Task<(WolfmedScenario Scenario, EntityUid Body)> Patient()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        return (s, body);
    }

    [Test]
    public async Task StoppedHeartFadesBeforeTheCollapse()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            s.Consciousness.Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up), "the patient dropped the moment the heart stopped.");
                Assert.That(s.Life.ArrestCollapsed(body), Is.False);
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(body), Is.False, "Dying actions before the collapse.");
            });

            s.Advance(body, 20);
            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up), "down already 20 s into the collapse.");
                Assert.That(speed.WalkSpeedModifier, Is.LessThan(1f), "no slowdown with the heart stopped.");
                Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(speed.CurrentWalkSpeed).Within(0.01f), "still sprinting with no pulse.");
            });

            var downed = s.Advance(body, 30, _ => s.State(body) != WolfmedConsciousness.Up);
            Assert.That(20 + downed, Is.InRange(28, 36), "the legs did not give out about two thirds into the collapse.");
            Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Downed));

            var outAt = s.Advance(body, 30, _ => s.State(body) == WolfmedConsciousness.Unconscious);
            var oxygen = s.Life.GetOxygenation(body);
            Assert.Multiple(() =>
            {
                Assert.That(20 + downed + outAt, Is.InRange(44, 47), "the collapse did not end at 45 s.");
                Assert.That(s.Life.ArrestCollapsed(body), Is.True);
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(body), Is.True, "no Dying actions after the collapse.");
                Assert.That(oxygen, Is.LessThan(0.8f), "the brain clock waited for the collapse instead of the stop.");
            });
        });
    }

    [Test]
    public async Task ShockStopsTheHeartAtOnce()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "shock"), Is.True);
            s.Consciousness.Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Unconscious), "a shock arrest left the patient standing.");
                Assert.That(s.Life.ArrestCollapsed(body), Is.True);
            });
        });
    }

    [Test]
    public async Task HeartAttackSmiteHoldsWithAHealthyHeart()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, WolfmedHeartAttackSmiteSystem.Cause), Is.True);
            s.Advance(body, 50);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(body), Is.True, "a healthy heart restarted the smite's arrest by itself.");
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Unconscious));
            });
        });
    }

    [Test]
    public async Task RestartDuringTheCollapseLiftsIt()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            s.Advance(body, 25);
            Assert.That(s.Life.EndArrest(body), Is.True);
            s.Advance(body, 5);
            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up));
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.01f), "the slowdown outlived the arrest.");
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(body), Is.False);
            });
        });
    }
}
