using System.Linq;
using Content.Client._WF.MameArcade.NeoGeo;

namespace Content.Client._WF.MameArcade.Cps2;

/// <summary>A memory region of a CPS2 board.</summary>
public enum Cps2RegionKind : byte
{
    MainCpu,
    Gfx,
    AudioCpu,
    QSound,
}

/// <summary>How a chip's bytes are laid into its region, as MAME's ROM_LOAD macros do.</summary>
public enum Cps2LoadMode : byte
{
    /// <summary>Copied as dumped.</summary>
    Bytes,

    /// <summary>The two bytes of each word swap places.</summary>
    WordSwap,

    /// <summary>Every second byte.</summary>
    Skip1,

    /// <summary>Two bytes, then six skipped: four chips interleave into 64-bit groups.</summary>
    Word64,

    /// <summary>One byte, then seven skipped.</summary>
    Byte64,

    /// <summary>The range is filled with the value in <see cref="Cps2Load.Crc"/>.</summary>
    Fill,
}

/// <summary>Part of a chip placed in a region. <paramref name="AltCrc"/> is an older dump MAME also accepted.</summary>
public sealed record Cps2Load(string Chip, uint Crc, int FileOffset, int Length, int RegionOffset, Cps2LoadMode Mode, uint AltCrc = 0);

public sealed record Cps2Region(Cps2RegionKind Kind, int Size, Cps2Load[] Loads);

/// <summary>A decryption key: the 64-bit master key and the end of the encrypted range in bytes.</summary>
public readonly record struct Cps2Key(uint Key0, uint Key1, int UpperLimit);

/// <summary>
/// A MAME CPS2 set. The key comes from the set's .key file if the player's zip has it, or else from
/// <paramref name="KnownKey"/>, for zips made before MAME shipped key files.
/// </summary>
public sealed record Cps2Set(
    string Name,
    string Parent,
    string Title,
    string Year,
    string Manufacturer,
    MameChip? KeyChip,
    Cps2Key? KnownKey,
    Cps2Region[] Regions) : IMameGame
{
    private Dictionary<uint, MameChip>? _chips;

    public MameBoard Board => MameBoard.Cps2;

    /// <summary>Every chip the set can use, by CRC: the expected dumps, older dumps and the key file.</summary>
    public IReadOnlyDictionary<uint, MameChip> Chips => _chips ??= BuildChips();

    private Dictionary<uint, MameChip> BuildChips()
    {
        var chips = new Dictionary<uint, MameChip>();
        foreach (var load in Regions.SelectMany(r => r.Loads))
        {
            if (load.Mode == Cps2LoadMode.Fill)
                continue;

            var size = Regions.SelectMany(r => r.Loads)
                .Where(l => l.Crc == load.Crc && l.Mode != Cps2LoadMode.Fill)
                .Max(l => l.FileOffset + l.Length);
            chips.TryAdd(load.Crc, new MameChip(load.Chip, load.Crc, size));
            if (load.AltCrc != 0)
                chips.TryAdd(load.AltCrc, new MameChip(load.Chip, load.AltCrc, size));
        }

        if (KeyChip is { } key)
            chips.TryAdd(key.Crc, key);

        return chips;
    }
}

/// <summary>The CPS2 sets this cabinet runs.</summary>
public static partial class Cps2Sets
{
    public static readonly IReadOnlyList<Cps2Set> All = Build();

    private static readonly Dictionary<string, Cps2Set> ByName = All.ToDictionary(s => s.Name);

    public static Cps2Set? Find(string name) => ByName.GetValueOrDefault(name);
}
