using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MameArcade.NeoGeo;
using NUnit.Framework;

namespace Content.Tests._WF.MameArcade;

/// <summary>The Neo Geo ROM table is self-consistent and the loader lays chips out the way MAME's macros do.</summary>
[TestFixture]
[TestOf(typeof(NeoGeoLoader))]
public sealed class NeoGeoLoaderTest
{
    [Test]
    public void EverySetFitsItsRegions()
    {
        Assert.That(NeoGeoSets.All, Has.Count.GreaterThan(100));
        Assert.That(NeoGeoSets.Find("kof94"), Is.Not.Null);
        Assert.That(NeoGeoSets.Find("mslug"), Is.Not.Null);

        foreach (var set in NeoGeoSets.All)
        {
            var kinds = set.Regions.Select(r => r.Kind).ToHashSet();
            foreach (var needed in new[] { NeoGeoRegionKind.MainCpu, NeoGeoRegionKind.Fixed, NeoGeoRegionKind.AudioCpu, NeoGeoRegionKind.Ymsnd, NeoGeoRegionKind.Sprites })
            {
                Assert.That(kinds, Does.Contain(needed), $"{set.Name} has no {needed} region");
            }

            if (set.Parent.Length > 0)
                Assert.That(NeoGeoSets.Find(set.Parent), Is.Not.Null, $"{set.Name}'s parent {set.Parent} isn't listed");

            foreach (var region in set.Regions)
            {
                foreach (var load in region.Loads)
                {
                    var stride = load.Mode switch
                    {
                        NeoGeoLoadMode.Skip1 => 2,
                        NeoGeoLoadMode.Skip3 => 4,
                        _ => 1,
                    };

                    var last = load.RegionOffset + (load.Length - 1) * stride;
                    Assert.That(last, Is.LessThan(region.Size), $"{set.Name}: {load.Chip} runs past {region.Kind}");
                    Assert.That(load.Length, Is.GreaterThan(0), $"{set.Name}: {load.Chip} is empty");
                    if (load.Mode == NeoGeoLoadMode.WordSwap)
                        Assert.That(load.Length % 2, Is.Zero, $"{set.Name}: {load.Chip} is an odd length");
                }
            }
        }
    }

    [Test]
    public void ContinuedChipKeepsItsWholeSize()
    {
        // KOF '94 stores the two halves of its program ROM swapped: MAME loads it with ROM_CONTINUE.
        var set = NeoGeoSets.Find("kof94")!;
        var program = set.Regions.Single(r => r.Kind == NeoGeoRegionKind.MainCpu);
        Assert.That(program.Loads, Has.Length.EqualTo(2));
        Assert.That(program.Loads[0].RegionOffset, Is.EqualTo(0x100000));
        Assert.That(program.Loads[1].FileOffset, Is.EqualTo(0x100000));
        Assert.That(set.Chips[program.Loads[0].Crc].Size, Is.EqualTo(0x200000));
    }

    [Test]
    public void LoadModesMatchMame()
    {
        var chip = new byte[] { 0x10, 0x11, 0x12, 0x13 };
        var region = new byte[8];

        NeoGeoLoader.Place(chip, new NeoGeoLoad("p", 0, 0, 4, 0, NeoGeoLoadMode.WordSwap), region);
        Assert.That(region.Take(4), Is.EqualTo(new byte[] { 0x11, 0x10, 0x13, 0x12 }));

        region = new byte[8];
        NeoGeoLoader.Place(chip, new NeoGeoLoad("c", 0, 0, 4, 1, NeoGeoLoadMode.Skip1), region);
        Assert.That(region, Is.EqualTo(new byte[] { 0, 0x10, 0, 0x11, 0, 0x12, 0, 0x13 }));

        region = new byte[16];
        NeoGeoLoader.Place(chip, new NeoGeoLoad("c", 0, 2, 2, 3, NeoGeoLoadMode.Skip3), region);
        Assert.That(region[3], Is.EqualTo(0x12));
        Assert.That(region[7], Is.EqualTo(0x13));
    }

