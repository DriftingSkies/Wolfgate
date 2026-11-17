#nullable enable
using Content.Shared._Mono.Shipyard;
using Content.Shared._WG.Performance;
using Content.Shared.Projectiles;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>
/// States built after a referenced entity is deleted carry no live reference to it. The pair fails the test on any
/// logged error, which is what a generated state did.
/// </summary>
[TestFixture]
[TestOf(typeof(WGDeletedRefStateSystem))]
public sealed class WGDeletedRefStateTest
{
    [Test]
    public async Task DeletedReferencesAreSentQuietly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var shooter = entMan.Spawn();
            var weapon = entMan.Spawn();
            var liveCard = entMan.Spawn();
            var goneCard = entMan.Spawn();

            var projectile = entMan.Spawn();
            var shot = entMan.AddComponent<ProjectileComponent>(projectile);
            shot.Shooter = shooter;
            shot.Weapon = weapon;
            shot.ProjectileSpent = true;

            var ship = entMan.Spawn();
            var guests = entMan.AddComponent<ShipGuestAccessComponent>(ship);
            guests.GuestIdCards.Add(liveCard);
            guests.GuestIdCards.Add(goneCard);

            var live = (WGProjectileState) entMan.GetComponentState(entMan.EventBus, shot, null, GameTick.Zero)!;
            Assert.That(live.Weapon, Is.EqualTo(entMan.GetNetEntity(weapon)));

            var station = entMan.Spawn();
            var key = new StationRecordKey(1, station);
            var liveCardNet = entMan.GetNetEntity(liveCard);

            entMan.DeleteEntity(shooter);
            entMan.DeleteEntity(weapon);
            entMan.DeleteEntity(goneCard);
            entMan.DeleteEntity(station);

            var state = (WGProjectileState) entMan.GetComponentState(entMan.EventBus, shot, null, GameTick.Zero)!;
            var guestState = (WGShipGuestAccessState) entMan.GetComponentState(entMan.EventBus, guests, null, GameTick.Zero)!;
            var convertedKey = entMan.System<SharedStationRecordsSystem>().Convert(key);

            Assert.Multiple(() =>
            {
                // Invalid, not null: a shot whose gun was deleted still counts as shot.
                Assert.That(state.Shooter, Is.EqualTo(NetEntity.Invalid));
                Assert.That(state.Weapon, Is.EqualTo(NetEntity.Invalid));
                Assert.That(state.ProjectileSpent, Is.True);
                Assert.That(guestState.IdCards, Is.EquivalentTo(new[] { liveCardNet }));
                Assert.That(convertedKey.Item1, Is.EqualTo(NetEntity.Invalid));
            });

            entMan.DeleteEntity(liveCard);
            entMan.DeleteEntity(projectile);
            entMan.DeleteEntity(ship);
        });

        await pair.CleanReturnAsync();
    }
}
