using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._WG.ShipShields;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Content.Server.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks collision candidates do not charge shields before an actual projectile hit.</summary>
[TestFixture]
public sealed class WGShipShieldProjectileContactTest
{
    [Test]
    public async Task BroadphaseChecksDoNotDamageShieldButProjectileHitsDo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            var maps = entities.System<SharedMapSystem>();
            maps.SetTile(map.Grid, new Robust.Shared.Maths.Vector2i(200, 100), map.Tile.Tile);
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var emitterUid = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            var projectileUid = entities.SpawnEntity("BulletDebugZoom", new EntityCoordinates(map.Grid.Owner, -3f, 0.5f));
            entities.EnsureComponent<ShipWeaponProjectileComponent>(projectileUid);
            var projectile = entities.GetComponent<ProjectileComponent>(projectileUid);
            var projectileBody = entities.GetComponent<PhysicsComponent>(projectileUid);
            var shieldBody = entities.GetComponent<PhysicsComponent>(shield);
            var shieldFixture = entities.GetComponent<FixturesComponent>(shield).Fixtures.Values.First();
            var projectileFixture = entities.GetComponent<FixturesComponent>(projectileUid).Fixtures.Values.First();
            var broadphase = new PreventCollideEvent(shield, projectileUid, shieldBody, projectileBody, shieldFixture, projectileFixture);
            entities.EventBus.RaiseLocalEvent(shield, ref broadphase);
            Assert.That(broadphase.Cancelled, Is.False);
            Assert.That(emitter.Damage, Is.Zero, "A broadphase candidate is not an impact.");
            Assert.That(projectile.ProjectileSpent, Is.False);

            entities.System<ProjectileSystem>().ProjectileCollide((projectileUid, projectile, projectileBody), shield);
            Assert.That(emitter.Damage, Is.EqualTo((float)projectile.Damage.GetTotal()));
            Assert.That(projectile.ProjectileSpent, Is.True);
            entities.System<ProjectileSystem>().ProjectileCollide((projectileUid, projectile, projectileBody), shield);
            Assert.That(emitter.Damage, Is.EqualTo((float)projectile.Damage.GetTotal()), "A spent shot cannot charge twice.");
            var audioState = entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner);
            var echo = audioState.ActiveImpactSound!.Value;
            var audio = entities.GetComponent<AudioComponent>(echo);
            Assert.That(audio.Params.Volume, Is.EqualTo(4f));
            Assert.That((audioState.NextImpactSound - server.Timing.CurTime).TotalSeconds, Is.InRange(0.75d, 1.1d));
            Assert.That(entities.GetComponent<TransformComponent>(echo).ParentUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(audio.Flags.HasFlag(AudioFlags.GridAudio), Is.True);
            Assert.That(audio.Flags.HasFlag(AudioFlags.NoOcclusion), Is.True);
            var center = maps.GetGridPosition(map.Grid.Owner);
            var farListener = entities.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 200.5f, 100.5f));
            Assert.That(audio.Params.ReferenceDistance, Is.GreaterThanOrEqualTo(Vector2.Distance(center, farListener.Position)),
                "An occupant at an off-center station's far end must hear the echo at full gain.");
            var global = (HashSet<EntityUid>)typeof(PvsOverrideSystem).GetField("GlobalOverride", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(entities.System<PvsOverrideSystem>())!;
            Assert.That(global, Does.Contain(echo), "Distant occupants must receive the sound entity outside the impact's PVS.");
            var cooldown = audioState.NextImpactSound;
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"unshieldentity {map.Grid.Owner}");
            Assert.That(entities.EntityExists(echo), Is.True, "Shield collapse must leave the hull's echo playing.");
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var replacement = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            entities.GetComponent<ShipShieldComponent>(replacement).Source = emitterUid;
            var nextUid = entities.SpawnEntity("BulletDebugZoom", map.GridCoords);
            entities.EnsureComponent<ShipWeaponProjectileComponent>(nextUid);
            var nextProjectile = entities.GetComponent<ProjectileComponent>(nextUid);
            entities.System<ProjectileSystem>().ProjectileCollide((nextUid, nextProjectile, entities.GetComponent<PhysicsComponent>(nextUid)), replacement);
            Assert.That(nextProjectile.ProjectileSpent, Is.True);
            Assert.That(audioState.NextImpactSound, Is.EqualTo(cooldown));
            Assert.That(audioState.ActiveImpactSound, Is.EqualTo(echo), "Replacing a shield cannot stack another hull echo.");
        });
        for (var hit = 0; hit < 2; hit++)
        {
            await pair.RunTicksSync(36);
            await server.WaitAssertion(() =>
            {
                var state = entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner);
                var previous = state.ActiveImpactSound;
                var oldest = state.PreviousImpactSound;
                var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
                var uid = entities.SpawnEntity("BulletDebugZoom", map.GridCoords);
                entities.EnsureComponent<ShipWeaponProjectileComponent>(uid);
                var projectile = entities.GetComponent<ProjectileComponent>(uid);
                entities.System<ProjectileSystem>().ProjectileCollide((uid, projectile, entities.GetComponent<PhysicsComponent>(uid)), shield);
                Assert.That(state.ActiveImpactSound, Is.Not.EqualTo(previous), "A new hit must be audible before the full echo finishes.");
                Assert.That(state.PreviousImpactSound, Is.EqualTo(previous));
                if (oldest is { } old)
                    Assert.That(!entities.EntityExists(old) || entities.IsQueuedForDeletion(old), Is.True,
                        "Starting a third impact must retire the oldest tail.");
            });
        }
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
