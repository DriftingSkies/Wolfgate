using System.Collections.Generic;
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks visual shutdown tails never retain protection or accumulate across restarts.</summary>
[TestFixture]
public sealed class WGShipShieldTransitionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task TransitionCopiesAreVisualOnlyBoundedAndExpire(bool manual)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var ending = EntityUid.Invalid;

        List<EntityUid> Tails()
        {
            var result = new List<EntityUid>();
            var query = entities.EntityQueryEnumerator<WGShipShieldVisualsComponent>();
            while (query.MoveNext(out var uid, out var visuals))
            {
                if (visuals.Grid == map.Grid.Owner &&
                    visuals.Transition is WGShipShieldTransition.Collapsing or WGShipShieldTransition.Lowering &&
                    !entities.IsQueuedForDeletion(uid))
                    result.Add(uid);
            }
            return result;
        }

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            var system = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(generator);
            var power = entities.EnsureComponent<ApcPowerReceiverComponent>(generator);
            power.Powered = true;
            system.Update(1.5f);
            var active = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var original = entities.GetComponent<WGShipShieldVisualsComponent>(active);
            Assert.That(original.Transition, Is.EqualTo(WGShipShieldTransition.Forming));
            Assert.That(entities.GetComponent<PhysicsComponent>(active).CanCollide, Is.True,
                "Formation must not delay actual protection.");

            if (manual)
                Assert.That(system.SetWolfgateShieldEnabled(map.Grid.Owner, false), Is.True);
            else
            {
                power.Powered = false;
                system.Update(1.5f);
            }
            Assert.That(entities.IsQueuedForDeletion(active), Is.True);
            Assert.That(entities.GetComponent<PhysicsComponent>(active).CanCollide, Is.False,
                "Shutdown must stop interception before the visual tail finishes.");
            Assert.That(entities.HasComponent<ShipShieldedComponent>(map.Grid.Owner), Is.False);
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(map.Grid.Owner).Active, Is.False);
            var tails = Tails();
            Assert.That(tails, Has.Count.EqualTo(1));
            var firstTail = tails[0];
            var copy = entities.GetComponent<WGShipShieldVisualsComponent>(firstTail);
            Assert.That(copy.Transition, Is.EqualTo(manual ? WGShipShieldTransition.Lowering : WGShipShieldTransition.Collapsing));
            Assert.That(copy.Contours, Is.EqualTo(original.Contours));
            Assert.That(copy.Health, Is.EqualTo(original.Health));
            Assert.That(entities.HasComponent<PhysicsComponent>(firstTail), Is.False);
            Assert.That(entities.HasComponent<ShipShieldComponent>(firstTail), Is.False);

            system.SetWolfgateShieldEnabled(map.Grid.Owner, true);
            power.Powered = true;
            emitter.Damage = 0f;
            emitter.Recharging = false;
            system.Update(1.5f);
            var restored = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            Assert.That(restored, Is.Not.EqualTo(active));
            Assert.That(entities.IsQueuedForDeletion(firstTail), Is.True,
                "A new field must retire its predecessor's visual tail.");
            Assert.That(Tails(), Is.Empty);
            system.SetWolfgateShieldEnabled(map.Grid.Owner, false);
            Assert.That(Tails(), Has.Count.EqualTo(1));
            ending = Tails()[0];
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(ending), Is.False, "Visual tails must expire independently of emitter recovery.");
            Assert.That(Tails(), Is.Empty);
            Assert.That(entities.HasComponent<ShipShieldedComponent>(map.Grid.Owner), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
