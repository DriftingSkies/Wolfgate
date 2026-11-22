using Content.Client._WG.CustomMarkings;
using Content.Shared._WG.CustomMarkings;
using NUnit.Framework;
using Robust.Shared.Maths;
using SixLabors.ImageSharp.PixelFormats;
using ServerCustomMarkingSystem = Content.Server._WG.CustomMarkings.CustomMarkingSystem;

namespace Content.Tests._WG.CustomMarkings;

/// <summary>Glowing pixels: which can glow, how glow follows the pixels through edits, and how it is split and hashed.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingArt))]
public sealed class CustomMarkingGlowTest
{
    private const int South = CustomMarkingArt.South;
    private const int East = CustomMarkingArt.East;
    private const int West = CustomMarkingArt.West;

    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 128);

    /// <summary>Only a drawn pixel glows; recolouring keeps the glow and clearing the pixel drops it.</summary>
    [Test]
    public void GlowFollowsPixelTest()
    {
        var art = new CustomMarkingArt();
        Assert.That(art.SetGlow(0, South, 3, 3, true), Is.False, "an undrawn pixel can't glow");

        art.SetPixel(0, South, 3, 3, Red);
        Assert.That(art.SetGlow(0, South, 3, 3, true), Is.True);
        Assert.That(art.SetGlow(0, South, 3, 3, true), Is.False);
        Assert.That(art.HasGlow(), Is.True);

        art.SetPixel(0, South, 3, 3, Blue);
        Assert.That(art.IsGlowing(0, South, 3, 3), Is.True, "recolouring keeps the glow");

        art.SetPixel(0, South, 3, 3, default);
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, South, 3, 3), Is.False, "clearing the pixel drops the glow");
            Assert.That(art.HasGlow(), Is.False);
        });
    }

    /// <summary>Moving, flipping and copying a facing take the glow along; clearing glow leaves the pixels.</summary>
    [Test]
    public void FacingOperationsTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(0, East, 1, 2, Red);
        art.SetPixel(0, East, 5, 5, Red);
        art.SetGlow(0, East, 1, 2, true);

        art.Shift(0, East, 2, 1);
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, East, 3, 3), Is.True);
            Assert.That(art.IsGlowing(0, East, 1, 2), Is.False);
            Assert.That(art.IsGlowing(0, East, 7, 6), Is.False, "the pixel moved beside it doesn't glow");
        });

        art.Flip(0, East, 30);
        Assert.That(art.IsGlowing(0, East, 27, 3), Is.True);

        art.CopyFacing(0, East, West, true);
        Assert.That(art.IsGlowing(0, West, CustomMarkingRules.FrameSize - 1 - 27, 3), Is.True);

        art.ClearGlow(0, East);
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, East, 27, 3), Is.False);
            Assert.That(art.GetPixel(0, East, 27, 3), Is.EqualTo(Red), "the pixel stays");
            Assert.That(art.IsGlowing(0, West, CustomMarkingRules.FrameSize - 1 - 27, 3), Is.True, "other facings keep theirs");
        });

        art.Clear(0, West);
        Assert.That(art.HasGlow(), Is.False);
    }

    /// <summary>Each frame has its own glow, which goes with the frame wherever it is moved.</summary>
    [Test]
    public void FramesTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(0, South, 0, 0, Red);
        art.SetGlow(0, South, 0, 0, true);

        Assert.That(art.AddFrame(0), Is.EqualTo(1));
        art.SetPixel(1, South, 1, 0, Red);
        art.AddFrame(1);
        art.SetPixel(2, South, 2, 0, Red);
        art.SetGlow(2, South, 2, 0, true);

        Assert.Multiple(() =>
        {
            Assert.That(art.Glow, Has.Length.EqualTo(3 * CustomMarkingRules.EraseBytes));
            Assert.That(art.IsGlowing(1, South, 0, 0), Is.True, "a new frame copies the glow of the one it copies");
            Assert.That(art.IsGlowing(1, South, 1, 0), Is.False);
        });

        art.ReverseFrames();
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, South, 2, 0), Is.True);
            Assert.That(art.IsGlowing(2, South, 2, 0), Is.False);
            Assert.That(art.IsGlowing(2, South, 0, 0), Is.True);
        });

        art.MoveFrame(0, 2);
        Assert.That(art.IsGlowing(2, South, 2, 0), Is.True);

        art.RemoveFrame(2);
        Assert.Multiple(() =>
        {
            Assert.That(art.Glow, Has.Length.EqualTo(2 * CustomMarkingRules.EraseBytes));
            Assert.That(art.IsGlowing(0, South, 2, 0) || art.IsGlowing(1, South, 2, 0), Is.False);
        });

        var pingPong = new CustomMarkingArt();
        pingPong.SetPixel(0, South, 0, 0, Red);
        pingPong.AddFrame(0);
        pingPong.AddFrame(1);
        pingPong.SetGlow(1, South, 0, 0, true);
        pingPong.PingPong = true;
        var played = pingPong.Played();
        Assert.Multiple(() =>
        {
            Assert.That(played.Frames, Is.EqualTo(4));
            Assert.That(played.IsGlowing(1, South, 0, 0), Is.True);
            Assert.That(played.IsGlowing(3, South, 0, 0), Is.True, "the way back shows the middle frame again");
            Assert.That(played.IsGlowing(2, South, 0, 0), Is.False);
        });
    }

    /// <summary>An upload's glow mask must cover every frame; glow on undrawn pixels is dropped.</summary>
    [Test]
    public void UploadTest()
    {
        var pixels = new byte[CustomMarkingRules.PixelBytes];
        pixels[3] = 255;
        var glow = new byte[CustomMarkingRules.EraseBytes];
        glow[0] = 0b11;

        var art = CustomMarkingArt.FromPixels(pixels, glow: glow)!;
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, South, 0, 0), Is.True);
            Assert.That(art.IsGlowing(0, South, 1, 0), Is.False, "nothing is drawn there");
            Assert.That(art.Glow, Is.Not.SameAs(glow));
            Assert.That(CustomMarkingArt.FromPixels(pixels, glow: new byte[2 * CustomMarkingRules.EraseBytes]), Is.Null,
                "a mask for each frame, no more");
        });
    }

    /// <summary>Glow counts as a change to the art, and is undone like any other.</summary>
    [Test]
    public void SameAndUndoTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(0, South, 4, 4, Red);
        var before = art.Clone();
        var fingerprint = art.Fingerprint();

        art.SetGlow(0, South, 4, 4, true);
        Assert.Multiple(() =>
        {
            Assert.That(art.Same(before), Is.False);
            Assert.That(art.SamePixels(before), Is.True);
            Assert.That(art.Fingerprint(), Is.Not.EqualTo(fingerprint));
            Assert.That(art.Clone().IsGlowing(0, South, 4, 4), Is.True);
        });

        art.Restore(before);
        Assert.That(art.HasGlow(), Is.False);

        var sketch = new CustomMarkingSketch(before) { Mirror = true };
        sketch.Art.SetPixel(0, South, 26, 4, Red);
        sketch.Begin();
        sketch.GlowLine(0, South, new Vector2i(4, 4), new Vector2i(5, 4), true);
        sketch.End();
        Assert.Multiple(() =>
        {
            Assert.That(sketch.Art.IsGlowing(0, South, 4, 4), Is.True);
            Assert.That(sketch.Art.IsGlowing(0, South, 26, 4), Is.True, "mirrored about column 15");
            Assert.That(sketch.Art.IsGlowing(0, South, 5, 4), Is.False, "nothing is drawn there");
        });

        sketch.Undo();
        Assert.That(sketch.Art.HasGlow(), Is.False);
    }

    /// <summary>Splitting puts each drawn pixel on one side only, with the frames and their times kept.</summary>
    [Test]
    public void SplitTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(0, South, 1, 1, Red);
        art.SetPixel(0, South, 2, 1, Blue);
        art.SetGlow(0, South, 2, 1, true);
        art.AddFrame(0);
        art.SetFrameTime(1, 700);

        var (lit, glowing) = art.SplitGlow();
        Assert.Multiple(() =>
        {
            Assert.That(lit.GetPixel(0, South, 1, 1), Is.EqualTo(Red));
            Assert.That(lit.GetPixel(0, South, 2, 1).A, Is.Zero);
            Assert.That(lit.HasGlow(), Is.False);
            Assert.That(glowing.GetPixel(0, South, 2, 1), Is.EqualTo(Blue));
            Assert.That(glowing.GetPixel(0, South, 1, 1).A, Is.Zero);
            Assert.That(glowing.GetPixel(1, South, 2, 1), Is.EqualTo(Blue));
            Assert.That(lit.GetFrameTime(1), Is.EqualTo(700));
            Assert.That(glowing.GetFrameTime(1), Is.EqualTo(700));
        });
    }

    /// <summary>Art that doesn't glow keeps the hash it always had; glow makes a different one.</summary>
    [Test]
    public void HashTest()
    {
        var art = new CustomMarkingArt();
        art.SetPixel(0, South, 1, 1, Red);
        var plain = ServerCustomMarkingSystem.Hash(art);

        var glowing = art.Clone();
        glowing.SetGlow(0, South, 1, 1, true);
        var glowHash = ServerCustomMarkingSystem.Hash(glowing);

        var erasing = art.Clone();
        erasing.SetErased(South, 1, 1, true);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(ServerCustomMarkingSystem.Hash(art.Clone())));
            Assert.That(glowHash, Is.Not.EqualTo(plain));
            Assert.That(glowHash, Is.Not.EqualTo(ServerCustomMarkingSystem.Hash(erasing)),
                "a glow mask and an erase mask with the same bits differ");
            Assert.That(CustomMarkingRules.IsValidHash(glowHash), Is.True);
        });
    }

    /// <summary>A glow fill reaches the touching pixels of the clicked colour only, and the sketch mirrors it.</summary>
    [Test]
    public void GlowFillTest()
    {
        var art = new CustomMarkingArt();
        for (var x = 2; x <= 5; x++)
        {
            art.SetPixel(0, South, x, 4, Red);
        }

        art.SetPixel(0, South, 6, 4, Blue);
        art.SetPixel(0, South, 8, 4, Red);

        art.FillGlow(0, South, 2, 4, true);
        Assert.Multiple(() =>
        {
            Assert.That(art.IsGlowing(0, South, 5, 4), Is.True, "the touching red run glows");
            Assert.That(art.IsGlowing(0, South, 6, 4), Is.False, "another colour stops it");
            Assert.That(art.IsGlowing(0, South, 8, 4), Is.False, "red that doesn't touch stays dark");
        });

        art.FillGlow(0, South, 3, 4, false);
        Assert.That(art.HasGlow(), Is.False, "filling again with glow off clears the run");

        art.FillGlow(0, South, 0, 0, true);
        Assert.That(art.HasGlow(), Is.False, "an undrawn pixel fills nothing");

        var sketch = new CustomMarkingSketch(new CustomMarkingArt()) { Mirror = true, MirrorAxis = CustomMarkingSketch.DefaultMirrorAxis };
        sketch.Art.SetPixel(0, South, 10, 4, Red);
        sketch.Art.SetPixel(0, South, sketch.MirrorX(10), 4, Red);
        sketch.GlowFill(0, South, 10, 4, true);
        Assert.That(sketch.Art.IsGlowing(0, South, sketch.MirrorX(10), 4), Is.True, "the mirrored pixel glows too");
    }
}
