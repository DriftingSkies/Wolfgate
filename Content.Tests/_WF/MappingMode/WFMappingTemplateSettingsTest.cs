#nullable enable
using System;
using System.Numerics;
using Content.Client._WF.MappingMode.Template;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingTemplateSettingsTest
{
    [Test]
    public void RoundTrip()
    {
        var settings = new WFMappingTemplateSettings
        {
            Position = new Vector2(12.5f, -3.25f),
            Uniform = false,
            Rotation = 33.3f,
            Opacity = 0.75f,
            PixelsPerTile = 64,
            Locked = true,
            Visible = false,
            Layer = WFMappingTemplateLayer.Over,
            Depth = -2,
        };
        settings.SetScaleX(2.5f);
        settings.SetScaleY(0.1f);

        var read = WFMappingTemplateSettings.Read(settings.Write());
        Assert.Multiple(() =>
        {
            Assert.That(read.Position, Is.EqualTo(settings.Position));
            Assert.That(read.ScaleX, Is.EqualTo(2.5f));
            Assert.That(read.ScaleY, Is.EqualTo(0.1f));
            Assert.That(read.Uniform, Is.False);
            Assert.That(read.Rotation, Is.EqualTo(33.3f));
            Assert.That(read.Opacity, Is.EqualTo(0.75f));
            Assert.That(read.PixelsPerTile, Is.EqualTo(64));
            Assert.That(read.Locked, Is.True);
            Assert.That(read.Visible, Is.False);
            Assert.That(read.Layer, Is.EqualTo(WFMappingTemplateLayer.Over));
            Assert.That(read.Depth, Is.EqualTo(-2));
            Assert.That(read.Write(), Is.EqualTo(settings.Write()));
        });
    }

    [TestCase("")]
    [TestCase("not: [closed")]
    [TestCase("- a list")]
    public void UnreadableTextGivesDefaults(string text)
    {
        var read = WFMappingTemplateSettings.Read(text);
        Assert.That(read.Write(), Is.EqualTo(new WFMappingTemplateSettings().Write()));
    }

    [Test]
    public void ReadClampsAndKeepsDefaultsForBadValues()
    {
        const string text = @"
x: 1e9
y: nan
scaleX: 5000
scaleY: -1
rotation: 540
opacity: 3
pixelsPerTile: 0
uniform: maybe
locked: true
layer: sideways
";
        var read = WFMappingTemplateSettings.Read(text);
        Assert.Multiple(() =>
        {
            Assert.That(read.Position.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
            Assert.That(read.Position.Y, Is.EqualTo(0f), "NaN keeps the default.");
            Assert.That(read.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MaxScale));
            Assert.That(read.ScaleY, Is.EqualTo(WFMappingTemplateSettings.MinScale));
            Assert.That(read.Rotation, Is.EqualTo(180f));
            Assert.That(read.Opacity, Is.EqualTo(1f));
            Assert.That(read.PixelsPerTile, Is.EqualTo(WFMappingTemplateSettings.MinPixelsPerTile));
            Assert.That(read.Uniform, Is.True, "An unreadable flag keeps its default.");
            Assert.That(read.Locked, Is.True);
            Assert.That(read.Layer, Is.EqualTo(WFMappingTemplateLayer.Under));
        });
    }

    [Test]
    public void SettersClamp()
    {
        var settings = new WFMappingTemplateSettings
        {
            Position = new Vector2(-1e7f, float.PositiveInfinity),
            ScaleX = 0f,
            ScaleY = float.NaN,
            Opacity = float.NaN,
            PixelsPerTile = 100000,
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Position, Is.EqualTo(new Vector2(-WFMappingTemplateSettings.MaxOffset, 0f)));
            Assert.That(settings.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MinScale));
            Assert.That(settings.ScaleY, Is.EqualTo(1f));
            Assert.That(settings.Opacity, Is.EqualTo(WFMappingTemplateSettings.DefaultOpacity));
            Assert.That(settings.PixelsPerTile, Is.EqualTo(WFMappingTemplateSettings.MaxPixelsPerTile));
        });
    }

    [TestCase(0f, 0f)]
    [TestCase(180f, 180f)]
    [TestCase(-180f, 180f)]
    [TestCase(190f, -170f)]
    [TestCase(-190f, 170f)]
    [TestCase(720f, 0f)]
    [TestCase(float.NaN, 0f)]
    public void RotationWraps(float degrees, float expected)
    {
        Assert.That(WFMappingTemplateSettings.WrapDegrees(degrees), Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void UniformScaleKeepsTheRatio()
    {
        var settings = new WFMappingTemplateSettings { ScaleX = 2f, ScaleY = 1f };
        settings.SetScaleX(4f);
        Assert.That(settings.ScaleY, Is.EqualTo(2f));

        settings.Uniform = false;
        settings.SetScaleY(3f);
        Assert.That(settings.ScaleX, Is.EqualTo(4f));
    }

    [Test]
    public void ResetKeepsTheDepth()
    {
        var settings = new WFMappingTemplateSettings { Depth = 3, ScaleX = 7f, Opacity = 0.1f };
        settings.Reset(new Vector2(4f, 5f));
        Assert.Multiple(() =>
        {
            Assert.That(settings.Depth, Is.EqualTo(3));
            Assert.That(settings.Position, Is.EqualTo(new Vector2(4f, 5f)));
            Assert.That(settings.ScaleX, Is.EqualTo(1f));
            Assert.That(settings.Opacity, Is.EqualTo(WFMappingTemplateSettings.DefaultOpacity));
        });
    }

    [TestCase("Outpost", "Outpost")]
    [TestCase("  My map_1  ", "My map_1")]
    [TestCase("a/b", "612F62")]
    [TestCase("..", "2E2E")]
    [TestCase("Ünï", "C39C6EC3AF")]
    public void SessionNameKey(string name, string expected)
    {
        Assert.That(WFMappingTemplateSystem.FolderKey(name), Is.EqualTo(expected));
    }

    /// <summary>A PNG signature and IHDR chunk for an image of the given size, plus <paramref name="extra"/> bytes.</summary>
    private static byte[] Png(uint width, uint height, int extra = 0)
    {
        var data = new byte[33 + extra];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        WriteUInt32(data, 8, 13);
        new[] { (byte) 'I', (byte) 'H', (byte) 'D', (byte) 'R' }.CopyTo(data, 12);
        WriteUInt32(data, 16, width);
        WriteUInt32(data, 20, height);
        data[24] = 8;
        data[25] = 6;
        return data;
    }

    private static void WriteUInt32(byte[] data, int at, uint value)
    {
        data[at] = (byte) (value >> 24);
        data[at + 1] = (byte) (value >> 16);
        data[at + 2] = (byte) (value >> 8);
        data[at + 3] = (byte) value;
    }

    [Test]
    public void PngOfOnePixel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingPngHeader.Check(Png(1, 1), out var width, out var height), Is.EqualTo(WFMappingPngCheck.Ok));
            Assert.That(width, Is.EqualTo(1));
            Assert.That(height, Is.EqualTo(1));
        });
    }

    [Test]
    public void PngAtTheCapsIsAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingPngHeader.Check(Png(WFMappingPngHeader.MaxSide, 2048), out _, out _), Is.EqualTo(WFMappingPngCheck.Ok));
            Assert.That(WFMappingPngHeader.Check(Png(4096, 4096), out _, out _), Is.EqualTo(WFMappingPngCheck.Ok));
        });
    }

    [TestCase(8193u, 1u)]
    [TestCase(1u, 8193u)]
    [TestCase(8192u, 4096u)]
    [TestCase(5000u, 5000u)]
    [TestCase(uint.MaxValue, 1u)]
    public void OversizedPngIsRefused(uint width, uint height)
    {
        var expected = width > int.MaxValue ? WFMappingPngCheck.NotPng : WFMappingPngCheck.ImageTooLarge;
        Assert.That(WFMappingPngHeader.Check(Png(width, height), out _, out _), Is.EqualTo(expected));
    }

    [Test]
    public void OversizedFileIsRefused()
    {
        Assert.That(WFMappingPngHeader.Check(Png(1, 1, WFMappingPngHeader.MaxFileBytes), out _, out _),
            Is.EqualTo(WFMappingPngCheck.FileTooLarge));
    }

    [Test]
    public void NonPngIsRefused()
    {
        var jpeg = Png(1, 1);
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        var noIhdr = Png(1, 1);
        noIhdr[12] = (byte) 'I';
        noIhdr[13] = (byte) 'D';
        noIhdr[14] = (byte) 'A';
        noIhdr[15] = (byte) 'T';
        var badLength = Png(1, 1);
        WriteUInt32(badLength, 8, 12);

        Assert.Multiple(() =>
        {
            Assert.That(WFMappingPngHeader.Check(jpeg, out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(noIhdr, out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(badLength, out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(Png(0, 5), out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng),
                "A zero side.");
        });
    }

    [Test]
    public void TruncatedHeaderIsRefused()
    {
        var full = Png(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingPngHeader.Check(full.AsSpan(0, 23), out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(full.AsSpan(0, 8), out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(ReadOnlySpan<byte>.Empty, out _, out _), Is.EqualTo(WFMappingPngCheck.NotPng));
            Assert.That(WFMappingPngHeader.Check(full.AsSpan(0, 24), out _, out _), Is.EqualTo(WFMappingPngCheck.Ok),
                "The size is all the check needs.");
        });
    }

    [Test]
    public void PngCheckTakesItsOwnFileCap()
    {
        var png = Png(1, 1, 100);
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingPngHeader.Check(png, png.Length, out _, out _), Is.EqualTo(WFMappingPngCheck.Ok));
            Assert.That(WFMappingPngHeader.Check(png, png.Length - 1, out _, out _),
                Is.EqualTo(WFMappingPngCheck.FileTooLarge));
            Assert.That(WFMappingPngHeader.Check(Png(9000, 1), png.Length, out _, out _),
                Is.EqualTo(WFMappingPngCheck.ImageTooLarge), "The image caps hold whatever the file cap.");
        });
    }

    [Test]
    public void StateRoundTripLeavesLocalOptions()
    {
        var source = new WFMappingTemplateSettings
        {
            Position = new Vector2(7.5f, -2f),
            ScaleX = 3f,
            ScaleY = 0.5f,
            Rotation = -45f,
            PixelsPerTile = 16,
            Layer = WFMappingTemplateLayer.Over,
            Depth = 1,
        };

        var target = new WFMappingTemplateSettings { Opacity = 0.9f, Visible = false, Locked = true, Uniform = false };
        target.ApplyState(source.ToState());
        Assert.Multiple(() =>
        {
            Assert.That(target.ToState(), Is.EqualTo(source.ToState()));
            Assert.That(target.Opacity, Is.EqualTo(0.9f), "Opacity stays with the player.");
            Assert.That(target.Visible, Is.False);
            Assert.That(target.Locked, Is.True);
            Assert.That(target.Uniform, Is.False);
        });
    }

    [Test]
    public void ClampKeepsAStateInBounds()
    {
        var wild = new WFMappingTemplateState(1e9f, float.NaN, 5000f, 0f, 270f, 0, (WFMappingTemplateLayer) 9, 7);
        var clamped = WFMappingTemplateSettings.Clamp(wild, -1, 2);
        Assert.Multiple(() =>
        {
            Assert.That(clamped.X, Is.EqualTo(WFMappingTemplateSettings.MaxOffset));
            Assert.That(clamped.Y, Is.EqualTo(0f));
            Assert.That(clamped.ScaleX, Is.EqualTo(WFMappingTemplateSettings.MaxScale));
            Assert.That(clamped.ScaleY, Is.EqualTo(WFMappingTemplateSettings.MinScale));
            Assert.That(clamped.Rotation, Is.EqualTo(-90f).Within(1e-4f));
            Assert.That(clamped.PixelsPerTile, Is.EqualTo(WFMappingTemplateSettings.MinPixelsPerTile));
            Assert.That(clamped.Layer, Is.EqualTo(WFMappingTemplateLayer.Under), "An unknown layer is Under.");
            Assert.That(clamped.Depth, Is.EqualTo(2));
            Assert.That(WFMappingTemplateSettings.Clamp(wild with { Depth = -5 }, -1, 2).Depth, Is.EqualTo(-1));
            Assert.That(WFMappingTemplateSettings.Clamp(clamped, -1, 2), Is.EqualTo(clamped), "Clamping is stable.");
        });
    }
}
