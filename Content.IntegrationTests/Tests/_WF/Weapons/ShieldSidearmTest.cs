#nullable enable
using Content.Shared._Mono.Blocking.Components;
using Content.Shared.Blocking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>
/// The shield rule exempts ballistic sidearms and nothing bigger, whichever base a gun inherits from.
/// </summary>
[TestFixture]
[TestOf(typeof(BlockingSystem))]
public sealed class ShieldSidearmTest
{
    private const string User = "MobHuman";
    private const string Shield = "RiotShield";
    private const string Hclm = "WeaponSubMachineGunVectorNtsfHclm";

    private static readonly string[] Sidearms =
    {
        "WeaponPistolAnaconda",
        "WeaponPistolAnacondaRegistered",
        "WeaponPistolHawk4",
        "WeaponRevolverDragoon",
    };

    /// <summary>The HCLM's exemption, inherited from the revolver base, is switched off; the sidearms' is on.</summary>
    [Test]
    public async Task PrototypesResolveExemption()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            bool Exempt(string id)
            {
                return prototypes.Index<EntityPrototype>(id).TryGetComponent(out CanShootWithShieldComponent? exempt, factory)
                       && exempt.Enabled;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Exempt(Hclm), Is.False, $"{Hclm} is exempt from the shield rule.");
                foreach (var sidearm in Sidearms)
                {
                    Assert.That(Exempt(sidearm), Is.True, $"{sidearm} is not exempt from the shield rule.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Holding a shield, the HCLM is refused and each sidearm still fires; without one the HCLM fires too.</summary>
    [Test]
    public async Task ShieldRefusesOnlyTheHclm()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var hands = entMan.System<SharedHandsSystem>();

        await server.WaitAssertion(() =>
        {
            // The gun system's own attempt, as it raises it on the shooter.
            bool Refused(string gunProto, bool shielded)
            {
                var user = entMan.SpawnEntity(User, map.GridCoords);
                if (shielded)
                    Assert.That(hands.TryPickupAnyHand(user, entMan.SpawnEntity(Shield, map.GridCoords)), Is.True, "no hand took the shield.");

                var gun = entMan.SpawnEntity(gunProto, map.GridCoords);
                Assert.That(hands.TryPickupAnyHand(user, gun), Is.True, $"no hand took {gunProto}.");
                var ev = new ShotAttemptedEvent { User = user, Used = (gun, entMan.GetComponent<GunComponent>(gun)) };
                entMan.EventBus.RaiseLocalEvent(user, ref ev);
                return ev.Cancelled;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Refused(Hclm, true), Is.True, $"{Hclm} fired alongside a shield.");
                Assert.That(Refused(Hclm, false), Is.False, $"{Hclm} would not fire without a shield.");
                foreach (var sidearm in Sidearms)
                {
                    Assert.That(Refused(sidearm, true), Is.False, $"{sidearm} would not fire alongside a shield.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
