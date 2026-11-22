#nullable enable
using System.Linq;
using System.Reflection;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Ci;

/// <summary>The stale-bounds probe reads engine internals by name, so this checks it still finds what it looks for.</summary>
[TestFixture]
public sealed class WGStaleSpriteBoundsTest
{
    [Test]
    public async Task NamesALayerWhoseBoundsWentStale()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var entities = client.EntMan;
            var crowbar = entities.SpawnEntity("Crowbar", MapCoordinates.Nullspace);
            var sprite = entities.GetComponent<SpriteComponent>(crowbar);
            entities.System<SpriteSystem>().GetLocalBounds((crowbar, sprite));
            var name = entities.ToPrettyString(crowbar).ToString();
            Assert.That(WGStaleSpriteBounds.Describe(entities) ?? "", Does.Not.Contain(name), "a fresh crowbar was reported stale.");

            var layer = sprite.AllLayers.OfType<SpriteComponent.Layer>().First();
            typeof(SpriteComponent.Layer).GetField("Bounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(layer, new Box2(0f, 0f, 5f, 5f));
            Assert.That(WGStaleSpriteBounds.Describe(entities), Does.Contain(name), "a crowbar with stale bounds wasn't reported.");
            entities.DeleteEntity(crowbar);
        });
        await pair.CleanReturnAsync();
    }
}
