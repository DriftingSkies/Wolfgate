using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks the client Z-level sprite passes lift and lower raised entities and leave the rest alone.</summary>
[TestFixture]
public sealed class WGZLevelOffsetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGZLevelOffsetDummy
  components:
  - type: Sprite
    sprite: Objects/Misc/bureaucracy.rsi
    state: paper
  - type: CEZPhysics
";

    /// <summary>A height set on the client lifts the sprite, and landing lowers it and drops it from the list.</summary>
    [Test]
    public async Task LiftsAndLowersSprites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var zLevels = client.System<CEClientZLevelsSystem>();
        var testMap = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid uid = default;
        await client.WaitPost(() =>
        {
            uid = client.EntMan.SpawnEntity("WGZLevelOffsetDummy", new MapCoordinates(Vector2.Zero, testMap.MapId));
        });

        Vector2 Offset() => client.EntMan.GetComponent<SpriteComponent>(uid).Offset;

        await client.WaitPost(() => zLevels.SetZPosition(uid, 1f));
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() =>
        {
            Assert.That(Offset().Y, Is.EqualTo(CESharedZLevelsSystem.ZLevelOffset).Within(0.001f), "A raised entity's sprite should lift.");
            Assert.That(zLevels.WGRaised, Does.Contain(uid));
        });

        await client.WaitPost(() => zLevels.SetZPosition(uid, 0f));
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() =>
        {
            Assert.That(Offset(), Is.EqualTo(Vector2.Zero), "A landed entity's sprite should drop back to its base offset.");
            Assert.That(zLevels.WGRaised, Does.Not.Contain(uid), "A landed entity should leave the list.");
        });

        // At height 0 the passes leave the offset alone, so another system's offset survives.
        await client.WaitPost(() => client.System<SpriteSystem>().SetOffset(uid, new Vector2(0.3f, 0f)));
        await client.WaitRunTicks(3);
        await client.WaitAssertion(() => Assert.That(Offset(), Is.EqualTo(new Vector2(0.3f, 0f))));

        await client.WaitPost(() => zLevels.SetZPosition(uid, 1f));
        await client.WaitPost(() => client.EntMan.DeleteEntity(uid));
        await client.WaitAssertion(() => Assert.That(zLevels.WGRaised, Does.Not.Contain(uid), "A deleted entity should leave the list."));
        await pair.CleanReturnAsync();
    }

    /// <summary>A height sent by the server reaches the list through the state handler, for an entity the client never predicts.</summary>
    [Test]
    public async Task ServerHeightLiftsSprite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var zLevels = client.System<CEClientZLevelsSystem>();
        var serverZ = server.System<CESharedZLevelsSystem>();
        var testMap = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid sUid = default;
        await server.WaitPost(() => sUid = server.EntMan.SpawnEntity("WGZLevelOffsetDummy", testMap.MapCoords));
        await pair.RunTicksSync(5);

        var cUid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(sUid));
        Vector2 Offset() => client.EntMan.GetComponent<SpriteComponent>(cUid).Offset;

        await server.WaitPost(() => serverZ.SetZPosition(sUid, 0.5f));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.GetComponent<CEZPhysicsComponent>(cUid).LocalPosition, Is.EqualTo(0.5f));
            Assert.That(zLevels.WGRaised, Does.Contain(cUid));
            Assert.That(Offset().Y, Is.EqualTo(0.5f * CESharedZLevelsSystem.ZLevelOffset).Within(0.001f));
        });

        await server.WaitPost(() => serverZ.SetZPosition(sUid, 0f));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(Offset(), Is.EqualTo(Vector2.Zero));
            Assert.That(zLevels.WGRaised, Does.Not.Contain(cUid));
        });

        await pair.CleanReturnAsync();
    }
}
