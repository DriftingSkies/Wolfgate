using System.Numerics;
using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>Swept ship rounds stop at the face they hit: impact fuses fire on the wall, not behind it.</summary>
[TestFixture]
public sealed class WFHullImpactTest
{
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
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var maps = entities.System<SharedMapSystem>();
        var wall = EntityUid.Invalid;
        var inside = EntityUid.Invalid;
        var shell = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            // A sealed 5x5 hull: plastitanium ring around a 3x3 room, struck on the west wall's middle tile.
            for (var x = 0; x <= 4; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                    if (x is 0 or 4 || y is -2 or 2)
                    {
                        var proto = x == 0 && y == 0 ? wallPrototype : "WallPlastitanium";
                        var spawned = entities.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, x + 0.5f, y + 0.5f));
                        if (x == 0 && y == 0)
                            wall = spawned;
                    }
                }
            }

            inside = entities.SpawnEntity("Girder", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));

            var start = transform.ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, -12f, 0.5f));
            shell = entities.SpawnEntity("90mmBulletHE", start);
            physics.SetLinearVelocity(shell, new Vector2(300f, 0f));
            physics.WakeBody(shell);
            physics.SetLinearVelocity(map.Grid.Owner, new Vector2(-closingSpeed, 0f));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(shell), Is.False, "The shell should have detonated.");
            Assert.That(entities.TryGetComponent<DamageableComponent>(wall, out var wallDamage), Is.True,
                "The wall should survive a single shell.");
            Assert.That(wallDamage!.TotalDamage.Float(), Is.GreaterThan(0f), "The shell should have hit the wall.");
            Assert.That(entities.TryGetComponent<DamageableComponent>(inside, out var insideDamage), Is.True,
                "The girder behind the wall was destroyed.");
            Assert.That(insideDamage!.TotalDamage.Float(), Is.Zero,
                $"The blast reached behind a {wallPrototype} it could not break (closing speed {closingSpeed}).");
        });

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
        var physics = entities.System<SharedPhysicsSystem>();
        var maps = entities.System<SharedMapSystem>();
        var first = EntityUid.Invalid;
        var second = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetTickrate, 30);
            for (var x = 0; x <= 8; x++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);
            }

            first = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            second = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f));

            // Wear the first wall down so one AP hit breaks it.
            var structural = server.ProtoMan.Index<DamageTypePrototype>("Structural");
            var destroyedAt = entities.System<DestructibleSystem>().DestroyedAt(first);
            entities.System<DamageableSystem>().TryChangeDamage(first,
                new DamageSpecifier(structural, destroyedAt - 10), ignoreResistances: true);

            var start = entities.System<SharedTransformSystem>()
                .ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, -12f, 0.5f));
            var round = entities.SpawnEntity("90mmBulletAP", start);
            physics.SetLinearVelocity(round, new Vector2(300f, 0f));
            physics.WakeBody(round);
            physics.SetLinearVelocity(map.Grid.Owner, new Vector2(-closingSpeed, 0f));
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
}
