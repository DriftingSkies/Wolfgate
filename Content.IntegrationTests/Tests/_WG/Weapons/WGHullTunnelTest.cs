using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// Rounds fired at a sealed hull from every bearing never reach what is inside it while its walls stand, whether the
/// hull is still, flying or turning.
/// </summary>
[TestFixture(60)]
[TestFixture(30)]
public sealed class WGHullTunnelTest
{
    /// <summary>
    /// <c>physics.target_minimum_tickrate</c>: 60 steps physics twice a tick, 30 once, as the live server runs. In one
    /// step a held round crept a whole metre, out of the far side of the wall it struck.
    /// </summary>
    private readonly int _physicsRate;

    public WGHullTunnelTest(int physicsRate)
    {
        _physicsRate = physicsRate;
    }

    private const int TickRate = 30;
    private const int Shots = 64;

    // Ammo, its speed, the hull's speed and its turn rate (rad/s).
    // With a deflector field up, a round let through the field used to fly the rest of its tick unswept and land inside.
    [TestCase("20mmBullet", 192f, 0f, 0f)]
    [TestCase("20mmBullet", 192f, 45f, 0f)]
    [TestCase("20mmBullet", 192f, 45f, 2f)]
    [TestCase("20mmBullet", 192f, 0f, 3f)]
    [TestCase("90mmBulletHE", 300f, 45f, 0f)]
    [TestCase("90mmBulletHE", 300f, 45f, 2f)]
    [TestCase("BulletMinigun", 60f, 45f, 1f)]
    [TestCase("20mmBullet", 192f, 0f, 0f, true)]
    [TestCase("20mmBullet", 192f, 45f, 2f, true)]
    [TestCase("90mmBulletHE", 300f, 45f, 0f, true)]
    [TestCase("90mmBulletFlak", 300f, 0f, 0f)]
    [TestCase("20mmBullet", 192f, 45f, 2f, false, 0.1f)]
    [TestCase("90mmBulletFlak", 300f, 45f, 2f)]
    public async Task RoundsNeverReachInsideSealedHull(string ammo, float speed, float hullSpeed, float hullSpin,
        bool deflector = false, float margin = 0f)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var failures = new List<string>();
        var random = new Random(1234);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, _physicsRate);
            server.CfgMan.SetCVar(Robust.Shared.CVars.PhysicsBroadphaseMargin, margin);
        });

        for (var i = 0; i < Shots; i++)
        {
            var bearing = (float) (random.NextDouble() * Math.Tau);
            var range = 6f + (float) random.NextDouble() * 8f;
            var aimOff = new Vector2((float) random.NextDouble() - 0.5f, (float) random.NextDouble() - 0.5f) * 3f;
            // Every other shot goes for a corner, where the diagonals are.
            if (i % 2 == 1)
                aimOff = new Vector2(random.Next(2) * 4 - 2, random.Next(2) * 4 - 2) + aimOff / 3f;
            var heading = (float) (random.NextDouble() * Math.Tau);
            var map = await pair.CreateTestMap();
            var hull = new List<EntityUid>();
            var inside = new List<EntityUid>();

            await server.WaitPost(() =>
            {
                SpawnHull(entities, map, hull, inside);
                if (deflector)
                    RaiseDeflector(server, entities, map);
                Fire(entities, map, ammo, speed, bearing, range, aimOff,
                    new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * hullSpeed, hullSpin);
            });

            await pair.RunTicksSync(15);

            await server.WaitPost(() =>
            {
                var standing = hull.TrueForAll(entities.EntityExists);
                foreach (var thing in inside)
                {
                    if (standing && entities.EntityExists(thing) &&
                        entities.GetComponent<DamageableComponent>(thing).TotalDamage <= 0)
                        continue;

                    failures.Add($"shot {i}: bearing {bearing:0.00} range {range:0.0} aim {aimOff} heading {heading:0.00}" +
                                 (standing ? "" : " (a wall fell)"));
                    break;
                }

                entities.DeleteEntity(map.MapUid);
            });
        }

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.PhysicsBroadphaseMargin, 0f);
            server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, 60);
        });

        Assert.That(failures, Is.Empty,
            $"{ammo} against a hull at {hullSpeed} m/s turning {hullSpin} rad/s got inside {failures.Count}/{Shots}:\n" +
            string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    /// <summary>A round held on a wall hits it at the next sweep even if no physics contact ever forms.</summary>
    [Test]
    public async Task HeldRoundLandsItsHitWithoutAContact()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hull = new List<EntityUid>();
            SpawnHull(entities, map, hull, new List<EntityUid>());
            Fire(entities, map, "20mmBullet", 192f, 0f, 5f, Vector2.Zero, Vector2.Zero, 0f);
            bool Struck() => hull.Exists(h => entities.GetComponent<DamageableComponent>(h).TotalDamage > 0);

            var sweep = entities.System<Content.Server.Projectiles.ProjectileSystem>();
            sweep.SweepProjectiles(1f / TickRate);
            Assert.That(Struck(), Is.False, "The first sweep only holds the round on the wall.");
            sweep.SweepProjectiles(1f / TickRate);
            Assert.That(Struck(), Is.True, "With no contact in between, the next sweep lands the hit.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Flak shrapnel has a wide hit circle for catching missiles; it must not reach through a hull.</summary>
    [Test]
    public async Task FlakShrapnelStopsAtTheHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var hull = new List<EntityUid>();
        var inside = new List<EntityUid>();
        var crew = new List<EntityUid>();
        EntityUid spacer = default;

        await server.WaitPost(() =>
        {
            var transform = entities.System<SharedTransformSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, _physicsRate);
            SpawnHull(entities, map, hull, inside);
            var centre = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    crew.Add(entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, x + 0.5f, y + 0.5f)));
                }
            }

            // A burst a metre and a half off each wall, flying at it.
            for (var i = 0; i < 48; i++)
            {
                var side = new Angle(Math.PI / 2 * (i % 4));
                var at = centre.Position + side.RotateVec(new Vector2(4f, -1.5f + i / 4 * 0.25f));
                var pellet = entities.SpawnEntity("Shrapnel90mmFlak", new MapCoordinates(at, centre.MapId));
                physics.SetLinearVelocity(pellet, side.RotateVec(new Vector2(-8f, 0f)));
                physics.WakeBody(pellet);
            }

            // And someone adrift three metres from another burst, with nothing in between.
            spacer = entities.SpawnEntity("MobHuman", new MapCoordinates(centre.Position + new Vector2(30f, 3f), centre.MapId));
            var far = entities.SpawnEntity("Shrapnel90mmFlak", new MapCoordinates(centre.Position + new Vector2(30f, 0f), centre.MapId));
            physics.SetLinearVelocity(far, new Vector2(0f, 8f));
            physics.WakeBody(far);
        });

        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(hull.TrueForAll(entities.EntityExists), Is.True);
            // Slash is the shrapnel's own; both are also short of air.
            float Cut(EntityUid uid) => entities.GetComponent<DamageableComponent>(uid).Damage.DamageDict
                .TryGetValue("Slash", out var slash) ? slash.Float() : 0f;
            Assert.That(crew.TrueForAll(c => Cut(c) == 0f), Is.True,
                "Shrapnel hit someone inside a sealed hull.");
            Assert.That(Cut(spacer), Is.GreaterThan(0f),
                "Shrapnel no longer reaches someone in the open.");
            server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, 60);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The same from a real turret on a ship of its own, from point blank outwards, both ships under way.</summary>
    [TestCase("WeaponTurretL85Autocannon", 0f, 0f, 0f)]
    [TestCase("WeaponTurretL85Autocannon", 40f, 0f, 0f)]
    [TestCase("WeaponTurretL85Autocannon", 40f, 40f, 0f)]
    [TestCase("WeaponTurretL85Autocannon", 40f, 40f, 2f)]
    public async Task TurretRoundsNeverReachInsideSealedHull(string gunProto, float shooterSpeed, float hullSpeed, float hullSpin)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var failures = new List<string>();
        var random = new Random(4321);
        var hits = 0;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, _physicsRate);
        });

        for (var i = 0; i < Shots; i++)
        {
            var bearing = (float) (random.NextDouble() * Math.Tau);
            var range = 4.5f + (float) random.NextDouble() * 10f;
            var heading = (float) (random.NextDouble() * Math.Tau);
            var shooterHeading = (float) (random.NextDouble() * Math.Tau);
            var map = await pair.CreateTestMap();
            var hull = new List<EntityUid>();
            var inside = new List<EntityUid>();

            await server.WaitPost(() =>
            {
                var transform = entities.System<SharedTransformSystem>();
                var physics = entities.System<SharedPhysicsSystem>();
                var maps = entities.System<SharedMapSystem>();
                SpawnHull(entities, map, hull, inside);
                var centre = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                var hullVelocity = new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * hullSpeed;
                var shooterVelocity = new Vector2(MathF.Cos(shooterHeading), MathF.Sin(shooterHeading)) * shooterSpeed;

                var ship = maps.CreateGridEntity(centre.MapId);
                maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, map.Tile.Tile);
                physics.SetBodyType(ship.Owner, Robust.Shared.Physics.BodyType.Dynamic);
                transform.SetWorldPosition(ship.Owner,
                    centre.Position + new Vector2(MathF.Cos(bearing), MathF.Sin(bearing)) * range - new Vector2(0.5f, 0.5f));
                var gun = entities.SpawnEntity(gunProto, new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
                physics.SetLinearVelocity(ship.Owner, shooterVelocity);
                physics.SetLinearVelocity(map.Grid.Owner, hullVelocity);
                physics.SetAngularVelocity(map.Grid.Owner, hullSpin);

                var comp = entities.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(gun);
                var flight = range / comp.ProjectileSpeedModified;
                var aim = new MapCoordinates(centre.Position + (hullVelocity - shooterVelocity) * flight, centre.MapId);
                entities.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>()
                    .AttemptShoot(gun, gun, comp, transform.ToCoordinates(aim));
            });

            await pair.RunTicksSync(15);

            await server.WaitPost(() =>
            {
                var standing = hull.TrueForAll(entities.EntityExists);
                if (hull.Exists(h => entities.TryGetComponent<DamageableComponent>(h, out var d) && d.TotalDamage > 0))
                    hits++;
                foreach (var thing in inside)
                {
                    if (standing && entities.EntityExists(thing) &&
                        entities.GetComponent<DamageableComponent>(thing).TotalDamage <= 0)
                        continue;

                    failures.Add($"shot {i}: bearing {bearing:0.00} range {range:0.0} heading {heading:0.00} shooter {shooterHeading:0.00}" +
                                 (standing ? "" : " (a wall fell)"));
                    break;
                }

                entities.DeleteEntity(map.MapUid);
            });
        }

        await server.WaitPost(() => server.CfgMan.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, 60));

        Assert.That(failures, Is.Empty,
            $"{gunProto} at {shooterSpeed} m/s against a hull at {hullSpeed} m/s turning {hullSpin} rad/s got inside " +
            $"{failures.Count}/{Shots} ({hits} struck the hull):\n" + string.Join("\n", failures));
        Assert.That(hits, Is.GreaterThan(Shots / 4), "Too few rounds struck the hull for the case to mean anything.");

        await pair.CleanReturnAsync();
    }

    /// <summary>A sealed 5x5 hull, plastitanium with a shuttle window in the middle of each side, girders filling the room.</summary>
    private static void SpawnHull(IEntityManager entities, TestMapData map, List<EntityUid> hull, List<EntityUid> inside)
    {
        var maps = entities.System<SharedMapSystem>();
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                var edge = Math.Abs(x) == 2 || Math.Abs(y) == 2;
                var corner = Math.Abs(x) == 2 && Math.Abs(y) == 2;
                var proto = !edge ? "Girder"
                    : corner ? (x == y ? "ShuttleWindowDiagonal" : "WallPlastitaniumDiagonal")
                    : x == 0 || y == 0 ? "ShuttleWindow" : "WallPlastitanium";
                var spawned = entities.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, x + 0.5f, y + 0.5f));
                // A diagonal's solid half faces south-east unturned; turn it to face the room.
                if (corner)
                {
                    entities.System<SharedTransformSystem>().SetLocalRotation(spawned,
                        Angle.FromDegrees(x < 0 ? (y < 0 ? 90 : 0) : (y < 0 ? 180 : -90)));
                }
                (edge ? hull : inside).Add(spawned);
            }
        }
    }

    /// <summary>Raises a deflector field around the hull: it weakens solid shot and lets it through.</summary>
    private static void RaiseDeflector(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server,
        IEntityManager entities, TestMapData map)
    {
        var generator = entities.SpawnEntity("WGShieldGeneratorDeflector", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        server.ResolveDependency<Robust.Shared.Console.IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
        var shield = entities.GetComponent<Content.Shared._Crescent.ShipShields.ShipShieldedComponent>(map.Grid.Owner).Shield;
        entities.GetComponent<Content.Shared._Crescent.ShipShields.ShipShieldComponent>(shield).Source = generator;
    }

    private static void Fire(IEntityManager entities, TestMapData map, string prototype, float speed, float bearing,
        float range, Vector2 aimOff, Vector2 hullVelocity, float hullSpin)
    {
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var centre = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        var from = centre.Position + new Vector2(MathF.Cos(bearing), MathF.Sin(bearing)) * range;

        // Lead the hull, as a gunner does.
        var flight = range / speed;
        var direction = Vector2.Normalize(centre.Position + aimOff + hullVelocity * flight - from);

        var round = entities.SpawnEntity(prototype, new MapCoordinates(from, centre.MapId));
        transform.SetWorldRotation(round, direction.ToWorldAngle() + entities.GetComponent<ProjectileComponent>(round).Angle);
        physics.SetLinearVelocity(round, direction * speed);
        physics.WakeBody(round);
        physics.SetLinearVelocity(map.Grid.Owner, hullVelocity);
        physics.SetAngularVelocity(map.Grid.Owner, hullSpin);
    }
}
