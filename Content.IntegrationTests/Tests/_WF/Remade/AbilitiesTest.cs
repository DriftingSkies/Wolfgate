#nullable enable
using Content.Server._WF.Remade.Abilities;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The NPC ability kit: a slasher shouts at the sight of a target, charges when it's in range, and dodges when shot.</summary>
[TestFixture]
public sealed class AbilitiesTest
{
    [Test]
    public async Task ASlasherShoutsThenChargesATargetInRange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid crewman = default;
        EntityUid slasher = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LimbsTest.LayDeck(pair, map);
            crewman = LimbsTest.SpawnCrewman(pair, LimbsTest.At(map, 0, 0));
            slasher = entMan.SpawnEntity("WFRemadeSlasher", LimbsTest.At(map, 10, 0));
        });

        var system = entMan.System<WFAbilitySystem>();
        var abilities = entMan.GetComponent<WFRemadeAbilitiesComponent>(slasher);
        var speed = entMan.GetComponent<MovementSpeedModifierComponent>(slasher);
        var shout = system.Find<WFShoutAbility>(slasher)!;
        var charge = system.Find<WFChargeAbility>(slasher)!;
        var baseSpeed = speed.BaseSprintSpeed;
        var sawWindUp = false;
        var sawRun = false;
        var trace = new System.Text.StringBuilder();

        await LimbsTest.RunUntil(pair, () =>
        {
            trace.Append($"[{timing.CurTime.TotalSeconds:0.0} {abilities.Active?.GetType().Name ?? "-"} v={speed.CurrentSprintSpeed:0.0} d={system.Distance(slasher, crewman):0.0}] ");
            if (abilities.Active is WFChargeAbility)
            {
                sawWindUp |= speed.CurrentSprintSpeed < baseSpeed * 0.1f;
                sawRun |= speed.CurrentSprintSpeed > baseSpeed * 2f;
            }

            return sawRun && abilities.Active == null;
        }, 40);

        await server.WaitAssertion(() =>
        {
            Assert.That(shout.NextUse, Is.GreaterThan(TimeSpan.Zero), "it never shouted at the sight of a target.");
            Assert.That(sawWindUp, Is.True, $"it didn't stand still to wind up: {trace}");
            Assert.That(sawRun, Is.True, $"it never charged: {trace}");
            Assert.That(abilities.Active, Is.Null, "the charge never ended.");
            Assert.That(charge.NextUse, Is.GreaterThan(timing.CurTime), "the charge isn't on cooldown.");
            Assert.That(charge.Hit, Is.True, $"the charge ended without hitting the target: {trace}");
            Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(baseSpeed).Within(0.01f), "its speed didn't come back.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AChargeKnocksAKnockableMobDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entMan.System<WFAbilitySystem>().Knockdown(human, 2f), Is.True);
            Assert.That(entMan.HasComponent<KnockedDownComponent>(human), Is.True);
            entMan.DeleteEntity(human);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASlasherDodgesSidewaysWhenShotAndNotAgainAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        var transforms = entMan.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            LimbsTest.LayDeck(pair, map);
            var shooter = entMan.SpawnEntity("MobCarp", LimbsTest.At(map, 8, 0));
            var slasher = entMan.SpawnEntity("WFRemadeSlasher", LimbsTest.At(map, 0, 0));
            var start = transforms.GetWorldPosition(slasher);

            LimbsTest.Hit(pair, slasher, "Piercing", 5, null, shooter);
            var dodged = transforms.GetWorldPosition(slasher);
            Assert.That((dodged - start).Length(), Is.EqualTo(1.5f).Within(0.1f), "it didn't step away from the shot.");
            Assert.That(MathF.Abs(dodged.X - start.X), Is.LessThan(0.1f), "it stepped along the line of fire, not across it.");
            Assert.That(abilities.Find<WFDodgeAbility>(slasher)!.NextUse, Is.GreaterThan(TimeSpan.Zero));

            LimbsTest.Hit(pair, slasher, "Piercing", 5, null, shooter);
            Assert.That(transforms.GetWorldPosition(slasher), Is.EqualTo(dodged), "it dodged again with the first dodge on cooldown.");

            var close = entMan.SpawnEntity("WFRemadeSlasher", LimbsTest.At(map, -10, -10));
            var near = entMan.SpawnEntity("MobCarp", LimbsTest.At(map, -10, -9));
            var closeStart = transforms.GetWorldPosition(close);
            LimbsTest.Hit(pair, close, "Piercing", 5, null, near);
            Assert.That(transforms.GetWorldPosition(close), Is.EqualTo(closeStart), "it dodged something beside it.");
            entMan.DeleteEntity(slasher);
            entMan.DeleteEntity(close);
        });
        await pair.CleanReturnAsync();
    }
}
