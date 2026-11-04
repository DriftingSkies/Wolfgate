using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MameArcade;
using Content.Client._WF.MameArcade.Cps2;
using Content.Client._WF.MameArcade.NeoGeo;
using NUnit.Framework;

namespace Content.Tests._WF.MameArcade;

/// <summary>
/// CPS2 sets load the way MAME lays them out: the chip interleaves, the graphics unshuffle and pixel packing, the
/// key files and the decryption. Marvel vs. Capcom booting from a real set is what showed these right; these keep them so.
/// </summary>
[TestFixture]
[TestOf(typeof(Cps2Loader))]
public sealed class Cps2LoaderTest
{
    [Test]
    public void TableListsPlayableSets()
    {
        Assert.That(Cps2Sets.All, Has.Count.GreaterThan(150));
        Assert.That(Cps2Sets.Find("mvsc"), Is.Not.Null);
        Assert.That(Cps2Sets.Find("sfa3"), Is.Not.Null);

        foreach (var set in Cps2Sets.All)
        {
            Assert.That(set.KeyChip != null || set.KnownKey != null, $"{set.Name} has no way to get its key");
            if (set.Parent.Length > 0)
                Assert.That(Cps2Sets.Find(set.Parent), Is.Not.Null, $"{set.Name}'s parent {set.Parent} isn't listed");

            foreach (var region in set.Regions)
            {
                foreach (var load in region.Loads)
                {
                    var end = load.Mode switch
                    {
                        Cps2LoadMode.Word64 => load.RegionOffset + load.Length * 4 - 6,
                        Cps2LoadMode.Byte64 => load.RegionOffset + load.Length * 8 - 7,
                        Cps2LoadMode.Skip1 => load.RegionOffset + load.Length * 2 - 1,
                        _ => load.RegionOffset + load.Length,
                    };
                    Assert.That(end, Is.LessThanOrEqualTo(region.Size), $"{set.Name}: {load.Chip} runs past {region.Kind}");
                }
            }
        }

        Assert.That(MameGames.Find("mvsc")?.Board, Is.EqualTo(MameBoard.Cps2));
        Assert.That(MameGames.Find("kof94")?.Board, Is.EqualTo(MameBoard.NeoGeo));
    }

    [Test]
    public void ChipsInterleaveLikeMame()
    {
        var chip = new byte[] { 1, 2, 3, 4 };

        var words = new byte[16];
        Cps2Loader.Place(chip, new Cps2Load("g", 0, 0, 4, 2, Cps2LoadMode.Word64), words);
        Assert.That(words, Is.EqualTo(new byte[] { 0, 0, 1, 2, 0, 0, 0, 0, 0, 0, 3, 4, 0, 0, 0, 0 }), "Two bytes, then six skipped");

        var bytes = new byte[32];
        Cps2Loader.Place(chip, new Cps2Load("g", 0, 1, 2, 3, Cps2LoadMode.Byte64), bytes);
        Assert.That(bytes[3], Is.EqualTo(2));
        Assert.That(bytes[11], Is.EqualTo(3));

        var program = new byte[4];
        Cps2Loader.Place(chip, new Cps2Load("p", 0, 0, 4, 0, Cps2LoadMode.WordSwap), program);
        Assert.That(program, Is.EqualTo(new byte[] { 2, 1, 4, 3 }));
    }

