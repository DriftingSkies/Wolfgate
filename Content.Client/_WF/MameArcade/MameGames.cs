using System.Linq;
using Content.Client._WF.MameArcade.Cps2;
using Content.Client._WF.MameArcade.NeoGeo;

namespace Content.Client._WF.MameArcade;

/// <summary>The arcade boards the cabinet emulates.</summary>
public enum MameBoard : byte
{
    NeoGeo,
    Cps2,
}

/// <summary>A MAME set this cabinet can run.</summary>
public interface IMameGame
{
    string Name { get; }

    /// <summary>The set this one is a version of, or empty.</summary>
    string Parent { get; }

    string Title { get; }
    string Year { get; }
    string Manufacturer { get; }
    MameBoard Board { get; }

    /// <summary>Every chip the set can use, by CRC.</summary>
    IReadOnlyDictionary<uint, MameChip> Chips { get; }
}

/// <summary>Every game of every board, and what each board needs to run one.</summary>
public static class MameGames
{
    private static readonly IMameGame[] AllGames = NeoGeoSets.All.Cast<IMameGame>().Concat(Cps2Sets.All).ToArray();

    // Chips some game or BIOS uses, to tell which zips hold nothing the cabinet runs.
    private static readonly HashSet<(uint, long)> KnownChips = BuildKnownChips();

    public static IReadOnlyList<IMameGame> All => AllGames;

    public static IMameGame? Find(string name) =>
        (IMameGame?) NeoGeoSets.Find(name) ?? Cps2Sets.Find(name);

    /// <summary>The chips the player still needs, BIOS or key included.</summary>
    public static List<MameChip> Missing(IMameGame game, IMameChipSource source) => game switch
    {
        NeoGeoSet neo => NeoGeoLoader.Missing(neo, source),
        Cps2Set cps2 => Cps2Loader.Missing(cps2, source),
        _ => new List<MameChip>(),
    };

    /// <summary>Equal on two clients only if they would run identical data; empty if the game can't run.</summary>
    public static string Fingerprint(IMameGame game, IMameChipSource source) => game switch
    {
        NeoGeoSet neo => NeoGeoLoader.Fingerprint(neo, source),
        Cps2Set cps2 => Cps2Loader.Fingerprint(cps2, source),
        _ => string.Empty,
    };

    /// <summary>True if the zips hold no Neo Geo system ROM, which every Neo Geo game needs.</summary>
    public static bool LacksNeoGeoBios(IMameChipSource source) => !NeoGeoLoader.MainBioses.Any(source.Has);

    public static bool IsKnownChip(uint crc, long size) => KnownChips.Contains((crc, size));

    private static HashSet<(uint, long)> BuildKnownChips()
    {
        var known = new HashSet<(uint, long)>();
        foreach (var chip in AllGames.SelectMany(g => g.Chips.Values))
        {
            known.Add((chip.Crc, chip.Size));
        }

        foreach (var bios in NeoGeoLoader.MainBioses.Append(NeoGeoLoader.FixedBios).Append(NeoGeoLoader.ZoomTable).Append(NeoGeoLoader.AudioBios))
        {
            known.Add((bios.Crc, bios.Size));
        }

        return known;
    }
}
