#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipArmor;

/// <summary>
/// Armor-backed mounts hand part of explosion and projectile damage to the thickest adjacent armor,
/// scaled by that armor's thickness, and keep the rest. Plain damage and penetrating shots stay on the mount.
/// </summary>
[TestFixture]
[TestOf(typeof(Content.Server._WF.ShipArmor.WFArmorBackedSystem))]
public sealed class WFArmorBackedTest
{
    /// <summary>Mount that moves part of its damage onto adjacent armor.</summary>
    private const string MountProto = "WFTestArmorBackedMount";

    /// <summary>Armor of thickness 10, full coverage.</summary>
    private const string ThickWallProto = "WFTestArmorWallThick";

    /// <summary>Armor of thickness 5, half coverage.</summary>
    private const string ThinWallProto = "WFTestArmorWallThin";

    /// <summary>Mount with a 2x2 hard fixture.</summary>
    private const string MultiTileMountProto = "WFTestMultiTileMount";

    /// <summary>Mount with a 1x1 hard fixture and a non-hard sensor fixture below it.</summary>
    private const string HardFixtureMountProto = "WFTestHardFixtureMount";

    /// <summary>Projectile used as the damage tool for a shot.</summary>
    private const string ProjectileProto = "WFTestArmorProjectile";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestArmorBackedMount
  components:
  - type: Transform
  - type: Damageable
  - type: Hardpoint

- type: entity
  id: WFTestMultiTileMount
  components:
  - type: Transform
  - type: Damageable
  - type: Hardpoint
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.45,-0.45,1.45,1.45""
        hard: true

- type: entity
  id: WFTestHardFixtureMount
  components:
  - type: Transform
  - type: Damageable
  - type: Hardpoint
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      hard:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.45,-0.45,0.45,0.45""
        hard: true
      sensor:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.45,-3.45,0.45,0.45""
        hard: false

- type: entity
  id: WFTestArmorWallThick
  components:
  - type: Transform
  - type: Damageable
  - type: ArmorThickness
    thickness: 10
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 100000
      behaviors: []

- type: entity
  id: WFTestArmorWallThin
  components:
  - type: Transform
  - type: Damageable
  - type: ArmorThickness
    thickness: 5
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 100000
      behaviors: []

- type: entity
  id: WFTestArmorProjectile
  components:
  - type: Projectile
    damage:
      types:
        Structural: 1
";

    /// <summary>A mount with no armor next to it takes an explosion in full.</summary>
    [Test]
    public async Task MountAloneTakesExplosionInFull()
    {
        await RunCase(null, explosion: true, projectile: false, expectMount: 100, expectWall: 0);
    }

    /// <summary>A thick wall next to the mount takes 80% of an explosion, leaving 20% on the mount.</summary>
    [Test]
    public async Task ThickWallTakesExplosionShare()
    {
        await RunCase(ThickWallProto, explosion: true, projectile: false, expectMount: 20, expectWall: 80);
    }

    /// <summary>A thin wall takes half the explosion share, since its thickness is half of full coverage.</summary>
    [Test]
    public async Task ThinWallTakesScaledExplosionShare()
    {
        await RunCase(ThinWallProto, explosion: true, projectile: false, expectMount: 60, expectWall: 40);
    }

    /// <summary>Plain damage without a tool is never shared with armor.</summary>
    [Test]
    public async Task PlainDamageStaysOnMount()
    {
        await RunCase(ThickWallProto, explosion: false, projectile: false, expectMount: 100, expectWall: 0);
    }

    /// <summary>A projectile hit takes 40% of the share onto a fully covering wall.</summary>
    [Test]
    public async Task ProjectileHitTakesShare()
    {
        await RunCase(ThickWallProto, explosion: false, projectile: true, expectMount: 60, expectWall: 40);
    }

    /// <summary>A 2x2 mount reaches armor two tiles away from its origin tile, past the old 3x3 box.</summary>
    [Test]
    public async Task MultiTileMountFindsArmorPastItsCenterTile()
    {
        await RunCase(ThickWallProto, explosion: true, projectile: false, expectMount: 20, expectWall: 80,
            mountProto: MultiTileMountProto, wallPos: new Vector2(2.5f, 0.5f), tiles: new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) });
    }

    /// <summary>A non-hard fixture (such as a sensor) does not extend the mount's footprint.</summary>
    [Test]
    public async Task NonHardFixtureDoesNotExtendFootprint()
    {
        await RunCase(ThickWallProto, explosion: true, projectile: false, expectMount: 100, expectWall: 0,
            mountProto: HardFixtureMountProto, wallPos: new Vector2(0.5f, -1.5f), tiles: new[] { new Vector2i(0, 0), new Vector2i(0, -2) });
    }

    /// <summary>
    /// Spawns a mount (and optional wall) on the test grid, deals 100 Structural damage and checks the split.
    /// Tolerance covers fixed-point rounding of the shared share.
    /// </summary>
    private static async Task RunCase(string? wallProto, bool explosion, bool projectile, float expectMount, float expectWall,
        string mountProto = MountProto, Vector2? wallPos = null, Vector2i[]? tiles = null)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var xformSys = server.System<SharedTransformSystem>();
        var damageable = server.System<DamageableSystem>();

        var grid = map.Grid;
        EntityUid mount = default;
        EntityUid wall = default;
        EntityUid tool = default;

        await server.WaitAssertion(() =>
        {
            foreach (var tile in tiles ?? new[] { new Vector2i(0, 0), new Vector2i(1, 0) })
                mapSys.SetTile(grid.Owner, grid.Comp, tile, new Tile(1));

            mount = entMan.SpawnEntity(mountProto, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            xformSys.AnchorEntity((mount, entMan.GetComponent<TransformComponent>(mount)));

            if (wallProto is not null)
            {
                wall = entMan.SpawnEntity(wallProto, new EntityCoordinates(grid.Owner, wallPos ?? new Vector2(1.5f, 0.5f)));
                xformSys.AnchorEntity((wall, entMan.GetComponent<TransformComponent>(wall)));
            }

            if (projectile)
                tool = entMan.SpawnEntity(ProjectileProto, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { { "Structural", FixedPoint2.New(100) } } };

            if (explosion)
            {
                damageable.TryChangeDamage(mount, damage, ignoreResistances: true, ignoreGlobalModifiers: true,
                    originFlag: DamageableSystem.DamageOriginFlag.Explosion);
            }
            else
            {
                damageable.TryChangeDamage(mount, damage, tool: projectile ? tool : null);
            }
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DamageableComponent>(mount).TotalDamage.Float(),
                Is.EqualTo(expectMount).Within(0.05));

            if (wallProto is not null)
            {
                Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Float(),
                    Is.EqualTo(expectWall).Within(0.05));
            }
        });

        await pair.CleanReturnAsync();
    }
}
