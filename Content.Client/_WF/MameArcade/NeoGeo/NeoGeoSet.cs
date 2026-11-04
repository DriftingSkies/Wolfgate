using System.Linq;

namespace Content.Client._WF.MameArcade.NeoGeo;

/// <summary>A memory region of a Neo Geo cartridge, in the order MAME loads them.</summary>
public enum NeoGeoRegionKind : byte
{
    MainCpu,
    Fixed,
    AudioCpu,
    Ymsnd,
    YmsndDeltaT,
    Sprites,
}

/// <summary>How a chip's bytes are laid into its region.</summary>
public enum NeoGeoLoadMode : byte
{
    /// <summary>Copied as dumped.</summary>
    Bytes,

    /// <summary>68000 program data: the two bytes of each word swap places.</summary>
    WordSwap,

    /// <summary>Every second byte, for interleaved sprite chips.</summary>
    Skip1,

    /// <summary>Every fourth byte.</summary>
    Skip3,

    /// <summary>A range copied from a region loaded earlier.</summary>
    Copy,
}

/// <summary>Part of a chip placed in a region, or a copy from another region.</summary>
public sealed record NeoGeoLoad(string Chip, uint Crc, int FileOffset, int Length, int RegionOffset, NeoGeoLoadMode Mode)
{
    public NeoGeoRegionKind CopySource { get; private init; }

    public static NeoGeoLoad Copy(NeoGeoRegionKind source, int sourceOffset, int length, int offset)
    {
        return new NeoGeoLoad(string.Empty, 0, sourceOffset, length, offset, NeoGeoLoadMode.Copy) { CopySource = source };
    }
}

public sealed record NeoGeoRegion(NeoGeoRegionKind Kind, int Size, NeoGeoLoad[] Loads);

/// <summary>A MAME ROM set and how its chips make up the cartridge.</summary>
public sealed record NeoGeoSet(string Name, string Parent, string Title, string Year, string Manufacturer, NeoGeoRegion[] Regions)
    : IMameGame
{
    private Dictionary<uint, MameChip>? _chips;

    public MameBoard Board => MameBoard.NeoGeo;

    /// <summary>Every chip the set needs, by CRC.</summary>
    public IReadOnlyDictionary<uint, MameChip> Chips => _chips ??= BuildChips();

    private Dictionary<uint, MameChip> BuildChips()
    {
        var chips = new Dictionary<uint, MameChip>();
        foreach (var load in Regions.SelectMany(r => r.Loads))
        {
            if (load.Mode == NeoGeoLoadMode.Copy)
                continue;

            var end = load.FileOffset + load.Length;
            if (!chips.TryGetValue(load.Crc, out var chip) || chip.Size < end)
                chips[load.Crc] = new MameChip(load.Chip, load.Crc, end);
        }

        return chips;
    }
}

/// <summary>One dumped ROM chip, found in a player's zip by its CRC and size.</summary>
public readonly record struct MameChip(string Name, uint Crc, int Size);

public static partial class NeoGeoSets
{
    public static readonly IReadOnlyList<NeoGeoSet> All = Build();

    private static readonly Dictionary<string, NeoGeoSet> ByName = All.ToDictionary(s => s.Name);

    public static NeoGeoSet? Find(string name) => ByName.GetValueOrDefault(name);
}
