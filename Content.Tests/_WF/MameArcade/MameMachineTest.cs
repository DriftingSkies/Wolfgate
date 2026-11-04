using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MameArcade;
using Content.Client._WF.MameArcade.NeoGeo;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.MameArcade;

/// <summary>
/// The ported emulator boots a Neo Geo and runs frames. There are no free Neo Geo ROMs, so the system ROM is a few
/// hand-assembled 68000 instructions that paint the backdrop blue and keep the watchdog fed.
/// </summary>
[TestFixture]
[TestOf(typeof(MameMachine))]
public sealed class MameMachineTest
{
    private static readonly byte[] Program =
    {
        0x33, 0xfc, 0x00, 0x0f, 0x00, 0x40, 0x1f, 0xfe, // move.w #$000f,$401ffe   backdrop: palette entry $fff
        0x13, 0xc0, 0x00, 0x30, 0x00, 0x01,             // loop: move.b d0,$300001   watchdog
        0x52, 0x79, 0x00, 0x10, 0x00, 0x00,             // addq.w #1,$100000          work RAM counter
        0x60, 0xf2,                                     // bra.s loop
    };

    [Test]
    public void BootsAndDrawsFrames()
    {
        var set = NeoGeoSets.Find("nam1975")!;
        var image = NeoGeoLoader.Load(set, new BlankCartridge(SystemRom()));
        var samples = 0;

        try
        {
            MameMachine.StartNeoGeo(set, image, null, (_, frames) => samples += frames);
            for (var i = 0; i < 30; i++)
            {
                MameMachine.RunFrame(MameControls.None, MameControls.None);
            }

            var pixels = new Rgba32[MameMachine.Width * MameMachine.Height];
            MameMachine.CopyFrame(pixels);

            Assert.That(MameMachine.Width, Is.EqualTo(320));
            Assert.That(MameMachine.Height, Is.EqualTo(224));
            Assert.That(MameMachine.FrameSeconds, Is.EqualTo(1 / 59.1856).Within(0.0001));
            Assert.That(pixels.Distinct().Count(), Is.EqualTo(1), "Only the backdrop is drawn");
            Assert.That(pixels[0].B, Is.GreaterThan(0x80), "The program set the backdrop to blue");
            Assert.That(pixels[0].R, Is.LessThan(0x20));
            Assert.That(samples, Is.GreaterThan(MameArcadeAudio.SampleRate / 4), "The sound chips produced audio");

            var saved = MameMachine.Checksum();
            var state = MameMachine.SaveState();
            var ahead = Run(3);
            Assert.That(ahead, Is.Not.EqualTo(saved), "The program changes work RAM every frame");

            MameMachine.LoadState(state);
            Assert.That(MameMachine.Checksum(), Is.EqualTo(saved), "Loading a state restores work RAM");
            Assert.That(Run(3), Is.EqualTo(ahead), "A loaded state replays the same frames");
        }
        finally
        {
            MameMachine.Stop();
        }
    }

    [Test]
    public void TwoBootsMatchFrameForFrame()
    {
        // A 2-player game relies on both clients' emulators agreeing from power-on.
        var set = NeoGeoSets.Find("nam1975")!;
        var checksums = new List<uint>();

        for (var boot = 0; boot < 2; boot++)
        {
            try
            {
                MameMachine.StartNeoGeo(set, NeoGeoLoader.Load(set, new BlankCartridge(SystemRom())), null, null);
                checksums.Add(Run(45));
            }
            finally
            {
                MameMachine.Stop();
            }
        }

        Assert.That(checksums[1], Is.EqualTo(checksums[0]));
    }

    private static uint Run(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            MameMachine.RunFrame(MameControls.None, MameControls.None);
        }

        return MameMachine.Checksum();
    }

    /// <summary>A 128K system ROM as dumped: the reset vectors, then the program at $c00402, with words byte-swapped.</summary>
    private static byte[] SystemRom()
    {
        var rom = new byte[0x20000];
        new byte[] { 0x00, 0x10, 0xf3, 0x00, 0x00, 0xc0, 0x04, 0x02 }.CopyTo(rom, 0);
        Program.CopyTo(rom, 0x402);
        return NeoGeoLoader.SwapWords(rom);
    }

    /// <summary>Every game chip blank, and the hand-made system ROM in place of the Europe BIOS.</summary>
    private sealed class BlankCartridge : IMameChipSource
    {
        private readonly byte[] _systemRom;

        public BlankCartridge(byte[] systemRom)
        {
            _systemRom = systemRom;
        }

        public bool Has(MameChip chip) => true;

        public byte[] Read(MameChip chip) => chip == NeoGeoLoader.MainBioses[0] ? _systemRom : new byte[chip.Size];
    }
}
