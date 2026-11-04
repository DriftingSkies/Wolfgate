using System.Linq;

namespace Content.Client._WF.MameArcade.NeoGeo;

/// <summary>Finds a player's dumped chips by CRC and size.</summary>
public interface IMameChipSource
{
    bool Has(MameChip chip);

    /// <summary>The whole chip, as dumped.</summary>
    byte[] Read(MameChip chip);
}

/// <summary>The region files MAME.NET's Neo Geo driver asks for, built from a player's MAME sets.</summary>
public sealed class NeoGeoImage
{
    public readonly Dictionary<string, byte[]> Regions = new();
    public readonly Dictionary<string, byte[]> Bios = new();

    /// <summary>Equal on two clients only if they would run identical data.</summary>
    public string Fingerprint = string.Empty;
}

/// <summary>Turns MAME Neo Geo sets into MAME.NET's region files, following the layouts in <see cref="NeoGeoSets"/>.</summary>
public static class NeoGeoLoader
{
    public static readonly MameChip FixedBios = new("sfix.sfix", 0xc2ea0cfd, 0x20000);
    public static readonly MameChip ZoomTable = new("000-lo.lo", 0x5a86cff2, 0x20000);
    public static readonly MameChip AudioBios = new("sm1.sm1", 0x94416d67, 0x20000);

    /// <summary>MVS system ROMs, in order of preference. Europe is MAME's default.</summary>
    public static readonly MameChip[] MainBioses =
    {
        new("sp-s2.sp1", 0x9036d879, 0x20000),
        new("sp-s.sp1", 0xc7f2fa45, 0x20000),
        new("sp-u2.sp1", 0xe72943de, 0x20000),
        new("sp-e.sp1", 0x2723a5b5, 0x20000),
        new("sp1-u2", 0x62f021f4, 0x20000),
        new("sp1-u4.bin", 0x1179a30f, 0x20000),
        new("sp1-u3.bin", 0x2025b7a2, 0x20000),
        new("vs-bios.rom", 0xf0e8f27d, 0x20000),
        new("sp-j2.sp1", 0xacede59c, 0x20000),
        new("sp1.jipan.1024", 0x9fb0abe4, 0x20000),
        new("japan-j3.bin", 0xdff6d41f, 0x20000),
        new("sp1-j3.bin", 0xfbc6d469, 0x20000),
        new("sp-s3.sp1", 0x91b64be3, 0x20000),
    };

    private static readonly NeoGeoRegionKind[] RegionOrder =
    {
        NeoGeoRegionKind.MainCpu,
        NeoGeoRegionKind.Fixed,
        NeoGeoRegionKind.AudioCpu,
        NeoGeoRegionKind.Ymsnd,
        NeoGeoRegionKind.YmsndDeltaT,
        NeoGeoRegionKind.Sprites,
    };

    public static string RegionFile(NeoGeoRegionKind kind) => kind switch
    {
        NeoGeoRegionKind.MainCpu => "maincpu.rom",
        NeoGeoRegionKind.Fixed => "fixed.rom",
        NeoGeoRegionKind.AudioCpu => "audiocpu.rom",
        NeoGeoRegionKind.Ymsnd => "ymsnd.rom",
        NeoGeoRegionKind.YmsndDeltaT => "ymsnddeltat.rom",
        _ => "sprites.rom",
    };

    /// <summary>The chips the player still needs, BIOS included.</summary>
    public static List<MameChip> Missing(NeoGeoSet set, IMameChipSource source)
    {
        var missing = set.Chips.Values.Where(c => !source.Has(c)).ToList();
        foreach (var bios in new[] { FixedBios, ZoomTable, AudioBios })
        {
            if (!source.Has(bios))
                missing.Add(bios);
        }

        if (!MainBioses.Any(source.Has))
            missing.Add(MainBioses[0]);

        return missing;
    }

    /// <summary>What two players' copies must agree on: the set, and the system ROM it boots with.</summary>
    public static string Fingerprint(NeoGeoSet set, IMameChipSource source)
    {
        var bios = MainBioses.FirstOrDefault(source.Has);
        return bios.Size == 0 ? string.Empty : $"{set.Name}:{bios.Crc:x8}";
    }

