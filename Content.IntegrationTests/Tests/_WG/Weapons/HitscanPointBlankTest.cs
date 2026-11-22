using System.Numerics;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>A hitscan shot at a target on the shooter's own spot has no direction, and is dropped rather than traced.</summary>
[TestFixture]
public sealed class HitscanPointBlankTest
{
    [Test]
    public async Task ShotWithNoDirectionIsDropped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var shooter = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var beam = em.SpawnEntity("RedLaser", map.GridCoords);
            var from = em.GetComponent<TransformComponent>(shooter).Coordinates;

            // Traced as it was, the zero direction normalised to NaN and failed the engine's ray assert.
            Assert.DoesNotThrow(() => em.System<SharedGunSystem>().ShootHitscan(beam, from, Vector2.Zero, beam, shooter));
        });

        await pair.CleanReturnAsync();
    }
}