    [Test]
    public void SpritesUseMameBitplaneOrder()
    {
        var region = new byte[0x80];
        region[0x40] = 0x01; // plane 0 of row 0's left half, pixel 0
        region[0x43] = 0x80; // plane 3 of row 0's left half, pixel 7
        region[0x02] = 0x01; // plane 1 of row 0's right half, pixel 8

        var packed = NeoGeoLoader.OptimizeSprites(region);

        Assert.That(packed, Has.Length.EqualTo(0x80));
        Assert.That(packed[0] >> 4, Is.EqualTo(0x1), "pixel 0");
        Assert.That(packed[3] & 0xf, Is.EqualTo(0x8), "pixel 7");
        Assert.That(packed[4] >> 4, Is.EqualTo(0x2), "pixel 8");
        Assert.That(packed.Skip(5).All(b => b == 0), "Only three pixels were set");
    }

    [TestCase(0x80, 0xffL)]
    [TestCase(0x300000, 0x7fffffL)]
    [TestCase(0x4000000, 0x7ffffffL)]
    public void RegionMaskRoundsUp(int length, long mask)
    {
        Assert.That(NeoGeoLoader.RegionMask(length), Is.EqualTo(mask));
    }

    [Test]
    public void LoadsASetFromAnyZips()
    {
        var set = NeoGeoSets.Find("nam1975")!;
        var source = new FakeChips(set.Chips.Values.Concat(new[]
        {
            NeoGeoLoader.FixedBios, NeoGeoLoader.ZoomTable, NeoGeoLoader.AudioBios, NeoGeoLoader.MainBioses[2],
        }));

        Assert.That(NeoGeoLoader.Missing(set, source), Is.Empty);
        var image = NeoGeoLoader.Load(set, source);

        var program = set.Regions.Single(r => r.Kind == NeoGeoRegionKind.MainCpu);
        var chip = source.Read(set.Chips[program.Loads[0].Crc]);
        Assert.That(image.Regions["maincpu.rom"], Has.Length.EqualTo(program.Size));
        Assert.That(image.Regions["maincpu.rom"][0], Is.EqualTo(chip[1]), "68000 words are byte-swapped");
        Assert.That(image.Regions.ContainsKey("ymsnddeltat.rom"), "NAM-1975 has separate ADPCM-B ROMs");
        Assert.That(image.Bios["mainbios.rom"][0], Is.EqualTo(source.Read(NeoGeoLoader.MainBioses[2])[1]));
        Assert.That(image.Fingerprint, Is.EqualTo($"nam1975:{NeoGeoLoader.MainBioses[2].Crc:x8}"));
    }

    [Test]
    public void ReportsMissingBios()
    {
        var set = NeoGeoSets.Find("nam1975")!;
        var missing = NeoGeoLoader.Missing(set, new FakeChips(set.Chips.Values));

        Assert.That(missing.Select(m => m.Name), Does.Contain("sfix.sfix"));
        Assert.That(missing.Select(m => m.Name), Does.Contain("sp-s2.sp1"));
        Assert.That(NeoGeoLoader.Fingerprint(set, new FakeChips(set.Chips.Values)), Is.Empty);
    }

    /// <summary>Chips filled with bytes derived from their CRC, so different chips differ.</summary>
    private sealed class FakeChips : IMameChipSource
    {
        private readonly HashSet<(uint, int)> _chips;

        public FakeChips(IEnumerable<MameChip> chips)
        {
            _chips = chips.Select(c => (c.Crc, c.Size)).ToHashSet();
        }

        public bool Has(MameChip chip) => _chips.Contains((chip.Crc, chip.Size));

        public byte[] Read(MameChip chip)
        {
            if (!Has(chip))
                throw new InvalidOperationException(chip.Name);

            var data = new byte[chip.Size];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte) (chip.Crc + i * 7);
            }

            return data;
        }
    }
}