    public static NeoGeoImage Load(NeoGeoSet set, IMameChipSource source)
    {
        var image = new NeoGeoImage();
        var chips = new Dictionary<uint, byte[]>();
        var regions = new Dictionary<NeoGeoRegionKind, byte[]>();

        foreach (var kind in RegionOrder)
        {
            if (set.Regions.FirstOrDefault(r => r.Kind == kind) is not { } region)
                continue;

            regions[kind] = Assemble(set, region, source, chips, regions);
        }

        foreach (var (kind, data) in regions)
        {
            image.Regions[RegionFile(kind)] = kind == NeoGeoRegionKind.Sprites ? OptimizeSprites(data) : data;
        }

        var mainBios = MainBioses.FirstOrDefault(source.Has);
        if (mainBios.Size == 0)
            throw new InvalidOperationException($"No Neo Geo system ROM for {set.Name}.");

        image.Bios["mainbios.rom"] = SwapWords(source.Read(mainBios));
        image.Bios["sfix.sfix"] = source.Read(FixedBios);
        image.Bios["000-lo.lo"] = source.Read(ZoomTable);
        image.Bios["sm1.sm1"] = source.Read(AudioBios);
        image.Fingerprint = Fingerprint(set, source);
        return image;
    }

    private static byte[] Assemble(
        NeoGeoSet set,
        NeoGeoRegion region,
        IMameChipSource source,
        Dictionary<uint, byte[]> chips,
        Dictionary<NeoGeoRegionKind, byte[]> regions)
    {
        var data = new byte[region.Size];
        foreach (var load in region.Loads)
        {
            if (load.Mode == NeoGeoLoadMode.Copy)
            {
                Array.Copy(regions[load.CopySource], load.FileOffset, data, load.RegionOffset, load.Length);
                continue;
            }

            if (!chips.TryGetValue(load.Crc, out var chip))
            {
                chip = source.Read(set.Chips[load.Crc]);
                chips[load.Crc] = chip;
            }

            Place(chip, load, data);
        }

        return data;
    }

    /// <summary>Lays part of a chip into a region the way the matching MAME ROM_LOAD macro does.</summary>
    public static void Place(byte[] chip, NeoGeoLoad load, byte[] region)
    {
        var from = load.FileOffset;
        var to = load.RegionOffset;
        var length = load.Length;

        switch (load.Mode)
        {
            case NeoGeoLoadMode.Bytes:
                Array.Copy(chip, from, region, to, length);
                break;
            case NeoGeoLoadMode.WordSwap:
                for (var i = 0; i < length; i += 2)
                {
                    region[to + i] = chip[from + i + 1];
                    region[to + i + 1] = chip[from + i];
                }
                break;
            case NeoGeoLoadMode.Skip1:
                for (var i = 0; i < length; i++)
                {
                    region[to + i * 2] = chip[from + i];
                }
                break;
            case NeoGeoLoadMode.Skip3:
                for (var i = 0; i < length; i++)
                {
                    region[to + i * 4] = chip[from + i];
                }
                break;
        }
    }

    /// <summary>
    /// MAME's neosprite_optimized_device::optimize_helper, one pen per pixel, then packed two pixels a byte with the
    /// first in the high nibble, which is the sprites.rom MAME.NET unpacks.
    /// </summary>
    public static byte[] OptimizeSprites(byte[] region)
    {
        var pixels = RegionMask(region.Length) + 1;
        var packed = new byte[pixels / 2];
        var dest = 0L;

        for (var block = 0; block + 0x80 <= region.Length; block += 0x80)
        {
            for (var y = 0; y < 0x10; y++)
            {
                var row = block + (y << 2);
                PackRow(region[row + 0x43], region[row + 0x41], region[row + 0x42], region[row + 0x40], packed, ref dest);
                PackRow(region[row + 0x03], region[row + 0x01], region[row + 0x02], region[row + 0x00], packed, ref dest);
            }
        }

        return packed;
    }

    /// <summary>MAME's get_region_mask: twice the length, rounded up to a power of two, minus one.</summary>
    public static long RegionMask(int length)
    {
        var mask = 0xffffffffu;
        var span = (uint) length * 2 - 1;
        for (var bit = 0x80000000u; bit != 0; bit >>= 1)
        {
            if ((span & bit) != 0)
                break;

            mask >>= 1;
        }

        return mask;
    }

    public static byte[] SwapWords(byte[] data)
    {
        var swapped = new byte[data.Length];
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            swapped[i] = data[i + 1];
            swapped[i + 1] = data[i];
        }

        return swapped;
    }

    private static void PackRow(byte plane3, byte plane2, byte plane1, byte plane0, byte[] packed, ref long dest)
    {
        for (var x = 0; x < 8; x++)
        {
            var pen = (((plane3 >> x) & 1) << 3) | (((plane2 >> x) & 1) << 2) | (((plane1 >> x) & 1) << 1) | ((plane0 >> x) & 1);
            var index = dest >> 1;
            packed[index] = (dest & 1) == 0 ? (byte) (pen << 4) : (byte) (packed[index] | pen);
            dest++;
        }
    }
}
