using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.CustomMarkings;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.CustomMarkings;

/// <summary>Imported PNGs saved in another colour space come out as sRGB, not washed out.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingColorSpace))]
public sealed class CustomMarkingColorSpaceTest
{
    private const int Frame = CustomMarkingRules.FrameSize;

    /// <summary>Display P3's primaries, D50 adapted, as a matrix-shaper ICC profile stores them.</summary>
    private static readonly float[][] P3Primaries =
    {
        new[] { 0.5151f, 0.2412f, -0.0011f },
        new[] { 0.2920f, 0.6922f, 0.0419f },
        new[] { 0.1571f, 0.0666f, 0.7841f },
    };

    [Test]
    public void PlainPngIsLeftAlone()
    {
        var png = Png(new Rgba32(200, 100, 50, 255));
        Assert.That(CustomMarkingColorSpace.Read(png), Is.Null);
        Assert.That(Pixel(png), Is.EqualTo(new Rgba32(200, 100, 50, 255)));
        Assert.That(CustomMarkingColorSpace.Read(WithChunk(png, "sRGB", new byte[] { 0 })), Is.Null, "an sRGB chunk says it already is");
    }

    [Test]
    public void DisplayP3ProfileGetsItsSaturationBack()
    {
        var png = WithChunk(Png(new Rgba32(200, 100, 100, 255)), "iCCP", IccChunk(Profile(P3Primaries, 2.2f)));
        Assert.That(CustomMarkingColorSpace.Read(png), Is.Not.Null);

        var pixel = Pixel(png);
        Assert.Multiple(() =>
        {
            Assert.That(pixel.R, Is.GreaterThan(200), "P3 red is redder than sRGB's");
            Assert.That(pixel.G, Is.LessThan(100));
            Assert.That(pixel.A, Is.EqualTo(255));
        });

        // A grey has no gamut to gain: it stays grey.
        var grey = Pixel(WithChunk(Png(new Rgba32(128, 128, 128, 255)), "iCCP", IccChunk(Profile(P3Primaries, 2.2f))));
        Assert.That(grey.R, Is.EqualTo(grey.G).Within(1));
        Assert.That(grey.G, Is.EqualTo(grey.B).Within(1));
    }

    [Test]
    public void LinearGammaIsBroughtToSrgb()
    {
        // gAMA 1.0: stored values are linear light, so half is a lighter grey in sRGB.
        var png = WithChunk(Png(new Rgba32(128, 128, 128, 255)), "gAMA", BigEndian(100000));
        Assert.That(Pixel(png).R, Is.EqualTo(188).Within(1));
        Assert.That(CustomMarkingColorSpace.Read(WithChunk(Png(Rgba32()), "gAMA", BigEndian(45455))), Is.Null, "the usual 1/2.2 is left alone");
    }

    [Test]
    public void HdrPqWhiteLandsOnWhite()
    {
        // BT.2020 primaries, PQ, full range: reference white of 203 nits is a PQ signal of about 0.58.
        var signal = (byte) MathF.Round(0.5806f * 255);
        var png = WithChunk(Png(new Rgba32(signal, signal, signal, 255)), "cICP", new byte[] { 9, 16, 0, 1 });
        var pixel = Pixel(png);
        Assert.That(pixel.R, Is.GreaterThanOrEqualTo(250), "HDR reference white is sRGB white, not a dull grey");
        Assert.That(pixel.R, Is.EqualTo(pixel.B).Within(2));
    }

