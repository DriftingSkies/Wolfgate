using System.Globalization;
using System.Linq;
using System.Text;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Server._WF.MappingMode;

/// <summary>What the vessel generator learned from a grid.</summary>
public sealed class WFMappingVesselScan
{
    public int Tiles;

    /// <summary>The grid's <c>PricingSystem.AppraiseGrid</c> value.</summary>
    public double Appraisal;

    public bool CargoPallet;
    public bool Mining;

    /// <summary>An anchored gun stands on the grid: a turret or ship weapon.</summary>
    public bool MountedGuns;

    public readonly HashSet<VesselEngine> Engines = new();
}

/// <summary>The localized text the generated file carries: header comment lines and the placeholder description.</summary>
public sealed record WFMappingVesselText(IReadOnlyList<string> Header, string Description);

/// <summary>
/// Writes a <c>vessel</c> prototype, and the <c>gameMap</c> that gives a bought ship its station, for a grid saved
/// in mapping mode. Pure: the scan and the text come from <see cref="WFMappingVesselSystem"/>.
/// </summary>
public static class WFMappingVesselYaml
{
    public const string IdPrefix = "WF";

    /// <summary>The resource folder the grid file goes in, as <c>shuttlePath</c> names it.</summary>
    public const string ShuttleFolder = "/SharedMaps/_WF/Community";

    /// <summary>The repository folders the two files go in.</summary>
    public const string GridRepoFolder = "Resources/SharedMaps/_WF/Community";
    public const string PrototypeRepoFolder = "Resources/Prototypes/_WF/Community";

    /// <summary>Tile counts below which a ship is Micro, Small and Medium; anything bigger is Large.</summary>
    public const int MicroTiles = 50;
    public const int SmallTiles = 200;
    public const int MediumTiles = 500;

    /// <summary>The appraisal is raised by this before rounding, so the price stays over a sale of the ship.</summary>
    public const double PriceMarkup = 1.25;

    public const int MinPrice = 1000;

    /// <summary>Prototype id parts that mark mining or salvage gear, matched against an entity and its parents.</summary>
    private static readonly string[] MiningParts =
    {
        "OreProcessor", "OreBox", "OreMagnet", "MagnetBoxOre", "SalvageMagnet", "Pickaxe",
    };

    /// <summary>Prototype ids that are mining gear only when matched whole.</summary>
    private static readonly string[] MiningIds = { "MiningDrill" };

    /// <summary>Prototype id parts that give an engine type, most specific first.</summary>
    private static readonly (string Part, VesselEngine Engine)[] EngineParts =
    {
        ("AmeController", VesselEngine.AME),
        ("SuperPacman", VesselEngine.Uranium),
        ("DKJr", VesselEngine.Bananium),
        ("JrPacman", VesselEngine.Welding),
        ("Pacman", VesselEngine.Plasma),
        ("Pinch", VesselEngine.Plasma),
        ("NuclearReactor", VesselEngine.NFR),
        ("GeneratorRTG", VesselEngine.RTG),
        ("APU", VesselEngine.APU),
        ("SolarPanel", VesselEngine.Solar),
        ("TegCenter", VesselEngine.TEG),
        ("Supermatter", VesselEngine.Supermatter),
        ("TeslaGenerator", VesselEngine.Tesla),
        ("SingularityGenerator", VesselEngine.Singularity),
    };

    /// <summary><c>WF</c> and the name's words in PascalCase: "my cool-ship 2" gives <c>WFMyCoolShip2</c>.</summary>
    public static string VesselId(string mapName)
    {
        var builder = new StringBuilder(IdPrefix);
        foreach (var word in Words(mapName))
        {
            builder.Append(char.ToUpperInvariant(word[0]));
            builder.Append(word, 1, word.Length - 1);
        }

        return builder.Length > IdPrefix.Length ? builder.ToString() : IdPrefix + "Ship";
    }

    /// <summary>The grid and prototype file name, without <c>.yml</c>: the name's words in lower case joined by '_'.</summary>
    public static string FileName(string mapName)
    {
        var name = string.Join('_', Words(mapName).Select(word => word.ToLowerInvariant()));
        return name.Length > 0 ? name : "ship";
    }

