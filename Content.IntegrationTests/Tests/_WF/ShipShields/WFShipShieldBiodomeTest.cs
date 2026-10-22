using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Fixtures;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks the Biodome burns biothreats anywhere inside its hull shield.</summary>
[TestFixture]
public sealed class WFShipShieldBiodomeTest
{
    [Test]
    public async Task BiodomeBurnsChimerasInsideField()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var emitterUid = entities.SpawnEntity("ShieldGeneratorPOIBio", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            entities.System<ShipShieldsSystem>().Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            // Inside the hull, and out in space but still inside the padded field.
            var onHull = entities.SpawnEntity("MobLetoferolBeast", map.GridCoords);
            var nearHull = entities.SpawnEntity("MobLetoferolBeast",
                new EntityCoordinates(map.Grid.Owner, map.GridCoords.Position + new Vector2(3f, 0f)));
            var farAway = entities.SpawnEntity("MobLetoferolBeast",
                new EntityCoordinates(map.Grid.Owner, map.GridCoords.Position + new Vector2(60f, 0f)));
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);

            var dealer = entities.System<FixtureDamageDealerSystem>();
            dealer.Update(2f);
            dealer.Update(0f);

            float Damage(EntityUid uid) => entities.GetComponent<DamageableComponent>(uid).TotalDamage.Float();
            Assert.Multiple(() =>
            {
                Assert.That(Damage(onHull), Is.GreaterThan(0f), "A chimera on the hull must burn.");
                Assert.That(Damage(nearHull), Is.GreaterThan(0f), "A chimera inside the field off the hull must burn.");
                Assert.That(Damage(farAway), Is.Zero, "A chimera outside the field must not burn.");
                Assert.That(Damage(human), Is.Zero, "The field must not burn crew.");
            });
        });
        await pair.CleanReturnAsync();
    }
}
