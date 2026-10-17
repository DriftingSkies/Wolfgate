using Content.Server.NPC.Systems;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>A Downed NPC holds its fire, even with the sidearm a Downed player may still shoot.</summary>
[TestFixture]
[TestOf(typeof(NPCCombatSystem))]
public sealed class WFNpcDownedShootingTest
{
    [Test]
    public async Task DownedNpcDoesNotShootTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var combat = entMan.System<NPCCombatSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entMan.SpawnEntity("WeaponPistolMk58", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(mob, gun, checkActionBlocker: false), Is.True);

            Assert.That(combat.CanNpcShoot(mob), Is.True, "a healthy NPC cannot shoot");

            entMan.AddComponent<WolfmedDownedComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.System<ActionBlockerSystem>().CanAttack(mob), Is.True,
                    "Downed no longer lets a sidearm through, so this test checks nothing");
                Assert.That(combat.CanNpcShoot(mob), Is.False, "a Downed NPC can still shoot");
            });
        });

        await pair.CleanReturnAsync();
    }
}