    [Test]
    public void GraphicsUnshuffleAndPack()
    {
        // MAME's recursive unshuffle sends the even 64-bit groups to the front half and the odd ones to the back.
        Assert.That(Cps2Loader.UnshuffleOrder(8), Is.EqualTo(new[] { 0, 2, 4, 6, 1, 3, 5, 7 }));

        // Bitplane 0 of the first pixel and bitplane 3 of the eighth become their pens' bits.
        var gfx = new byte[] { 0x80, 0x00, 0x00, 0x01 };
        Cps2Loader.PackPixels(gfx);
        Assert.That(gfx, Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x80 }));
    }

    [Test]
    public void KeyFilesDecode()
    {
        var file = KeyFile(new ushort[] { 0x4802, 0x5ade, 0x1c69, 0x7b27, 0, 0, 0, 0x4000, 0x0900, 0x03c0 });
        var key = Cps2Crypt.FromKeyFile(file);
        Assert.That(key, Is.EqualTo(new Cps2Key(0x48025ade, 0x1c697b27, 0x100000)), "Marvel vs. Capcom's key, as MAME 0.150 listed it");

        Assert.That(Cps2Crypt.FromKeyFile(Enumerable.Repeat((byte) 0xff, 20).ToArray()), Is.Null, "A board whose battery died");
        Assert.That(Cps2Crypt.FromKeyFile(new byte[4]), Is.Null);
    }

    [Test]
    public void DecryptsOpcodes()
    {
        var rom = new byte[32];
        for (var a = 0; a < 16; a++)
        {
            var word = (ushort) (a * 0x1234 + 0x4e71);
            rom[a * 2] = (byte) (word >> 8);
            rom[a * 2 + 1] = (byte) word;
        }

        // From the port that decrypts Marvel vs. Capcom's real program.
        var expected = new ushort[]
        {
            0xe762, 0x6eb8, 0x315f, 0x2b4a, 0xa48a, 0xd855, 0x907f, 0x1744,
            0xdab3, 0x7a6b, 0xfa39, 0x45d6, 0x9ac4, 0x62a4, 0x6b32, 0xe496,
        };
        var dec = Cps2Crypt.Decrypt(rom, Cps2Sets.Find("mvsc")!.KnownKey!.Value);
        Assert.That(Enumerable.Range(0, 16).Select(a => (ushort) (dec[a * 2] << 8 | dec[a * 2 + 1])), Is.EqualTo(expected));

        var plain = Cps2Crypt.Decrypt(rom, new Cps2Key(0x48025ade, 0x1c697b27, 0x10));
        Assert.That(plain[0x14], Is.EqualTo(rom[0x14]), "Words past the encrypted range come out as they are");
        Assert.That(plain[0x10], Is.EqualTo(dec[0x10]), "The last encrypted word is still decrypted");
    }

    [Test]
    public void OlderDumpsAndTableKeysCount()
    {
        var set = Cps2Sets.Find("mvsc")!;
        var replaced = set.Regions.SelectMany(r => r.Loads).First(l => l.AltCrc != 0);
        var expected = new FakeChips(set.Chips.Values.Where(c => c.Crc != replaced.AltCrc && c.Crc != set.KeyChip!.Value.Crc));
        var older = new FakeChips(set.Chips.Values.Where(c => c.Crc != replaced.Crc && c.Crc != set.KeyChip!.Value.Crc));

        Assert.That(Cps2Loader.Missing(set, expected), Is.Empty, "No key file needed: MAME's old table has the key");
        Assert.That(Cps2Loader.Missing(set, older), Is.Empty, $"The older dump of {replaced.Chip} works too");
        Assert.That(Cps2Loader.Fingerprint(set, expected), Is.Not.EqualTo(Cps2Loader.Fingerprint(set, older)),
            "Players with different dumps can't play together");

        var none = new FakeChips(Array.Empty<MameChip>());
        Assert.That(Cps2Loader.Missing(set, none).Select(c => c.Name), Does.Contain(replaced.Chip));
        Assert.That(Cps2Loader.Fingerprint(set, none), Is.Empty);
    }

    /// <summary>The 20-byte layout MAME reads .key files in, holding the given ten words.</summary>
    private static byte[] KeyFile(ushort[] decoded)
    {
        var file = new byte[20];
        for (var b = 0; b < 160; b++)
        {
            if ((decoded[b / 16] & (0x8000 >> (b % 16))) == 0)
                continue;

            var bit = (317 - b) % 160;
            file[bit / 8] |= (byte) (1 << ((bit ^ 7) % 8));
        }

        return file;
    }

    private sealed class FakeChips : IMameChipSource
    {
        private readonly HashSet<(uint, int)> _chips;

        public FakeChips(IEnumerable<MameChip> chips)
        {
            _chips = chips.Select(c => (c.Crc, c.Size)).ToHashSet();
        }

        public bool Has(MameChip chip) => _chips.Contains((chip.Crc, chip.Size));

        public byte[] Read(MameChip chip) => new byte[chip.Size];
    }
}
