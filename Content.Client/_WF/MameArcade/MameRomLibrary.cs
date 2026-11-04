using System.IO;
using Content.Client._WF.MameArcade.Engine;
using Content.Client._WF.MameArcade.NeoGeo;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// The player's own MAME zips, kept in their user data. Chips are found by CRC and size across every zip, so split,
/// merged and non-merged sets all work, and the BIOS can sit in any of them.
/// </summary>
public sealed class MameRomLibrary
{
    public static readonly ResPath Root = new("/wolfgate_mame");
    public static readonly ResPath RomFolder = Root / "roms";
    private static readonly ResPath NvramFolder = Root / "nvram";
    private static readonly ResPath StateFolder = Root / "states";

    private readonly IWritableDirProvider _data;
    private Dictionary<(uint Crc, long Size), ChipLocation> _index = new();
    private List<(string Zip, List<(uint Crc, long Size)> Chips)> _zips = new();

    public MameRomLibrary(IWritableDirProvider data)
    {
        _data = data;
    }

    public int ZipCount => _zips.Count;

    /// <summary>Zips none of whose chips <paramref name="known"/> accepts, such as a game this cabinet can't run.</summary>
    public List<string> UnusedZips(Func<uint, long, bool> known)
    {
        var unused = new List<string>();
        foreach (var (zip, chips) in _zips)
        {
            if (!chips.Exists(chip => known(chip.Crc, chip.Size)))
                unused.Add(zip);
        }

        return unused;
    }

    /// <summary>Re-reads the zips' directories.</summary>
    public void Refresh()
    {
        var index = new Dictionary<(uint, long), ChipLocation>();
        var zips = new List<(string, List<(uint, long)>)>();
        _data.CreateDir(RomFolder);

        foreach (var entry in _data.DirectoryEntries(RomFolder))
        {
            if (!entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = RomFolder / entry;
            try
            {
                using var stream = _data.OpenRead(path);
                var chips = new List<(uint, long)>();
                foreach (var file in MameZip.ReadDirectory(stream))
                {
                    if (file.Size <= 0)
                        continue;

                    index.TryAdd((file.Crc, file.Size), new ChipLocation(path, file));
                    chips.Add((file.Crc, file.Size));
                }

                zips.Add((entry, chips));
            }
            catch (InvalidDataException)
            {
                // Not a zip; leave it for the player to remove.
            }
        }

        _index = index;
        _zips = zips;
    }

    /// <summary>A fixed view for the loader, unaffected by later refreshes.</summary>
    public IMameChipSource Snapshot() => new ChipSource(_data, _index);

    /// <summary>Copies a zip the player picked into the library.</summary>
    /// <returns>False if it isn't a zip.</returns>
    public bool Import(string fileName, Stream source)
    {
        var name = SafeName(fileName);
        var path = RomFolder / name;
        _data.CreateDir(RomFolder);

        using (var file = _data.OpenWrite(path))
        {
            source.CopyTo(file);
        }

        try
        {
            using var stream = _data.OpenRead(path);
            MameZip.ReadDirectory(stream);
        }
        catch (InvalidDataException)
        {
            _data.Delete(path);
            return false;
        }

        Refresh();
        return true;
    }

    public void OpenFolder() => _data.OpenOsWindow(RomFolder);

    public INvramStore Nvram() => new NvramStore(_data);

    public bool HasState(string game) => _data.Exists(StateFolder / $"{game}.sta");

    public bool TryLoadState(string game, out byte[] state)
    {
        var path = StateFolder / $"{game}.sta";
        state = _data.Exists(path) ? _data.ReadAllBytes(path) : Array.Empty<byte>();
        return state.Length > 0;
    }

    public void SaveState(string game, byte[] state)
    {
        _data.CreateDir(StateFolder);
        using var file = _data.OpenWrite(StateFolder / $"{game}.sta");
        file.Write(state, 0, state.Length);
    }

    /// <summary>A lowercase file name with only the characters MAME set names use.</summary>
    public static string SafeName(string fileName)
    {
        var start = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\')) + 1;
        var name = fileName.Substring(start).ToLowerInvariant();
        if (name.EndsWith(".zip"))
            name = name.Substring(0, name.Length - 4);

        var safe = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
                safe.Append(c);
        }

        return (safe.Length > 0 ? safe.ToString() : "roms") + ".zip";
    }

    private readonly record struct ChipLocation(ResPath Zip, MameZip.Entry Entry);

    private sealed class ChipSource : IMameChipSource
    {
        private readonly IWritableDirProvider _data;
        private readonly Dictionary<(uint, long), ChipLocation> _index;

        public ChipSource(IWritableDirProvider data, Dictionary<(uint, long), ChipLocation> index)
        {
            _data = data;
            _index = index;
        }

        public bool Has(MameChip chip) => _index.ContainsKey((chip.Crc, chip.Size));

        public byte[] Read(MameChip chip)
        {
            if (!_index.TryGetValue((chip.Crc, chip.Size), out var location))
                throw new InvalidOperationException($"Missing ROM {chip.Name}.");

            using var stream = _data.OpenRead(location.Zip);
            return MameZip.Read(stream, location.Entry);
        }
    }

    private sealed class NvramStore : INvramStore
    {
        private readonly IWritableDirProvider _data;

        public NvramStore(IWritableDirProvider data)
        {
            _data = data;
        }

        public byte[] Load(string game)
        {
            var path = NvramFolder / $"{game}.nv";
            return _data.Exists(path) ? _data.ReadAllBytes(path) : null!;
        }

        public void Save(string game, byte[] data)
        {
            _data.CreateDir(NvramFolder);
            using var file = _data.OpenWrite(NvramFolder / $"{game}.nv");
            file.Write(data, 0, data.Length);
        }
    }
}
