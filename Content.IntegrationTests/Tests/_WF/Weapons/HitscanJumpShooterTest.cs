using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>A chaining laser never jumps back through the one who fired it.</summary>
[TestFixture]
public sealed class HitscanJumpShooterTest
{
    [Test]
    public async Task ChainSkipsShooter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            // First target ahead, second target behind the shooter: the jump from the first to the second crosses the shooter.
            var shooter = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var ahead = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(3.5f, 0.5f)));
            var behind = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-0.5f, 0.5f)));
            var beam = em.SpawnEntity("RedHeavyLaser", map.GridCoords);

            var ev = new HitscanTraceEvent
            {
                FromCoordinates = em.GetComponent<TransformComponent>(shooter).Coordinates,
                ShotDirection = Vector2.UnitX,
                Gun = beam,
                Shooter = shooter,
            };
            em.EventBus.RaiseLocalEvent(beam, ref ev);

            Assert.That(em.GetComponent<DamageableComponent>(ahead).TotalDamage.Float(), Is.Positive, "The beam missed its first target.");
            Assert.That(em.GetComponent<DamageableComponent>(behind).TotalDamage.Float(), Is.Positive, "The beam never jumped to the second target.");
            Assert.That(em.GetComponent<DamageableComponent>(shooter).TotalDamage.Float(), Is.Zero, "The jump hit the shooter.");
        });

        await pair.CleanReturnAsync();
    }
}
