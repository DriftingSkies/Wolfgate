#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Shrapnel;
using Content.Server.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Shrapnel;

/// <summary>
/// A gyroscope destroyed by damage bursts into shrapnel that clears the machine frame left in its place.
/// </summary>
[TestFixture]
[TestOf(typeof(ShrapnelBehavior))]
public sealed class ShrapnelBehaviorTest
{
    private const string Target = "WGShrapnelTestTarget";
    private const string Blunt = "Blunt";
    private const int TargetCount = 8;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {Target}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.45
        layer:
        - MobLayer
  - type: Damageable
    damageContainer: Biological
";

    [Test]
    public async Task DestroyedGyroscopeFlingsShrapnel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var damageSys = entMan.System<DamageableSystem>();

        var gyroscope = EntityUid.Invalid;
        var targets = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var x = -3; x <= 3; x++)
            {
                for (var y = -3; y <= 3; y++)
                {
                    mapSys.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            var centre = new Vector2(0.5f, 0.5f);
            gyroscope = entMan.SpawnEntity("Gyroscope", new EntityCoordinates(map.Grid, centre));

            // A ring of crew a tile and a half out, clear of where the shrapnel starts.
            for (var i = 0; i < TargetCount; i++)
            {
                var pos = centre + Angle.FromDegrees(i * 360.0 / TargetCount).ToVec() * 1.5f;
                targets.Add(entMan.SpawnEntity(Target, new EntityCoordinates(map.Grid, pos)));
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            // Past the frame threshold (100) but short of the one that deletes it outright (300).
            var blunt = protoManager.Index<DamageTypePrototype>(Blunt);
            damageSys.TryChangeDamage(gyroscope, new DamageSpecifier(blunt, FixedPoint2.New(150)), ignoreResistances: true);
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(gyroscope), Is.True, "Test setup: the gyroscope should be destroyed.");

            var frames = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<MachineFrameComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId)
                    frames.Add(uid);
            }

            Assert.That(frames, Has.Count.EqualTo(1), "The gyroscope should leave a machine frame.");
            Assert.That(entMan.GetComponent<DamageableComponent>(frames[0]).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                "The shrapnel should start clear of the frame left on the gyroscope's tile.");

            var hit = 0;
            foreach (var target in targets)
            {
                var damage = entMan.GetComponent<DamageableComponent>(target).Damage.DamageDict;
                var taken = damage.Where(d => d.Value > FixedPoint2.Zero).Select(d => d.Key).ToList();

                Assert.That(taken, Is.SubsetOf(new[] { "Piercing" }), "Only shrapnel should hurt the crew, no blast.");
                if (taken.Count > 0)
                    hit++;
            }

            // Each target spans about three slices of the ring, so a miss is rare; allow a few.
            Assert.That(hit, Is.GreaterThanOrEqualTo(TargetCount - 3), "The shrapnel should hit the crew around it.");
        });

        await pair.CleanReturnAsync();
    }
}
