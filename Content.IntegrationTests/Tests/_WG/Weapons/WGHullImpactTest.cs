using System.Collections.Generic;
using System.Numerics;
using Content.Server.Destructible;
using Content.Shared._WG.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>Swept ship rounds stop at the face they hit: impact fuses fire on the wall, not behind it.</summary>
[TestFixture]
public sealed class WGHullImpactTest
{
    private static readonly ProtoId<DamageTypePrototype> StructuralProto = "Structural";
    private const float ShellSpeed = 300f;
    private const int TickRate = 30;

    // The diagonal blocks south and east, so a shell from the west strikes its open side with the room to the east.
    [TestCase("WallPlastitanium", 0f)]
    [TestCase("WallPlastitanium", 15f)]
    [TestCase("WallPlastitanium", 40f)]
    [TestCase("WallPlastitaniumDiagonal", 0f)]
    [TestCase("WallPlastitaniumDiagonal", 40f)]
    public async Task ShellBlastStopsAtIntactWall(string wallPrototype, float closingSpeed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var wall = EntityUid.Invalid;
        var inside = EntityUid.Invalid;
        var shell = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            (wall, inside) = SpawnHull(entities, map, wallPrototype);
            shell = FireShell(entities, map, "90mmBulletHE", 12f, closingSpeed);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(HullFailure(entities, shell, wall, inside), Is.Null,
                $"{wallPrototype} at closing speed {closingSpeed}");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Autocannon-sized blasts take the battle-load direct path; it must not reach behind the wall either.</summary>
    [TestCase("30mmBulletHE", "WallPlastitanium", 0f)]
    [TestCase("30mmBulletHE", "WallPlastitanium", 40f)]
    [TestCase("35mmBulletHE", "WallPlastitanium", 0f)]
    [TestCase("30mmBulletHE", "WallPlastitaniumDiagonal", 0f)]
    public async Task SmallShellBlastStopsAtIntactWall(string ammo, string wallPrototype, float closingSpeed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var wall = EntityUid.Invalid;
        var inside = EntityUid.Invalid;
        var shell = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            server.CfgMan.SetCVar(BattleLoadCVars.MicroExplosions, true);
            (wall, inside) = SpawnHull(entities, map, wallPrototype);
            shell = FireShell(entities, map, ammo, 12f, closingSpeed);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(HullFailure(entities, shell, wall, inside), Is.Null,
                $"{ammo} on {wallPrototype} at closing speed {closingSpeed}");
            Assert.That(entities.System<SharedMapSystem>().GetTileRef(map.Grid, new Vector2i(1, 0)).Tile.IsEmpty, Is.False,
                "The floor behind the wall was broken.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A shell stops at a closing hull from any range, not only where its sweep happens to land on the wall.
    /// </summary>
    [TestCase("WallPlastitanium", 40f)]
    [TestCase("WallPlastitanium", 60f)]
    [TestCase("WallPlastitaniumDiagonal", 40f)]
    [TestCase("WallPlastitaniumDiagonal", 60f)]
    public async Task ShellStopsAtClosingHullFromAnyRange(string wallPrototype, float closingSpeed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var failures = new List<string>();

        await server.WaitPost(() => server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate));

        // The gap closes this far each tick, so starts across one period meet the hull at every phase of the sweep.
        var period = (ShellSpeed + closingSpeed) / TickRate;
        var steps = (int) MathF.Ceiling(period / 0.1f);
        for (var i = 0; i < steps; i++)
        {
            var start = 9.5f + i * 0.1f;
            var map = await pair.CreateTestMap();
            var wall = EntityUid.Invalid;
            var inside = EntityUid.Invalid;
            var shell = EntityUid.Invalid;

            await server.WaitPost(() =>
            {
                (wall, inside) = SpawnHull(entities, map, wallPrototype);
                shell = FireShell(entities, map, "90mmBulletHE", start, closingSpeed);
            });

            await pair.RunTicksSync(30);

            await server.WaitPost(() =>
            {
                if (HullFailure(entities, shell, wall, inside) is { } failure)
                    failures.Add($"start {start:0.0}: {failure}");

                entities.DeleteEntity(map.MapUid);
            });
        }

        Assert.That(failures, Is.Empty,
            $"{wallPrototype} at closing speed {closingSpeed} let shells through:\n{string.Join("\n", failures)}");

        await pair.CleanReturnAsync();
    }

    /// <summary>An AP round that destroys the first wall still carries on into the next one.</summary>
    [TestCase(0f)]
    [TestCase(40f)]
    public async Task PenetratingRoundCarriesOnThroughDestroyedWall(float closingSpeed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var first = EntityUid.Invalid;
        var second = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, TickRate);
            for (var x = 0; x <= 8; x++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);
            }

            first = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            second = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f));

            // Wear the first wall down so one AP hit breaks it.
            var structural = server.ProtoMan.Index<DamageTypePrototype>(StructuralProto);
            var destroyedAt = entities.System<DestructibleSystem>().DestroyedAt(first);
            entities.System<DamageableSystem>().TryChangeDamage(first,
                new DamageSpecifier(structural, destroyedAt - 10), ignoreResistances: true);

            FireShell(entities, map, "90mmBulletAP", 12f, closingSpeed);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(first), Is.False, "The round should have destroyed the first wall.");
            Assert.That(!entities.TryGetComponent<DamageableComponent>(second, out var damage) || damage.TotalDamage > 0,
                Is.True, $"The round should have carried on into the second wall (closing speed {closingSpeed}).");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A sealed 5x5 hull: plastitanium ring around a 3x3 room, with a girder just inside the struck wall.</summary>
    private static (EntityUid Wall, EntityUid Inside) SpawnHull(IEntityManager entities, TestMapData map, string wallPrototype)
    {
        var maps = entities.System<SharedMapSystem>();
        var wall = EntityUid.Invalid;
        for (var x = 0; x <= 4; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                if (x is not (0 or 4) && y is not (-2 or 2))
                    continue;

                var proto = x == 0 && y == 0 ? wallPrototype : "WallPlastitanium";
                var spawned = entities.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, x + 0.5f, y + 0.5f));
                if (x == 0 && y == 0)
                    wall = spawned;
            }
        }

        var inside = entities.SpawnEntity("Girder", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
        return (wall, inside);
    }

    /// <summary>Fires a shell east at the hull's west wall from <paramref name="start"/> tiles out, set up as a gun sets it.</summary>
    private static EntityUid FireShell(IEntityManager entities, TestMapData map, string prototype, float start, float closingSpeed)
    {
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var direction = new Vector2(1f, 0f);

        var shell = entities.SpawnEntity(prototype, transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, -start, 0.5f)));
        transform.SetWorldRotation(shell, direction.ToWorldAngle() + entities.GetComponent<ProjectileComponent>(shell).Angle);
        physics.SetLinearVelocity(shell, direction * ShellSpeed);
        physics.WakeBody(shell);
        physics.SetLinearVelocity(map.Grid.Owner, -direction * closingSpeed);
        return shell;
    }

    /// <summary>Why the shell failed to stop at the wall, or null when it detonated on it.</summary>
    private static string HullFailure(IEntityManager entities, EntityUid shell, EntityUid wall, EntityUid inside)
    {
        if (entities.EntityExists(shell))
            return "the shell never detonated";
        if (!entities.TryGetComponent<DamageableComponent>(wall, out var wallDamage))
            return "a single shell destroyed the wall";
        if (wallDamage.TotalDamage <= 0)
            return "the shell missed the wall";
        if (!entities.TryGetComponent<DamageableComponent>(inside, out var insideDamage))
            return "the girder behind the wall was destroyed";
        if (insideDamage.TotalDamage > 0)
            return "the blast reached behind the wall";
        return null;
    }
}
