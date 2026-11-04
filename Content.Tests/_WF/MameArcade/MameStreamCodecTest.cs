using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MameArcade;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.MameArcade;

/// <summary>What a cabinet streams to bystanders comes back out as it went in, and bad packets are refused.</summary>
[TestFixture]
[TestOf(typeof(MameVideoEncoder))]
[TestOf(typeof(MameAudioCodec))]
public sealed class MameStreamCodecTest
{
    private const int Width = 320;
    private const int Height = 224;

    [Test]
    public void WatcherSeesTheSameScreen()
    {
        var frame = Scene(0);
        var encoder = new MameVideoEncoder();
        var decoder = new MameVideoDecoder();

        var key = encoder.Encode(frame, Width, Height, keyframe: true);
        Assert.That(decoder.Decode(key));
        Assert.That(decoder.Width, Is.EqualTo(Width));
        AssertSame(decoder.Frame, frame);

        // A sprite moves: only its tiles go out again.
        var moved = Scene(12);
        var deck = new MameDeckState(MameControls.Right | MameControls.Button1, MameControls.Start, true, true);
        var change = encoder.Encode(moved, Width, Height, keyframe: false, deck);
        Assert.That(change.Length, Is.LessThan(key.Length / 4), "A change sends only the tiles that differ");
        Assert.That(decoder.Decode(change));
        AssertSame(decoder.Frame, moved);
        Assert.That(decoder.Deck, Is.EqualTo(deck), "The watcher's control deck shows what both players hold");

        var still = encoder.Encode(moved, Width, Height, keyframe: false);
        Assert.That(still.Length, Is.LessThan(64), "An unchanged screen costs next to nothing");
        Assert.That(decoder.Decode(still));
        AssertSame(decoder.Frame, moved);
    }

    [Test]
    public void LateWatcherWaitsForAKeyframe()
    {
        var encoder = new MameVideoEncoder();
        encoder.Encode(Scene(0), Width, Height, keyframe: true);
        var change = encoder.Encode(Scene(8), Width, Height, keyframe: false);

        var late = new MameVideoDecoder();
        Assert.That(late.Decode(change), Is.False, "A change means nothing without the screen it changes");

        var key = encoder.Encode(Scene(16), Width, Height, keyframe: true);
        Assert.That(late.Decode(key));
        AssertSame(late.Frame, Scene(16));
    }

    [Test]
    public void BadPicturePacketsAreRefused()
    {
        var decoder = new MameVideoDecoder();
        Assert.That(decoder.Decode(Array.Empty<byte>()), Is.False);
        Assert.That(decoder.Decode(new byte[] { 1, 0xff, 0xff, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3 }), Is.False, "Too large a screen");
        Assert.That(decoder.Decode(new byte[] { 1, 64, 1, 224, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xde, 0xad, 0xbe, 0xef }), Is.False, "Not deflate data");

        var key = new MameVideoEncoder().Encode(Scene(0), Width, Height, keyframe: true);
        Assert.That(decoder.Decode(key[..(key.Length / 2)]), Is.False, "Cut short");
    }

    [Test]
    public void PacketsCarryWhenTheFrameWasTaken()
    {
        var packet = new MameVideoEncoder().Encode(Scene(0), Width, Height, keyframe: true, stamp: 0xdeadbeef);
        Assert.That(MameVideoCodec.TryReadStamp(packet, out var stamp));
        Assert.That(stamp, Is.EqualTo(0xdeadbeef));
        Assert.That(new MameVideoDecoder().Decode(packet), "The stamp doesn't disturb the picture");
        Assert.That(MameVideoCodec.TryReadStamp(new byte[] { 1, 2, 3 }, out _), Is.False);
    }

    [Test]
    public void SoundSurvivesTheTrip()
    {
        var samples = Tone(0);
        var packet = MameAudioCodec.Encode(samples);
        Assert.That(packet.Length, Is.EqualTo(3 + samples.Length / 2), "4 bits a sample");

        var decoded = MameAudioCodec.Decode(packet);
        Assert.That(decoded, Has.Length.EqualTo(samples.Length));
        Assert.That(SignalToNoise(samples, decoded, samples.Length), Is.GreaterThan(20), "Signal to noise in dB");
        Assert.That(MameAudioCodec.Decode(new byte[] { 1 }), Is.Empty);
    }

