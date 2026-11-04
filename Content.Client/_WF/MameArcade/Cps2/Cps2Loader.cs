using System.Linq;
using Content.Client._WF.MameArcade.NeoGeo;

namespace Content.Client._WF.MameArcade.Cps2;

/// <summary>
/// Builds the region files MAME.NET's CPS2 driver reads from a player's MAME set: the program and its decrypted
/// opcodes, the graphics unshuffled and decoded the way MAME's cps2_gfx_decode leaves them, and the sound.
/// </summary>
public static class Cps2Loader
{
    // MAME unshuffles the graphics in banks of this size.
    private const int GfxBank = 0x200000;

    private static readonly int[] BankOrder = UnshuffleOrder(GfxBank / 8);
    private static readonly uint[][] PlaneSpread = BuildPlaneSpread();

    public static string RegionFile(Cps2RegionKind kind) => kind switch
    {
        Cps2RegionKind.MainCpu => "maincpu.rom",
        Cps2RegionKind.Gfx => "gfx.rom",
        Cps2RegionKind.AudioCpu => "audiocpu.rom",
        _ => "qsound.rom",
    };

    /// <summary>The chips the player still needs: each one in a dump MAME accepts, and the key if no table has it.</summary>
    public static List<MameChip> Missing(Cps2Set set, IMameChipSource source)
    {
        var missing = new List<MameChip>();
        var seen = new HashSet<uint>();
        foreach (var load in set.Regions.SelectMany(r => r.Loads))
        {
            if (load.Mode == Cps2LoadMode.Fill || !seen.Add(load.Crc) || Pick(set, load, source) != null)
                continue;

            missing.Add(set.Chips[load.Crc]);
        }

        if (set.KnownKey == null && set.KeyChip is { } key && !source.Has(key))
            missing.Add(key);

        return missing;
    }

    /// <summary>What two players' copies must agree on: the set and which dump of each chip it uses.</summary>
    public static string Fingerprint(Cps2Set set, IMameChipSource source)
    {
        if (Missing(set, source).Count > 0)
            return string.Empty;

        var hash = 2166136261u;
        foreach (var load in set.Regions.SelectMany(r => r.Loads))
        {
            if (load.Mode == Cps2LoadMode.Fill)
                continue;

            hash = (hash ^ Pick(set, load, source)!.Value.Crc) * 16777619u;
        }

        return $"{set.Name}:{hash:x8}";
    }

    public static Dictionary<string, byte[]> Load(Cps2Set set, IMameChipSource source)
    {
        var chips = new Dictionary<uint, byte[]>();
        var files = new Dictionary<string, byte[]>();

        foreach (var region in set.Regions)
        {
            var data = new byte[region.Size];
            foreach (var load in region.Loads)
            {
                if (load.Mode == Cps2LoadMode.Fill)
                {
                    Array.Fill(data, (byte) load.Crc, load.RegionOffset, load.Length);
                    continue;
                }

                var chip = Pick(set, load, source) ?? throw new InvalidOperationException($"Missing ROM {load.Chip}.");
                if (!chips.TryGetValue(chip.Crc, out var bytes))
                {
                    bytes = source.Read(chip);
                    chips[chip.Crc] = bytes;
                }

                Place(bytes, load, data);
            }

            if (region.Kind == Cps2RegionKind.Gfx)
                DecodeGfx(data);

            files[RegionFile(region.Kind)] = data;
        }

        var key = KeyFor(set, source) ?? throw new InvalidOperationException($"No decryption key for {set.Name}.");
        files["maincpuop.rom"] = Cps2Crypt.Decrypt(files[RegionFile(Cps2RegionKind.MainCpu)], key);
        return files;
    }

    /// <summary>The key from the set's .key file if the zip has one, or else the one MAME used to keep in a table.</summary>
    public static Cps2Key? KeyFor(Cps2Set set, IMameChipSource source)
    {
        if (set.KeyChip is { } chip && source.Has(chip) && Cps2Crypt.FromKeyFile(source.Read(chip)) is { } key)
            return key;

        return set.KnownKey;
    }