    [Test]
    public void HugeCurveTableIsRefused()
    {
        // A curve claiming 2^30 entries overflowed an int length check and asked for gigabytes.
        var profile = Profile(P3Primaries, 2.2f);
        var at = profile.Length - 14;
        Assert.That(Encoding.ASCII.GetString(profile, at, 4), Is.EqualTo("curv"));
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(at + 8), 0x40000000);
        Assert.That(CustomMarkingColorSpace.FromIcc(profile), Is.Null);
    }

    [Test]
    public void WrongSizeIsRefusedBeforeDecoding()
    {
        using var image = new Image<Rgba32>(Frame * 4, Frame * 4);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        Assert.That(CustomMarkingPng.Read(stream.ToArray()), Is.Null);
    }

    [Test]
    public void BrokenProfileIsIgnored()
    {
        var png = WithChunk(Png(new Rgba32(10, 20, 30, 255)), "iCCP", new byte[] { (byte) 'x', 0, 0, 1, 2, 3 });
        Assert.That(CustomMarkingColorSpace.Read(png), Is.Null);
        Assert.That(Pixel(png), Is.EqualTo(new Rgba32(10, 20, 30, 255)));
    }

    private static Rgba32 Rgba32() => new(1, 2, 3, 255);

    private static Rgba32 Pixel(byte[] png)
    {
        var art = CustomMarkingPng.Read(png);
        Assert.That(art, Is.Not.Null);
        return art!.GetPixel(0, CustomMarkingArt.South, 0, 0);
    }

    /// <summary>One facing, filled with a colour.</summary>
    private static byte[] Png(Rgba32 color)
    {
        using var image = new Image<Rgba32>(Frame, Frame, color);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>The PNG with a chunk put in just before its image data.</summary>
    private static byte[] WithChunk(byte[] png, string type, byte[] data)
    {
        var at = 8;
        while (Encoding.ASCII.GetString(png, at + 4, 4) != "IDAT")
        {
            at += 12 + BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
        }

        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var chunk = BigEndian(data.Length).Concat(typed).Concat(BigEndian((int) Crc(typed))).ToArray();
        return png.Take(at).Concat(chunk).Concat(png.Skip(at)).ToArray();
    }

    private static byte[] IccChunk(byte[] profile)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            zlib.Write(profile);
        }

        return Encoding.ASCII.GetBytes("test").Concat(new byte[] { 0, 0 }).Concat(compressed.ToArray()).ToArray();
    }

    /// <summary>A minimal RGB matrix-shaper ICC profile: three primaries and one gamma curve for every channel.</summary>
    private static byte[] Profile(float[][] primaries, float gamma)
    {
        var tags = new (string Tag, byte[] Data)[]
        {
            ("rXYZ", Xyz(primaries[0])), ("gXYZ", Xyz(primaries[1])), ("bXYZ", Xyz(primaries[2])),
            ("rTRC", Curve(gamma)), ("gTRC", Curve(gamma)), ("bTRC", Curve(gamma)),
        };

        var profile = new byte[128 + 4 + tags.Length * 12 + tags.Sum(tag => tag.Data.Length)];
        Encoding.ASCII.GetBytes("RGB ").CopyTo(profile, 16);
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(profile, 20);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(128), tags.Length);
        var offset = 132 + tags.Length * 12;
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = profile.AsSpan(132 + i * 12);
            Encoding.ASCII.GetBytes(tags[i].Tag).CopyTo(entry);
            BinaryPrimitives.WriteInt32BigEndian(entry[4..], offset);
            BinaryPrimitives.WriteInt32BigEndian(entry[8..], tags[i].Data.Length);
            tags[i].Data.CopyTo(profile, offset);
            offset += tags[i].Data.Length;
        }

        BinaryPrimitives.WriteInt32BigEndian(profile, profile.Length);
        return profile;

        static byte[] Xyz(float[] xyz)
        {
            var data = new byte[20];
            Encoding.ASCII.GetBytes("XYZ ").CopyTo(data, 0);
            for (var i = 0; i < 3; i++)
            {
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8 + i * 4), (int) MathF.Round(xyz[i] * 65536));
            }

            return data;
        }

        static byte[] Curve(float gamma)
        {
            var data = new byte[14];
            Encoding.ASCII.GetBytes("curv").CopyTo(data, 0);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), (ushort) MathF.Round(gamma * 256));
            return data;
        }
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