    [Test]
    public void OddSoundPacketKeepsItsLength()
    {
        var samples = Tone(0)[..101];
        Assert.That(MameAudioCodec.Decode(MameAudioCodec.Encode(samples)), Has.Length.EqualTo(101));
    }

    [Test]
    public void APacketPicksUpTheStepWhereTheLastEnded()
    {
        var encoder = new MameAudioEncoder();
        encoder.Encode(Tone(0));
        var next = Tone(1);
        var carried = MameAudioCodec.Decode(encoder.Encode(next));
        var fresh = MameAudioCodec.Decode(MameAudioCodec.Encode(next));

        // The first millisecond, where a fresh start is still ramping up its step.
        const int head = MameAudioCodec.SampleRate / 1000;
        Assert.That(SignalToNoise(next, carried, head), Is.GreaterThan(SignalToNoise(next, fresh, head) + 10));
        Assert.That(SignalToNoise(next, carried, head), Is.GreaterThan(20));
    }

    [Test]
    public void DownmixKeepsTheToneAndDropsWhatWouldFoldBack()
    {
        Assert.That(Level(1000), Is.InRange(0.97, 1.03), "A 1 kHz tone passes");
        Assert.That(Level(8000), Is.InRange(0.9, 1.05), "So does an 8 kHz one");
        Assert.That(Level(20000), Is.LessThan(0.01), "A 20 kHz tone would come back as 4 kHz");
    }

    /// <summary>A tenth of a second of a loud 440 Hz tone at the codec's rate, the <paramref name="part"/>th one.</summary>
    private static short[] Tone(int part)
    {
        var samples = new short[MameAudioCodec.SampleRate / 10];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double) (part * samples.Length + i) / MameAudioCodec.SampleRate;
            samples[i] = (short) (12000 * Math.Sin(2 * Math.PI * 440 * t));
        }

        return samples;
    }

    private static double SignalToNoise(short[] expected, short[] actual, int count)
    {
        double signal = 0, noise = 0;
        for (var i = 0; i < count; i++)
        {
            signal += (double) expected[i] * expected[i];
            noise += Math.Pow(expected[i] - actual[i], 2);
        }

        return 10 * Math.Log10(signal / Math.Max(noise, 1));
    }

    /// <summary>How loud a 48 kHz stereo tone comes out of the downmix, against how it went in.</summary>
    private static double Level(double hertz)
    {
        var stereo = new List<short>();
        for (var i = 0; i < MameArcadeAudio.SampleRate / 10; i++)
        {
            var sample = (short) (10000 * Math.Sin(2 * Math.PI * hertz * i / MameArcadeAudio.SampleRate));
            stereo.Add(sample);
            stereo.Add(sample);
        }

        var mono = new List<short>();
        new MameAudioEncoder().Downmix(stereo, mono);
        Assert.That(stereo, Is.Empty, "Every frame is taken");
        Assert.That(mono, Has.Count.EqualTo(MameAudioCodec.SampleRate / 10));

        // Past the filter's fill.
        var settled = mono.Skip(64).ToArray();
        return Math.Sqrt(settled.Average(s => (double) s * s)) / (10000 / Math.Sqrt(2));
    }

    /// <summary>A gradient backdrop with a block sprite at <paramref name="x"/>, in colours 15-bit holds exactly.</summary>
    private static Rgba32[] Scene(int x)
    {
        var pixels = new Rgba32[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var i = 0; i < Width; i++)
            {
                pixels[y * Width + i] = Expand((byte) (i * 31 / Width), (byte) (y * 31 / Height), 8);
            }
        }

        for (var y = 100; y < 140; y++)
        {
            for (var i = 40 + x; i < 72 + x; i++)
            {
                pixels[y * Width + i] = Expand(31, (byte) (i % 7), (byte) (y % 5));
            }
        }

        return pixels;
    }

    private static Rgba32 Expand(byte r, byte g, byte b) => MameVideoCodec.Unpack((ushort) (r << 10 | g << 5 | b));

    private static void AssertSame(Rgba32[] actual, Rgba32[] expected)
    {
        var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => actual[i] != expected[i], -1);
        Assert.That(first, Is.EqualTo(-1), $"Pixel {first} differs");
    }
}
