using System.Linq;
using Content.Client._WF.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// Pooled test clients share one process but each has its own managers, so the icon's static helpers must use the
/// calling client's rather than ones kept from an earlier client.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateMarkingIcon))]
public sealed class WolfgateMarkingIconTest
{
    [Test]
    public async Task UsesCallingClientTest()
    {
        await using var first = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await using var second = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        foreach (var client in new[] { first.Client, second.Client })
        {
            await client.WaitAssertion(() =>
            {
                var sprite = client.ResolveDependency<MarkingManager>().Markings.Values
                    .SelectMany(marking => marking.Sprites)
                    .OfType<SpriteSpecifier.Rsi>()
                    .First();
                var own = client.System<SpriteSystem>().RsiStateLike(sprite).TextureFor(Direction.South);

                Assert.That(WolfgateMarkingIcon.FrameFor(sprite, Direction.South), Is.SameAs(own));
            });
        }

        await first.CleanReturnAsync();
        await second.CleanReturnAsync();
    }
}
