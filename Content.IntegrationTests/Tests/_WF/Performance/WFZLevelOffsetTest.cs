using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Checks the client Z-level sprite passes still lift and lower sprites while skipping entities at height 0.</summary>
[TestFixture]
public sealed class WFZLevelOffsetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFZLevelOffsetDummy
  components:
  - type: Sprite
    sprite: Objects/Misc/bureaucracy.rsi
    state: paper
  - type: CEZPhysics
";

    [Test]
    public async Task LiftsAndLowersSprites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid uid = default;
        await client.WaitPost(() =>
        {
            uid = client.EntMan.SpawnEntity("WFZLevelOffsetDummy", new MapCoordinates(Vector2.Zero, testMap.MapId));
        });

        Vector2 Offset() => client.EntMan.GetComponent<SpriteComponent>(uid).Offset;
        CEZPhysicsComponent Z() => client.EntMan.GetComponent<CEZPhysicsComponent>(uid);

        await client.WaitPost(() => Z().LocalPosition = 1f);
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() =>
        {
            Assert.That(Offset().Y, Is.EqualTo(CESharedZLevelsSystem.ZLevelOffset).Within(0.001f), "A raised entity's sprite should lift.");
            Assert.That(Z().WFHeightOffset, Is.True);
        });

        await client.WaitPost(() => Z().LocalPosition = 0f);
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() =>
        {
            Assert.That(Offset(), Is.EqualTo(Vector2.Zero), "A landed entity's sprite should drop back to its base offset.");
            Assert.That(Z().WFHeightOffset, Is.False);
        });

        // At height 0 the passes leave the offset alone, so another system's offset survives.
        await client.WaitPost(() =>
            client.System<SpriteSystem>().SetOffset(uid, new Vector2(0.3f, 0f)));
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() => Assert.That(Offset(), Is.EqualTo(new Vector2(0.3f, 0f))));

        await client.WaitPost(() => client.EntMan.DeleteEntity(uid));
        await pair.CleanReturnAsync();
    }
}
