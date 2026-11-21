#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.CombatMode;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using ClientMeleeSystem = Content.Client.Weapons.Melee.MeleeWeaponSystem;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// A mob that is its own gun, fired with the secondary key, must not also disarm on that key: the disarm puts the mob
/// on melee cooldown, which cancels the shot.
/// </summary>
public sealed class SecondaryKeyGunTest : InteractionTest
{
    protected override string PlayerPrototype => "WGSecondaryKeyTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGSecondaryKeyTestMob
  parent: InteractionTestMob
  components:
  - type: CombatMode
  - type: MeleeWeapon
    damage:
      types:
        Heat: 5
  - type: ProjectileBatteryAmmoProvider
    proto: BulletMonolithicGestalt
    fireCost: 100
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
  - type: Gun
    fireRate: 2
    useKey: false
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
";

    [TestCase("MobMonolithicGestalt", true)]
    [TestCase("MobMonolithicGolomRanged", true)]
    [TestCase("WeaponCrusher", true)]
    [TestCase("WeaponLaserGun", false)] // fired with the primary key; its melee is AltFireMelee
    [TestCase("InteractionTestMob", false)] // no gun
    public async Task SecondaryKeyGoesToTheGun(string proto, bool expected)
    {
        var owns = !expected;
        await Client.WaitPost(() =>
        {
            var uid = CEntMan.SpawnEntity(proto, MapCoordinates.Nullspace);
            owns = CEntMan.System<ClientMeleeSystem>().WGGunOwnsSecondaryKey(uid);
            CEntMan.DeleteEntity(uid);
        });

        Assert.That(owns, Is.EqualTo(expected));
    }

    [Test]
    public async Task SelfGunFiresWithoutTheDisarm()
    {
        await Server.WaitPost(() => SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true));
        await RunTicks(5);

        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        // What the client sent before the fix on a right-click: a body disarm, then the shot.
        await Client.WaitPost(() =>
        {
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new DisarmAttackEvent(null, aim));
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent { Gun = Player, Coordinates = aim });
        });
        await RunTicks(5);
        Assert.That(await Charge(), Is.EqualTo(1000f), "the shot fired despite the disarm's melee cooldown");

        await RunSeconds(1.5f);

        // What it sends now: the shot alone.
        await Client.WaitPost(() =>
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent { Gun = Player, Coordinates = aim }));
        await RunTicks(5);
        Assert.That(await Charge(), Is.LessThan(1000f), "a player-controlled self-gun did not fire");
    }

    private async Task<float> Charge()
    {
        var charge = 0f;
        await Server.WaitPost(() => charge = SEntMan.GetComponent<BatteryComponent>(SPlayer).CurrentCharge);
        return charge;
    }
}
