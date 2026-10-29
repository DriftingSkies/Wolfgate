#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server.Physics.Controllers;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>A floating mob's reused push-off result still follows it when it moves.</summary>
[TestFixture]
[TestOf(typeof(SharedMoverController))]
public sealed class WFAroundColliderTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestPushOffBlock
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        mask:
        - FullTileMask
";

    [Test]
    public async Task ReusedResultFollowsTheMob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var mover = server.System<MoverController>();
        var check = typeof(SharedMoverController).GetMethod("WfIsAroundCollider",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lookup = server.System<EntityLookupSystem>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("WFTestPushOffBlock", new MapCoordinates(Vector2.Zero, map.MapId));
            mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(1.2f, 0f), map.MapId));
        });
        await server.WaitRunTicks(2);

        bool Touching()
        {
            var ent = new Entity<PhysicsComponent, MobMoverComponent, TransformComponent>(mob,
                entMan.GetComponent<PhysicsComponent>(mob), entMan.GetComponent<MobMoverComponent>(mob),
                entMan.GetComponent<TransformComponent>(mob));
            return (bool) check.Invoke(mover, new object[] { lookup, ent })!;
        }

        await server.WaitAssertion(() => Assert.That(Touching(), "A mob beside a wall should be able to push off it."));

        // Within the reuse window, a move away and back again must both show at once.
        await server.WaitAssertion(() =>
        {
            xforms.SetWorldPosition(mob, new Vector2(20f, 0f));
            Assert.That(Touching(), Is.False, "A mob that moved away still pushed off the wall.");
            xforms.SetWorldPosition(mob, new Vector2(1.2f, 0f));
            Assert.That(Touching(), "A mob back beside the wall couldn't push off it.");
        });

        await pair.CleanReturnAsync();
    }
}
