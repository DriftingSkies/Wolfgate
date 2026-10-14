#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.CombatMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using ClientGunSystem = Content.Client.Weapons.Ranged.Systems.GunSystem;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A spin-up gun shoots faster the longer it fires, and winds down again once it stops.</summary>
public sealed class VehicleGunSpinupTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFSpinupTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFSpinupTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: WFSpinupTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFSpinupTestGun
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 1.5
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: FullAuto
    availableModes:
    - FullAuto
  - type: GunSpinup
    baseShotDelay: 0.7
    baseScatter: 18
    spinUpTime: 10
    graceAfterStop: 2
    spinDownTime: 3
    minSpinLevel: 1
    maxSpinLevel: 11
    rateTiers: [1, 1, 2, 2, 3, 3, 3, 4, 4, 4, 5]
  - type: BallisticAmmoProvider
    cycleable: false
    proto: WFSpinupTestCartridge
    capacity: 500
";

    [Test]
    public async Task AGunSpinsUpWhileFiringAndDownWhenItStops()
    {
        var gun = EntityUid.Invalid;
        await Server.WaitPost(() => gun = SEntMan.SpawnEntity("WFSpinupTestGun", MapData.GridCoords));
        await RunTicks(5);

        var guns = SEntMan.System<SharedGunSystem>();
        var gunComp = SEntMan.GetComponent<GunComponent>(gun);
        var spin = SEntMan.GetComponent<GunSpinupComponent>(gun);
        var target = new EntityCoordinates(gun, new System.Numerics.Vector2(6f, 0f));

        var startRate = 0f;
        await Server.WaitPost(() => startRate = gunComp.FireRateModified);
        Assert.That(startRate, Is.EqualTo(1f / 0.7f).Within(0.05f), "The gun does not start at the first tier.");

        // Hold the trigger for twelve seconds.
        var ticks = (int) (12f / STiming.TickPeriod.TotalSeconds);
        var shots = 0;
        for (var i = 0; i < ticks; i++)
        {
            await Server.WaitPost(() =>
            {
                var before = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun).Count;
                guns.AttemptShoot(SPlayer, gun, gunComp, target);
                if (SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun).Count < before)
                    shots++;

                var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
                var shot = new List<EntityUid>();
                while (query.MoveNext(out var uid, out _))
                {
                    shot.Add(uid);
                }

                foreach (var uid in shot)
                {
                    SEntMan.DeleteEntity(uid);
                }
            });
            await RunTicks(1);
        }

        var spunUp = 0f;
        await Server.WaitPost(() => spunUp = gunComp.FireRateModified);
        Assert.That(spunUp, Is.GreaterThan(startRate * 3f), "The gun did not speed up while it fired.");
        Assert.That(shots, Is.GreaterThan((int) (12f * startRate)), "The gun fired no faster than at its first tier.");
        Assert.That(spin.CurrentSpinLevel, Is.GreaterThan(5f));

        // Let go: after the grace and the spin-down it is back at the first tier.
        await RunSeconds(8f);
        var wound = 0f;
        await Server.WaitPost(() => wound = gunComp.FireRateModified);
        Assert.That(wound, Is.LessThan(startRate * 1.2f), "The gun stayed spun up after it stopped.");
    }

    /// <summary>
    /// The shooter's client fires its own copy of every shot, so it has to wind the gun up in step with the server: one
    /// predicted copy for each of the server's rounds, and the same spin-up on both sides.
    /// </summary>
    [Test]
    public async Task TheShootersClientSpinsTheGunUpInStepWithTheServer()
    {
        var gun = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            gun = SEntMan.SpawnEntity("WFSpinupTestGun", Transform.GetMoverCoordinates(SPlayer));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, gun), Is.True, "The user could not hold the gun.");
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
        });
        await RunTicks(10);

        var netGun = SEntMan.GetNetEntity(gun);
        var aim = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, new System.Numerics.Vector2(8f, 0.5f)));
        var cGun = CEntMan.GetEntity(netGun);
        var cGunComp = CEntMan.GetComponent<GunComponent>(cGun);
        var sGunComp = SEntMan.GetComponent<GunComponent>(gun);

        var predicted = 0;
        var served = 0;
        var lastRequest = -10;
        var ticks = (int) (10f / STiming.TickPeriod.TotalSeconds);
        for (var i = 0; i < ticks; i++)
        {
            // What the client's gun system does each tick the trigger is held and the gun is ready. The server's word on when it
            // is ready stands in for the replay of inputs a real client does when each state arrives.
            var ready = false;
            await Server.WaitPost(() => ready = sGunComp.NextFire <= STiming.CurTime);

            // A request takes a tick to reach the server, which has not moved its cooldown on until then.
            if (i - lastRequest < 3)
                ready = false;

            await Client.WaitPost(() =>
            {
                if (!ready)
                    return;

                lastRequest = i;
                var guns = CEntMan.System<ClientGunSystem>();
                var slots = guns.ShootRequested(netGun, aim, null, null, ClientSession, true);
                CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent
                {
                    Gun = netGun,
                    Coordinates = aim,
                    Shot = slots,
                    Predicted = guns.DrewHitscan,
                });

                if (slots == null)
                    return;

                foreach (var id in slots.Where(id => id != 0))
                {
                    predicted++;
                    var copy = new EntityUid(id);
                    if (CEntMan.EntityExists(copy))
                        CEntMan.DeleteEntity(copy);
                }
            });
            await RunTicks(1);

            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
                var found = new List<EntityUid>();
                while (query.MoveNext(out var uid, out _))
                {
                    found.Add(uid);
                }

                served += found.Count;
                foreach (var uid in found)
                {
                    SEntMan.DeleteEntity(uid);
                }
            });
        }

        await RunTicks(5);

        var serverRate = 0f;
        var clientRate = 0f;
        var serverLevel = 0f;
        var clientLevel = 0f;
        await Server.WaitPost(() =>
        {
            serverRate = sGunComp.FireRateModified;
            serverLevel = SEntMan.GetComponent<GunSpinupComponent>(gun).CurrentSpinLevel;
        });
        await Client.WaitPost(() =>
        {
            clientRate = cGunComp.FireRateModified;
            clientLevel = CEntMan.GetComponent<GunSpinupComponent>(cGun).CurrentSpinLevel;
        });

        // Once the gun has been idle past its grace the spin ends, and the stop sound plays once for the shooter: the server's. The
        // shooter's client must not play a copy of its own.
        var stopPath = ((SoundPathSpecifier) CEntMan.GetComponent<GunSpinupComponent>(cGun).StopSound!).Path.ToString();
        var serverStops = 0;
        var clientStops = 0;
        var ticksForStop = (int) (6f / STiming.TickPeriod.TotalSeconds);
        for (var i = 0; i < ticksForStop; i++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => serverStops = Math.Max(serverStops, CountAudio(SEntMan, stopPath, false)));
            await Client.WaitPost(() => clientStops = Math.Max(clientStops, CountAudio(CEntMan, stopPath, true)));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(served, Is.GreaterThan(10), "The server fired too little to tell.");
            Assert.That(predicted, Is.EqualTo(served).Within(1), "The client did not predict a copy of each of the server's rounds.");
            Assert.That(serverLevel, Is.GreaterThan(5f), "The server's gun did not spin up.");
            Assert.That(clientLevel, Is.EqualTo(serverLevel).Within(1.5f), "The client's gun spun up differently from the server's.");
            Assert.That(clientRate, Is.EqualTo(serverRate).Within(serverRate * 0.25f), "The client's gun shoots at another rate than the server's.");
            Assert.That(serverStops, Is.EqualTo(1), "The server should play the stop sound once.");
            Assert.That(clientStops, Is.Zero, "The shooter's client played a stop sound of its own, doubling the server's.");
        }
    }

    /// <summary>How many audio entities play <paramref name="path"/>, counting only the ones the client made itself if asked.</summary>
    private static int CountAudio(IEntityManager entMan, string path, bool clientSideOnly)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (audio.FileName == path && (!clientSideOnly || entMan.IsClientSide(uid)))
                count++;
        }

        return count;
    }
}