    /// <summary>Lays part of a chip into a region the way the matching MAME ROM_LOAD macro does.</summary>
    public static void Place(byte[] chip, Cps2Load load, byte[] region)
    {
        var from = load.FileOffset;
        var to = load.RegionOffset;
        var length = load.Length;

        switch (load.Mode)
        {
            case Cps2LoadMode.Bytes:
                Array.Copy(chip, from, region, to, length);
                break;
            case Cps2LoadMode.WordSwap:
                for (var i = 0; i < length; i += 2)
                {
                    region[to + i] = chip[from + i + 1];
                    region[to + i + 1] = chip[from + i];
                }
                break;
            case Cps2LoadMode.Skip1:
                for (var i = 0; i < length; i++)
                {
                    region[to + i * 2] = chip[from + i];
                }
                break;
            case Cps2LoadMode.Word64:
                for (var i = 0; i < length; i += 2)
                {
                    region[to + i * 4] = chip[from + i];
                    region[to + i * 4 + 1] = chip[from + i + 1];
                }
                break;
            case Cps2LoadMode.Byte64:
                for (var i = 0; i < length; i++)
                {
                    region[to + i * 8] = chip[from + i];
                }
                break;
        }
    }

    /// <summary>MAME's cps2_gfx_decode: unshuffle each bank's 64-bit groups, then cps1_gfx_decode's pixel packing.</summary>
    public static void DecodeGfx(byte[] gfx)
    {
        var order = BankOrder;
        var bank = new byte[GfxBank];
        for (var start = 0; start + GfxBank <= gfx.Length; start += GfxBank)
        {
            Array.Copy(gfx, start, bank, 0, GfxBank);
            for (var i = 0; i < order.Length; i++)
            {
                Array.Copy(bank, order[i] * 8, gfx, start + i * 8, 8);
            }
        }

        PackPixels(gfx);
    }

    /// <summary>
    /// MAME's cps1_gfx_decode: each 32-bit group holds one bitplane of eight pixels per byte, and becomes the eight
    /// pixels' 4-bit pens, the first in the lowest nibble.
    /// </summary>
    public static void PackPixels(byte[] gfx)
    {
        var spread = PlaneSpread;
        for (var i = 0; i + 3 < gfx.Length; i += 4)
        {
            var value = spread[0][gfx[i]] | spread[1][gfx[i + 1]] | spread[2][gfx[i + 2]] | spread[3][gfx[i + 3]];
            gfx[i] = (byte) value;
            gfx[i + 1] = (byte) (value >> 8);
            gfx[i + 2] = (byte) (value >> 16);
            gfx[i + 3] = (byte) (value >> 24);
        }
    }

    /// <summary>
    /// Where each 64-bit group of a bank comes from after MAME's unshuffle, which recursively unshuffles both halves
    /// and then swaps their inner quarters.
    /// </summary>
    public static int[] UnshuffleOrder(int length)
    {
        var order = new int[length];
        for (var i = 0; i < length; i++)
        {
            order[i] = i;
        }

        Unshuffle(order, 0, length);
        return order;
    }

    private static void Unshuffle(int[] buf, int start, int length)
    {
        if (length == 2)
            return;

        length /= 2;
        Unshuffle(buf, start, length);
        Unshuffle(buf, start + length, length);
        for (var i = 0; i < length / 2; i++)
        {
            (buf[start + length / 2 + i], buf[start + length + i]) = (buf[start + length + i], buf[start + length / 2 + i]);
        }
    }

    private static uint[][] BuildPlaneSpread()
    {
        var spread = new uint[4][];
        for (var plane = 0; plane < 4; plane++)
        {
            spread[plane] = new uint[256];
            for (var value = 0; value < 256; value++)
            {
                for (var j = 0; j < 8; j++)
                {
                    if ((value & (0x80 >> j)) != 0)
                        spread[plane][value] |= 1u << (j * 4 + plane);
                }
            }
        }

        return spread;
    }

    /// <summary>The chip the player has for a load: the dump MAME expects, or an older one it also accepted.</summary>
    private static MameChip? Pick(Cps2Set set, Cps2Load load, IMameChipSource source)
    {
        var chip = set.Chips[load.Crc];
        if (source.Has(chip))
            return chip;

        if (load.AltCrc != 0 && set.Chips[load.AltCrc] is var alt && source.Has(alt))
            return alt;

        return null;
    }
}
