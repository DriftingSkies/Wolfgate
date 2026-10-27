using Content.Shared._WF.CustomMarkings;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.CustomMarkings;

/// <summary>Frame order tools and playing back and forth.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingArt))]
public sealed class CustomMarkingAnimationTest
{
    /// <summary>Art with one frame per time given, each frame marked by its index in the red channel.</summary>
    private static CustomMarkingArt Numbered(params int[] times)
    {
        var art = new CustomMarkingArt();
        for (var i = 1; i < times.Length; i++)
        {
            art.AddFrame(i - 1, int.MaxValue);
        }

        for (var i = 0; i < times.Length; i++)
        {
            art.SetPixel(i, CustomMarkingArt.South, 0, 0, new Rgba32((byte) (i + 1), 0, 0, 255));
            art.SetFrameTime(i, times[i]);
        }

        return art;
    }

    private static int[] Marks(CustomMarkingArt art)
    {
        var marks = new int[art.Frames];
        for (var i = 0; i < marks.Length; i++)
        {
            marks[i] = art.GetPixel(i, CustomMarkingArt.South, 0, 0).R - 1;
        }

        return marks;
    }

    [Test]
    public void MoveAndReverseTest()
    {
        var art = Numbered(100, 200, 300, 400);

        Assert.That(art.MoveFrame(0, 2), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(Marks(art), Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(art.GetFrameTimes(), Is.EqualTo(new[] { 200, 300, 100, 400 }), "times move with their frames");
        });

        Assert.That(art.MoveFrame(3, 0), Is.True);
        Assert.That(Marks(art), Is.EqualTo(new[] { 3, 1, 2, 0 }));
        Assert.That(art.MoveFrame(1, 1), Is.False);
        Assert.That(art.MoveFrame(0, 4), Is.False, "past the last frame");

        Assert.That(art.ReverseFrames(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(Marks(art), Is.EqualTo(new[] { 0, 2, 1, 3 }));
            Assert.That(art.GetFrameTimes(), Is.EqualTo(new[] { 100, 300, 200, 400 }));
        });

        Assert.That(art.SetAllFrameTimes(250), Is.True);
        Assert.That(art.GetFrameTimes(), Is.EqualTo(new[] { 250, 250, 250, 250 }));
        Assert.That(art.SetAllFrameTimes(250), Is.False);
        Assert.That(new CustomMarkingArt().ReverseFrames(), Is.False, "a still marking has nothing to reverse");
    }

    [Test]
    public void PingPongTest()
    {
        var art = Numbered(100, 200, 300, 400);
        Assert.That(art.PlayOrder(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(art.Played(), Is.SameAs(art));

        art.PingPong = true;
        var played = art.Played();
        Assert.Multiple(() =>
        {
            Assert.That(art.PlayOrder(), Is.EqualTo(new[] { 0, 1, 2, 3, 2, 1 }));
            Assert.That(Marks(played), Is.EqualTo(new[] { 0, 1, 2, 3, 2, 1 }));
            Assert.That(played.GetFrameTimes(), Is.EqualTo(new[] { 100, 200, 300, 400, 300, 200 }));
            Assert.That(played.PingPong, Is.False, "the drawn-out sheet just starts over");
            Assert.That(art.Same(art.Clone()), Is.True);
            Assert.That(art.Fingerprint(), Is.Not.EqualTo(Numbered(100, 200, 300, 400).Fingerprint()));
        });

        // Two frames back and forth is the same as round again.
        var two = Numbered(100, 200);
        two.PingPong = true;
        Assert.Multiple(() =>
        {
            Assert.That(two.PingPongs, Is.False);
            Assert.That(two.PlayOrder(), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(CustomMarkingArt.FromPixels(two.Pixels, two.GetFrameTimes(), pingPong: true)!.PingPong, Is.False);
            Assert.That(CustomMarkingArt.FromPixels(art.Pixels, art.GetFrameTimes(), pingPong: true)!.PingPongs, Is.True);
        });
    }

    [Test]
    public void PackedPingPongTest()
    {
        var times = new[] { 20, 300, CustomMarkingRules.MaxFrameTime };
        var plain = CustomMarkingRules.PackFrameTimes(times);
        var back = CustomMarkingRules.PackFrameTimes(times, true);
        Assert.Multiple(() =>
        {
            Assert.That(CustomMarkingRules.UnpackPingPong(plain), Is.False);
            Assert.That(CustomMarkingRules.UnpackPingPong(back), Is.True);
            Assert.That(CustomMarkingRules.UnpackFrameTimes(back), Is.EqualTo(times), "the flag doesn't change the times");
            Assert.That(back, Is.Not.EqualTo(plain), "so the art hashes apart");
            Assert.That(CustomMarkingRules.UnpackPingPong(null), Is.False);
            Assert.That(CustomMarkingRules.MaxFrameTime, Is.LessThan(0x8000), "a time never reaches the flag bit");
        });
    }
}
