using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.CustomMarkings;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.CustomMarkings;

/// <summary>
/// Art that plays back and forth is drawn from a sheet with the way back laid out, while the editor still reads it
/// as drawn.
/// </summary>
[TestFixture]
[TestOf(typeof(CustomMarkingResources))]
public sealed class CustomMarkingPingPongTest
{
    private static readonly string Hash = new('9', CustomMarkingRules.HashLength);

    [Test]
    public async Task PingPongSheetTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var art = new CustomMarkingArt();
            for (var i = 0; i < 3; i++)
            {
                if (i > 0)
                    art.AddFrame(i - 1);

                art.SetPixel(i, CustomMarkingArt.South, i, 0, new Rgba32(255, 0, 0, 255));
                art.SetFrameTime(i, 100 * (i + 1));
            }

            art.PingPong = true;

            var system = client.EntMan.System<CustomMarkingSystem>();
            system.Remember(Hash, art);
            Assert.That(system.TryGetArt(Hash, out var rsi), Is.True);
            Assert.That(rsi!.TryGetState(CustomMarkingResources.State, out var state), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(state!.DelayCount, Is.EqualTo(4), "three frames there and one on the way back");
                Assert.That(state.GetDelay(3), Is.EqualTo(0.2f).Within(0.001f), "the way back keeps the frames' times");
            });

            Assert.That(system.TryReadArt(Hash, out var read), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(read!.Frames, Is.EqualTo(3), "the editor gets the frames as drawn");
                Assert.That(read.PingPong, Is.True);
                Assert.That(read.Same(art), Is.True);
            });

            CustomMarkingResources.For(client.ResolveDependency<IResourceCache>()).Remove(Hash);
        });

        await pair.CleanReturnAsync();
    }
}