    /// <summary>The letter and digit runs of a name.</summary>
    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                continue;
            }

            if (builder.Length > 0)
                words.Add(builder.ToString());

            builder.Clear();
        }

        if (builder.Length > 0)
            words.Add(builder.ToString());

        return words;
    }

    public static VesselSize SizeFor(int tiles)
    {
        return tiles switch
        {
            < MicroTiles => VesselSize.Micro,
            < SmallTiles => VesselSize.Small,
            < MediumTiles => VesselSize.Medium,
            _ => VesselSize.Large,
        };
    }

    /// <summary>
    /// The appraisal raised by <see cref="PriceMarkup"/> and rounded up: to 500 below 20,000, 1,000 below 100,000 and
    /// 5,000 above; never under <see cref="MinPrice"/>.
    /// </summary>
    public static int PriceFor(double appraisal)
    {
        var raised = double.IsFinite(appraisal) && appraisal > 0 ? appraisal * PriceMarkup : 0;
        var step = raised switch
        {
            < 20_000 => 500,
            < 100_000 => 1_000,
            _ => 5_000,
        };

        var price = Math.Ceiling(raised / step) * step;
        return (int) Math.Clamp(price, MinPrice, int.MaxValue / 2);
    }

    /// <summary>Cargo, Salvage and Mercenary for what was found; Civilian, the catch-all, when nothing was.</summary>
    public static List<VesselClass> ClassesFor(WFMappingVesselScan scan)
    {
        var classes = new List<VesselClass>();
        if (scan.CargoPallet)
            classes.Add(VesselClass.Cargo);

        if (scan.Mining)
            classes.Add(VesselClass.Salvage);

        if (scan.MountedGuns)
            classes.Add(VesselClass.Mercenary);

        if (classes.Count == 0)
            classes.Add(VesselClass.Civilian);

        return classes;
    }

    /// <summary>The engine type a prototype id names, if any.</summary>
    public static VesselEngine? EngineFor(string prototypeId)
    {
        foreach (var (part, engine) in EngineParts)
        {
            if (prototypeId.Contains(part, StringComparison.Ordinal))
                return engine;
        }

        return null;
    }

    /// <summary>Whether a prototype id is mining or salvage gear.</summary>
    public static bool IsMiningId(string prototypeId)
    {
        return MiningIds.Contains(prototypeId) ||
               MiningParts.Any(part => prototypeId.Contains(part, StringComparison.Ordinal));
    }

    /// <summary>The shuttle path the prototype points at.</summary>
    public static string ShuttlePath(string mapName)
    {
        return $"{ShuttleFolder}/{FileName(mapName)}.yml";
    }

    /// <summary>The prototype file's text: header comment, the vessel and its game map.</summary>
    public static string Build(string mapName, WFMappingVesselScan scan, WFMappingVesselText text)
    {
        var id = VesselId(mapName);
        var path = ShuttlePath(mapName);
        var builder = new StringBuilder();

        foreach (var line in text.Header)
        {
            builder.Append("# ").Append(OneLine(line)).Append('\n');
        }

        builder.Append('\n');
        builder.Append("- type: vessel\n");
        builder.Append("  id: ").Append(id).Append('\n');
        builder.Append("  parent: BaseVessel\n");
        builder.Append("  name: ").Append(Quote(mapName)).Append('\n');
        builder.Append("  description: ").Append(Quote(text.Description)).Append('\n');
        builder.Append("  price: ").Append(PriceFor(scan.Appraisal).ToString(CultureInfo.InvariantCulture))
            .Append(" # appraised at ").Append(Math.Round(scan.Appraisal).ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("  category: ").Append(SizeFor(scan.Tiles)).Append(" # ")
            .Append(scan.Tiles.ToString(CultureInfo.InvariantCulture)).Append(" tiles\n");
        builder.Append("  group: Shipyard\n");
        builder.Append("  shuttlePath: ").Append(path).Append('\n');
        builder.Append("  guidebookPage: null\n");
        builder.Append("  class:\n");
        foreach (var vesselClass in ClassesFor(scan))
        {
            builder.Append("  - ").Append(vesselClass).Append('\n');
        }

        if (scan.Engines.Count > 0)
        {
            builder.Append("  engine:\n");
            foreach (var engine in scan.Engines.OrderBy(e => e))
            {
                builder.Append("  - ").Append(engine).Append('\n');
            }
        }

        builder.Append('\n');
        builder.Append("- type: gameMap\n");
        builder.Append("  id: ").Append(id).Append('\n');
        builder.Append("  mapName: ").Append(Quote(mapName)).Append('\n');
        builder.Append("  mapPath: ").Append(path).Append('\n');
        builder.Append("  minPlayers: 0\n");
        builder.Append("  stations:\n");
        builder.Append("    ").Append(id).Append(":\n");
        builder.Append("      stationProto: StandardFrontierVessel\n");
        builder.Append("      components:\n");
        builder.Append("      - type: StationNameSetup\n");
        builder.Append("        mapNameTemplate: ").Append(Quote(mapName + " {1}")).Append('\n');
        builder.Append("        nameGenerator:\n");
        builder.Append("          !type:NanotrasenNameGenerator\n");
        builder.Append("          prefixCreator: '14'\n");
        builder.Append("      - type: StationJobs\n");
        builder.Append("        availableJobs:\n");
        builder.Append("          Contractor: [ 0, 0 ]\n");
        return builder.ToString();
    }

    /// <summary>A single-quoted YAML scalar on one line.</summary>
    private static string Quote(string value)
    {
        return "'" + OneLine(value).Replace("'", "''") + "'";
    }

    private static string OneLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}
