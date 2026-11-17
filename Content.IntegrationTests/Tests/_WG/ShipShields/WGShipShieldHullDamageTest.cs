using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Client._WG.ShipShields;
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Shuttles.Systems;
using Content.Server.Power.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio;
using Content.Shared.Explosion.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Projectiles;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks damaged shields keep intercepting real artillery after hull fixture rebuilds.</summary>
[TestFixture]
public sealed class WGShipShieldHullDamageTest
{
    [Test]
    public async Task DamagedHullRebuildKeepsShieldCollidableForArtillery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        await pair.Client.WaitAssertion(() =>
        {
            var resources = pair.Client.ResolveDependency<IResourceCache>();
            using var screen = new WGShipShieldShuntScreen();
            screen.Stylesheet = pair.Client.ResolveDependency<IUserInterfaceManager>().Stylesheet;
            var requests = new List<(float Direction, float Concentration, float Arc)>();
            screen.AllocationRequested += (direction, concentration, arc) => requests.Add((direction, concentration, arc));
            screen.Measure(new Vector2(1000f, 700f));
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1000f, 700f)));
            Assert.That(screen.IsArrangeValid, Is.True);
            const float helmRotation = 0.41f;
            var state = new WGShipShieldShuntState(true, true, 1f, 0.8f, 0.2f, MathF.PI / 2f);
            screen.UpdateState(state, helmRotation);
            screen.SetDraft(35f, 0.73f, 120f);
            foreach (var panelSize in new[] { new Vector2(940f, 670f), new Vector2(760f, 540f), new Vector2(1100f, 760f) })
            foreach (var concentration in new[] { 0.73f, 1f })
            {
                screen.SetDraft(35f, concentration, 120f);
                screen.Measure(panelSize);
                screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, panelSize));
                Assert.That(screen.DesiredSize.X, Is.LessThanOrEqualTo(panelSize.X), "Shield controls must fit the helm width.");
                Assert.That(screen.DesiredSize.Y, Is.LessThanOrEqualTo(panelSize.Y), "Shield controls must fit without hiding the live controls.");
                AssertHorizontalBounds(screen);
            }
            screen.SetDraft(35f, 0.73f, 120f);
            Assert.That(requests, Is.Empty, "Edits in one frame must coalesce into a single live request.");
            screen.UpdateState(new WGShipShieldShuntState(true, false, 0.7f, 0.8f, 0.2f, MathF.PI / 2f, false), helmRotation);
            Tick(screen, 0.11f);
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Direction, Is.EqualTo(WGShipShieldHelmAngles.GridDirection(35f, helmRotation)).Within(0.0001f));
            Assert.That(requests[0].Concentration, Is.EqualTo(0.73f).Within(0.0001f));
            Assert.That(requests[0].Arc, Is.EqualTo(2f * MathF.PI / 3f).Within(0.0001f));
            screen.ResetAllocation();
            Tick(screen, 0.11f);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].Concentration, Is.Zero);
            screen.UpdateState(new WGShipShieldShuntState(false, false, 0f, 0f, 0f, MathF.PI / 2f), helmRotation);
            Tick(screen, 0.11f);
            screen.ResetAllocation();
            Tick(screen, 0.11f);
            Assert.That(requests, Has.Count.EqualTo(2), "An unavailable generator cannot submit an allocation.");
            foreach (var transition in new[] { (Name: "shield_on", Duration: 4.15d), (Name: "shield_off", Duration: 3.88d) })
            {
                var clip = resources.GetResource<AudioResource>($"/Audio/_WG/ShipShields/{transition.Name}.ogg").AudioStream;
                Assert.That(clip.ChannelCount, Is.EqualTo(1));
                Assert.That(clip.Length.TotalSeconds, Is.EqualTo(transition.Duration).Within(0.03d));
            }
            for (var variant = 1; variant <= 3; variant++)
            {
                var clip = resources.GetResource<AudioResource>($"/Audio/_WG/ShipShields/impact_{variant}.ogg").AudioStream;
                Assert.That(clip.ChannelCount, Is.EqualTo(1), "Positional shield impacts must load as mono audio.");
                Assert.That(clip.Length.TotalSeconds, Is.EqualTo(3.4d).Within(0.03d));
            }
        });
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var shield = EntityUid.Invalid;
        var emitterUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 60);
            maps.SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            transform.SetWorldRotation(map.Grid.Owner, Angle.FromDegrees(37));
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            emitterUid = entities.SpawnEntity(null, map.GridCoords);
            var emitter = entities.EnsureComponent<ShipShieldEmitterComponent>(emitterUid);
            emitter.Damage = 2500f;
            entities.GetComponent<ShipShieldComponent>(shield).Source = emitterUid;
            maps.SetTile(map.Grid, Vector2i.Zero, default);
            maps.SetTile(map.Grid, Vector2i.Zero, map.Tile.Tile);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PhysicsComponent>(shield).CanCollide, Is.True,
                "Destroying the last old fixture must not disable the rebuilt shield.");
            Assert.That(entities.GetComponent<WGShipShieldVisualsComponent>(shield).Health, Is.InRange(0.1f, 0.45f));
        });

        var shots = new[]
        {
            (Prototype: "20mmBullet", Start: new Vector2(-12f, 0.5f), Speed: 50f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-12f, -12f), Speed: 50f, Tickrate: 60),
            (Prototype: "20mmBullet", Start: new Vector2(14f, 0.5f), Speed: 50f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-12f, -10f), Speed: 74.9f, Tickrate: 60),
            (Prototype: "ShipCerberusPlasma", Start: new Vector2(-10f, -12f), Speed: 74.9f, Tickrate: 30),
        };
        var nextImpactSound = TimeSpan.Zero;
        var shotIndex = 0;
        foreach (var shot in shots)
        {
            var uid = EntityUid.Invalid;
            var projectile = default(ProjectileComponent);
            var expectedDamage = 0f;
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, shot.Tickrate);
                var start = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, shot.Start));
                var target = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 1f, 0.5f));
                var direction = Vector2.Normalize(target.Position - start.Position);
                uid = entities.SpawnEntity(shot.Prototype, start);
                transform.SetWorldRotation(uid, direction.ToAngle());
                projectile = entities.GetComponent<ProjectileComponent>(uid);
                expectedDamage = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage + (float)projectile.Damage.GetTotal();
                if (entities.TryGetComponent<ExplosiveComponent>(uid, out var explosive) &&
                    prototypes.TryIndex(explosive.ExplosionType, out var explosion))
                    expectedDamage += explosive.TotalIntensity * (float)explosion.DamagePerIntensity.GetTotal();
                physics.SetLinearVelocity(uid, direction * shot.Speed);
                physics.WakeBody(uid);
            });
            await pair.RunTicksSync(shot.Tickrate / 2);
            await server.WaitAssertion(() =>
            {
                var exists = entities.EntityExists(uid);
                var body = exists ? entities.GetComponent<PhysicsComponent>(uid) : null;
                var position = exists ? transform.ToCoordinates(map.Grid.Owner, transform.GetMapCoordinates(uid)).Position : Vector2.Zero;
                var phase = exists && entities.TryGetComponent<ProjectileGridPhaseComponent>(uid, out var source) ? source.SourceGrid : null;
                var actualDamage = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage;
                Assert.That(projectile.ProjectileSpent, Is.True,
                    $"{shot.Prototype} must hit the rebuilt shield from {shot.Start}. Exists={exists}, queued={(exists && entities.IsQueuedForDeletion(uid))}, " +
                    $"hullLocal={position}, velocity={body?.LinearVelocity}, collidable={body?.CanCollide}, sourceGrid={phase}, " +
                    $"actualEmitterDamage={actualDamage}, expectedEmitterDamage={expectedDamage}, shieldCollidable={entities.GetComponent<PhysicsComponent>(shield).CanCollide}.");
                Assert.That(entities.EntityExists(uid), Is.False);
                Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage, Is.EqualTo(expectedDamage).Within(0.01f));
                if (shotIndex >= 3)
                    return;
                var audio = entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner);
                if (nextImpactSound == TimeSpan.Zero)
                {
                    nextImpactSound = audio.NextImpactSound;
                    Assert.That((nextImpactSound - server.Timing.CurTime).TotalSeconds, Is.InRange(0d, 1.1d));
                }
                var soundCount = 0;
                var sounds = entities.EntityQueryEnumerator<AudioComponent>();
                while (sounds.MoveNext(out var soundUid, out var sound))
                {
                    var filename = sound.FileName;
                    if (!entities.IsQueuedForDeletion(soundUid) && filename.Contains("_WG/ShipShields/impact_"))
                        soundCount++;
                }
                Assert.That(soundCount, Is.InRange(1, 2), "Frequent impacts must keep at most two echo tails.");
            });
            shotIndex++;
        }
        var shields = entities.System<ShipShieldsSystem>();
        var center = new Vector2(21f, -9.5f);
        const float bearing = 0.731f;
        Vector2 Rotate(Vector2 point) => new(MathF.Cos(bearing) * point.X - MathF.Sin(bearing) * point.Y,
            MathF.Sin(bearing) * point.X + MathF.Cos(bearing) * point.Y);
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 60);
            maps.SetTile(map.Grid, new Vector2i(20, -10), map.Tile.Tile);
            maps.SetTile(map.Grid, new Vector2i(21, -10), map.Tile.Tile);
            maps.SetTile(map.Grid, Vector2i.Zero, default);
            maps.SetTile(map.Grid, new Vector2i(1, 0), default);
            entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage = 0f;
        });
        await pair.RunTicksSync(60);
        var throughUid = EntityUid.Invalid;
        var throughProjectile = default(ProjectileComponent);
        await server.WaitAssertion(() =>
        {
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, bearing, 1f, MathF.PI / 2f), Is.True);
            var allocation = entities.GetComponent<WGShipShieldShuntComponent>(shield);
            Assert.That(allocation.Center, Is.EqualTo(center));
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, invalid, 0.5f, MathF.PI), Is.False);
                Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, invalid, MathF.PI), Is.False);
                Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0.5f, invalid), Is.False);
            }
            Assert.That(allocation.Concentration, Is.EqualTo(1f));
            Assert.That(shields.SetWolfgateShieldShunt(emitterUid, 0f, 1f, MathF.PI / 2f), Is.False);
            throughUid = entities.SpawnEntity("BulletDebugZoom", transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, center + Rotate(new Vector2(-12f, 3.5f)))));
            entities.EnsureComponent<Content.Shared._Mono.SpaceArtillery.ShipWeaponProjectileComponent>(throughUid);
            throughProjectile = entities.GetComponent<ProjectileComponent>(throughUid);
            var direction = transform.GetWorldRotation(map.Grid.Owner).RotateVec(Rotate(Vector2.UnitX));
            transform.SetWorldRotation(throughUid, direction.ToAngle());
            physics.SetLinearVelocity(throughUid, direction * 10f);
            physics.WakeBody(throughUid);
        });
        await pair.RunTicksSync(40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(throughUid), Is.True, "A shot must pass through the fully unprotected front.");
            Assert.That(throughProjectile.ProjectileSpent, Is.False);
            Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage, Is.Zero);
        });
        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(throughProjectile.ProjectileSpent, Is.True, "The same shot must be caught by the reinforced rear.");
            Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage,
                Is.EqualTo((float)throughProjectile.Damage.GetTotal() / WGShipShieldShuntMath.PeakStrength(1f, MathF.PI / 2f)).Within(0.001f));
        });
        foreach (var scenario in new[]
        {
            (Amount: 0.5f, Side: -1f, DamageScale: 2f),
            (Amount: 0.5f, Side: 1f, DamageScale: 1f / WGShipShieldShuntMath.PeakStrength(0.5f, MathF.PI / 2f)),
            (Amount: 0f, Side: -1f, DamageScale: 1f),
        })
        {
            var uid = EntityUid.Invalid;
            var projectile = default(ProjectileComponent);
            await server.WaitAssertion(() =>
            {
                shields.SetWolfgateShieldShunt(map.Grid.Owner, bearing, scenario.Amount, MathF.PI / 2f);
                entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage = 0f;
                uid = entities.SpawnEntity("BulletDebugZoom", transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, center + Rotate(new Vector2(scenario.Side * 12f, 0f)))));
                entities.EnsureComponent<Content.Shared._Mono.SpaceArtillery.ShipWeaponProjectileComponent>(uid);
                projectile = entities.GetComponent<ProjectileComponent>(uid);
                var direction = transform.GetWorldRotation(map.Grid.Owner).RotateVec(Rotate(-scenario.Side * Vector2.UnitX));
                transform.SetWorldRotation(uid, direction.ToAngle());
                physics.SetLinearVelocity(uid, direction * 50f);
                physics.WakeBody(uid);
            });
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
            {
                Assert.That(projectile.ProjectileSpent, Is.True, $"Allocation {scenario.Amount}, side {scenario.Side} must retain coverage.");
                Assert.That(entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).Damage,
                    Is.EqualTo((float)projectile.Damage.GetTotal() * scenario.DamageScale).Within(0.001f));
            });
        }
        var powerSoundDeadline = TimeSpan.Zero;
        var replacementEmitter = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            entities.GetComponent<ShipShieldEmitterComponent>(emitterUid).PowerUpSound =
                new SoundPathSpecifier("/Audio/_WG/ShipShields/shield_on.ogg");
            shields.PlayWolfgateShieldPowerSound(emitterUid, map.Grid.Owner, true);
            var powerSounds = entities.EntityQueryEnumerator<AudioComponent, TransformComponent>();
            var heardPowerSound = false;
            while (powerSounds.MoveNext(out var sound, out var soundTransform))
            {
                if (sound.FileName != "/Audio/_WG/ShipShields/shield_on.ogg")
                    continue;
                heardPowerSound = true;
                Assert.That(sound.Flags.HasFlag(AudioFlags.GridAudio), Is.True);
                Assert.That(sound.Flags.HasFlag(AudioFlags.NoOcclusion), Is.True);
                Assert.That(soundTransform.ParentUid, Is.EqualTo(map.Grid.Owner));
                Assert.That(sound.IncludedEntities, Is.Null, "Ships without a station must still hear power transitions.");
            }
            Assert.That(heardPowerSound, Is.True);
            powerSoundDeadline = entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner).NextPowerSound;
            Assert.That((powerSoundDeadline - server.Timing.CurTime).TotalSeconds, Is.EqualTo(5d).Within(0.001d));
            shields.PlayWolfgateShieldPowerSound(emitterUid, map.Grid.Owner, false);
            Assert.That(entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner).NextPowerSound, Is.EqualTo(powerSoundDeadline));
            replacementEmitter = entities.SpawnEntity(null, new EntityCoordinates(map.Grid.Owner, center));
            entities.EnsureComponent<ShipShieldEmitterComponent>(replacementEmitter);
            shields.PlayWolfgateShieldPowerSound(replacementEmitter, map.Grid.Owner, true);
            Assert.That(entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner).NextPowerSound, Is.EqualTo(powerSoundDeadline),
                "A different emitter must share the hull transition cooldown.");
        });
        await pair.RunTicksSync(306);
        await server.WaitAssertion(() =>
        {
            shields.PlayWolfgateShieldPowerSound(replacementEmitter, map.Grid.Owner, false);
            var deadline = entities.GetComponent<WGShipShieldImpactAudioComponent>(map.Grid.Owner).NextPowerSound;
            Assert.That(deadline, Is.GreaterThan(powerSoundDeadline));
            Assert.That((deadline - server.Timing.CurTime).TotalSeconds, Is.EqualTo(5d).Within(0.001d));
        });
        await server.WaitAssertion(() =>
        {
            var helmCoordinates = new EntityCoordinates(map.Grid.Owner, center);
            transform.SetCoordinates(emitterUid, helmCoordinates);
            transform.AnchorEntity(emitterUid, entities.GetComponent<TransformComponent>(emitterUid));
            var helm = entities.SpawnEntity("ComputerShuttle", helmCoordinates);
            var actor = entities.SpawnEntity("MobHuman", helmCoordinates);
            Assert.That(entities.GetComponent<TransformComponent>(helm).GridUid, Is.EqualTo(map.Grid.Owner));
            entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered = true;
            var ui = entities.System<SharedUserInterfaceSystem>();
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(ui.IsUiOpen(helm, ShuttleConsoleUiKey.Key), Is.True);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            emitter.Damage = emitter.DamageLimit * 0.72f;
            entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Source = emitterUid;
            entities.GetComponent<WGShipShieldVisualsComponent>(shield).Health = 1f;
            var helms = entities.System<ShuttleConsoleSystem>();
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(helm, ShuttleConsoleUiKey.Key, out var initial), Is.True);
            var snapshots = (Dictionary<EntityUid, WGShipShieldShuntState>) typeof(ShuttleConsoleSystem)
                .GetField("_wgShieldHelmStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helms)!;
            helms.Update(0.21f);
            var active = State();
            Assert.That(active.Active, Is.True);
            Assert.That(active.Health, Is.EqualTo(0.28f).Within(0.001f), "The helm reads emitter capacity without waiting for visual replication.");
            helms.Update(0.21f);
            Assert.That(State(), Is.SameAs(active), "Unchanged status must not send another shield snapshot.");
            entities.QueueDeleteEntity(shield);
            helms.Update(0.21f);
            Assert.That(State().Active, Is.False, "Queued field removal must immediately report offline.");
            Assert.That(State().Health, Is.EqualTo(0.28f).Within(0.001f), "Offline controls retain actual emitter capacity.");

            WGShipShieldShuntState State()
            {
                Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(helm, ShuttleConsoleUiKey.Key, out var state), Is.True);
                Assert.That(state, Is.SameAs(initial), "Shield-only updates must not rebuild the navigation payload.");
                Assert.That(snapshots.TryGetValue(helm, out var snapshot), Is.True);
                return snapshot!;
            }
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertHorizontalBounds(Control parent)
    {
        // The slider thumb intentionally extends beyond its internal track at either endpoint.
        if (parent is Slider)
            return;
        foreach (var child in parent.Children)
        {
            if (!child.Visible)
                continue;
            Assert.That(child.Position.X, Is.GreaterThanOrEqualTo(-1f), $"{child.GetType().Name} starts outside {parent.GetType().Name}.");
            Assert.That(child.Position.X + child.Size.X, Is.LessThanOrEqualTo(parent.Size.X + 1f),
                $"{child.GetType().Name} overflows {parent.GetType().Name} at width {parent.Size.X}.");
            AssertHorizontalBounds(child);
        }
    }
    private static void Tick(WGShipShieldShuntScreen screen, float seconds) =>
        typeof(WGShipShieldShuntScreen).GetMethod("FrameUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(screen, new object[] { new Robust.Shared.Timing.FrameEventArgs(seconds) });

}
