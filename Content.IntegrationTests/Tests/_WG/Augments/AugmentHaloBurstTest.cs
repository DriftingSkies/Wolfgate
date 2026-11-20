using System.Collections.Generic;
using Content.Server._WG.Augments;
using Content.Server.Atmos.Components;
using Content.Shared._WG.Augments;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The Corona frag and concussion charges: each model throws only its own ring of orbs, holds the wearer still in a
/// shield until they go off together, blasts and throws back the people round the wearer but never the wearer, spends
/// one charge per ring, shows the wait on its button, and brings spent charges back.
/// </summary>
[TestFixture]
[TestOf(typeof(WGBurstLauncherSystem))]
public sealed class AugmentHaloBurstTest
{
    [Test]
    public async Task BurstTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var blocker = server.System<ActionBlockerSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();

        EntityUid wearer = default, target = default, stunAction = default, burstAction = default;
        await server.WaitAssertion(() =>
        {
            wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0f)));
            // The test map is airless, and low pressure deals Blunt.
            entities.RemoveComponent<BarotraumaComponent>(wearer);
            entities.RemoveComponent<BarotraumaComponent>(target);
            humanoids.LoadProfile(wearer, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithAugmentImplants(["WGAugmentHaloStun", "WGAugmentHaloBurst"]));

            Assert.That(entities.TryGetComponent<WGHaloStunComponent>(wearer, out var stun), "The stun model wasn't fitted.");
            Assert.That(entities.TryGetComponent<WGHaloBurstComponent>(wearer, out var burst), "The lethal model wasn't fitted.");
            stunAction = stun!.ActionEntity!.Value;
            burstAction = burst!.ActionEntity!.Value;
            Assert.That(Charges(stunAction), Is.EqualTo(2));
            Assert.That(Charges(burstAction), Is.EqualTo(2));
        });

        InstantActionComponent Action(EntityUid action) => entities.GetComponent<InstantActionComponent>(action);
        int? Charges(EntityUid action) => Action(action).Charges;

        // Only what the blasts deal.
        float BlastDamage(EntityUid body)
        {
            var dict = entities.GetComponent<DamageableComponent>(body).Damage.DamageDict;
            return (dict.GetValueOrDefault("Piercing") + dict.GetValueOrDefault("Heat")).Float();
        }

        int Orbs()
        {
            var count = 0;
            var query = entities.EntityQueryEnumerator<WGHaloOrbComponent>();
            while (query.MoveNext(out _, out _))
                count++;
            return count;
        }

        async Task Press(EntityUid action)
        {
            await server.WaitPost(() =>
            {
                var comp = Action(action);
                actions.PerformAction(wearer, null, action, comp, comp.BaseEvent, timing.CurTime, false);
            });
            await pair.RunTicksSync(5);
        }

        // Past the two-second fuse.
        Task WaitFuse() => pair.RunSeconds(2.5f);

        // Thrown: the wearer is held in the shield, the orbs hang round them, and nothing has gone off yet.
        await Press(stunAction);
        EntityUid? shield = null;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WGHaloArmingComponent>(wearer, out var arming), "The wearer isn't held.");
            Assert.That(blocker.CanMove(wearer), Is.False, "The wearer can walk while the orbs are out.");
            shield = arming!.Shield;
            Assert.That(entities.EntityExists(shield), "There is no shield.");
            Assert.That(Orbs(), Is.EqualTo(entities.GetComponent<WGHaloStunComponent>(wearer).Orbs));
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.EqualTo(0f), "The orbs went off at once.");
            Assert.That(Charges(stunAction), Is.EqualTo(1));
            Assert.That(Action(stunAction).Cooldown?.End, Is.EqualTo(arming.End), "The button doesn't show the fuse.");
        });

        // The other model can't throw a second ring while one is out.
        await Press(burstAction);
        await server.WaitAssertion(() => Assert.That(Charges(burstAction), Is.EqualTo(2), "A second ring was thrown."));

        // The orbs come to rest round the wearer, a tile and a half out.
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            var centre = xforms.GetWorldPosition(wearer);
            var reach = entities.GetComponent<WGHaloStunComponent>(wearer).OrbDistance;
            var query = entities.EntityQueryEnumerator<WGHaloOrbComponent, PhysicsComponent>();
            while (query.MoveNext(out var orb, out _, out var body))
            {
                Assert.That((xforms.GetWorldPosition(orb) - centre).Length(), Is.EqualTo(reach).Within(0.3f), "An orb didn't stop where it should.");
                Assert.That(body.LinearVelocity.Length(), Is.LessThan(0.01f), "An orb is still drifting.");
            }
        });

        // Gone off: the target is winded, the wearer is untouched and free, and the shield and orbs are gone.
        await WaitFuse();
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.GreaterThan(0f), "The stun ring missed.");
            Assert.That(entities.GetComponent<StaminaComponent>(wearer).StaminaDamage, Is.EqualTo(0f), "The wearer was hit.");
            Assert.That(entities.HasComponent<WGHaloArmingComponent>(wearer), Is.False);
            Assert.That(blocker.CanMove(wearer), "The wearer is still held.");
            Assert.That(entities.EntityExists(shield), Is.False, "The shield outlived the fuse.");
            Assert.That(Orbs(), Is.Zero);
            var xforms = server.System<SharedTransformSystem>();
            var thrown = (xforms.GetWorldPosition(target) - xforms.GetWorldPosition(wearer)).Length();
            Assert.That(thrown, Is.GreaterThan(1.4f), "The blast didn't throw the target back.");
        });

        // The lethal ring wounds a fresh target, the first having been thrown clear, and still leaves the wearer alone.
        EntityUid second = default;
        await server.WaitPost(() =>
        {
            second = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(-1f, 0f)));
            entities.RemoveComponent<BarotraumaComponent>(second);
        });
        await Press(burstAction);
        await WaitFuse();
        await server.WaitAssertion(() =>
        {
            Assert.That(BlastDamage(second), Is.GreaterThan(0f), "The lethal ring missed.");
            Assert.That(BlastDamage(wearer), Is.EqualTo(0f), "The wearer was hit.");
            Assert.That(Charges(burstAction), Is.EqualTo(1));
        });

        // Emptied, the action switches off and its button counts down to the next charge, which turns it back on.
        await Press(stunAction);
        await WaitFuse();
        await server.WaitAssertion(() =>
        {
            var stun = entities.GetComponent<WGHaloStunComponent>(wearer);
            Assert.That(Charges(stunAction), Is.EqualTo(0));
            Assert.That(Action(stunAction).Enabled, Is.False);
            Assert.That(stun.NextCharge, Is.Not.Null, "No recharge was started.");
            Assert.That(Action(stunAction).Cooldown?.End, Is.EqualTo(stun.NextCharge), "The button doesn't show the recharge.");
            stun.NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var stun = entities.GetComponent<WGHaloStunComponent>(wearer);
            Assert.That(Charges(stunAction), Is.EqualTo(1));
            Assert.That(Action(stunAction).Enabled, Is.True, "The recharged action stayed off.");
            Assert.That(stun.NextCharge, Is.Not.Null, "The second charge isn't coming back.");
            stun.NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(Charges(stunAction), Is.EqualTo(2));
            Assert.That(entities.GetComponent<WGHaloStunComponent>(wearer).NextCharge, Is.Null);
        });

        // Removing the augment takes its action with it.
        await server.WaitPost(() => entities.RemoveComponent<WGHaloStunComponent>(wearer));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(stunAction), "The action outlived the augment."));

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(wearer);
            entities.DeleteEntity(target);
            entities.DeleteEntity(second);
        });
        await pair.CleanReturnAsync();
    }
}
