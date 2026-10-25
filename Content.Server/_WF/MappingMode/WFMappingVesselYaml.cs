using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode.Export;
using YamlDotNet.RepresentationModel;

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

    /// <summary>The scan as YAML text, kept beside the grid file so an export outside the session can reuse it.</summary>
    public string Write()
    {
        var root = new YamlMappingNode();
        root.Add("tiles", Tiles.ToString(CultureInfo.InvariantCulture));
        root.Add("appraisal", Appraisal.ToString("R", CultureInfo.InvariantCulture));
        root.Add("cargoPallet", CargoPallet ? "true" : "false");
        root.Add("mining", Mining ? "true" : "false");
        root.Add("mountedGuns", MountedGuns ? "true" : "false");
        var engines = new YamlSequenceNode();
        foreach (var engine in Engines.OrderBy(e => e))
        {
            engines.Add(engine.ToString());
        }

        root.Add("engine", engines);
        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    /// <summary>Reads a scan written by <see cref="Write"/>; null when the text isn't one.</summary>
    public static WFMappingVesselScan? Read(string text)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return null;
        }

        if (root == null)
            return null;

        var scan = new WFMappingVesselScan();
        foreach (var (keyNode, value) in root)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
                continue;

            var scalar = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "tiles":
                    int.TryParse(scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out scan.Tiles);
                    break;
                case "appraisal":
                    double.TryParse(scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out scan.Appraisal);
                    break;
                case "cargoPallet":
                    scan.CargoPallet = scalar == "true";
                    break;
                case "mining":
                    scan.Mining = scalar == "true";
                    break;
                case "mountedGuns":
                    scan.MountedGuns = scalar == "true";
                    break;
                case "engine" when value is YamlSequenceNode engines:
                    foreach (var engine in engines)
                    {
                        if (engine is YamlScalarNode { Value: { } name } && Enum.TryParse<VesselEngine>(name, out var parsed))
                            scan.Engines.Add(parsed);
                    }

                    break;
            }
        }

        return scan;
    }
}

/// <summary>The localized text the generated file carries: header comment lines and the placeholder description.</summary>
public sealed record WFMappingVesselText(IReadOnlyList<string> Header, string Description);

/// <summary>
/// The vessel fields as they go into the file, after the mapper's details overrode the generator's guesses.
/// <see cref="Guessed"/> names the YAML keys that are still guesses.
/// </summary>
public sealed record WFMappingVesselFields(
    string Name,
    string Description,
    int Price,
    VesselSize Category,
    List<VesselClass> Classes,
    ShipyardConsoleUiKey Group,
    List<VesselEngine> Engines,
    string Access,
    bool RequireCrew,
    List<string> Guessed)
{
    /// <summary>The vessel id, <c>WF</c> and the name in PascalCase.</summary>
    public string Id => WFMappingVesselYaml.VesselId(Name);

    /// <summary>The grid and prototype file name without <c>.yml</c>.</summary>
    public string FileName => WFMappingVesselYaml.FileName(Name);
}

/// <summary>
/// Writes a <c>vessel</c> prototype, and the <c>gameMap</c> that gives a bought ship its station, for a grid saved
/// in mapping mode. Pure: the scan and the text come from <see cref="WFMappingVesselSystem"/>.
/// </summary>
public static class WFMappingVesselYaml
{
    public const string IdPrefix = WFMappingShipNaming.IdPrefix;

    /// <summary>The resource folder the grid file goes in, as <c>shuttlePath</c> names it.</summary>
    public const string ShuttleFolder = WFMappingShipNaming.ShuttleFolder;

    /// <summary>The repository folders the two files go in.</summary>
    public const string GridRepoFolder = "Resources/SharedMaps/_WF/Community";
    public const string PrototypeRepoFolder = "Resources/Prototypes/_WF/Community";

    public const int MicroTiles = WFMappingShipNaming.MicroTiles;
    public const int SmallTiles = WFMappingShipNaming.SmallTiles;
    public const int MediumTiles = WFMappingShipNaming.MediumTiles;
    public const double PriceMarkup = WFMappingShipNaming.PriceMarkup;
    public const int MinPrice = WFMappingShipNaming.MinPrice;

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

    /// <summary><c>WF</c> and the name's words in PascalCase; see <see cref="WFMappingShipNaming.VesselId"/>.</summary>
    public static string VesselId(string mapName)
    {
        return WFMappingShipNaming.VesselId(mapName);
    }

    /// <summary>The grid and prototype file name without <c>.yml</c>; see <see cref="WFMappingShipNaming.FileName"/>.</summary>
    public static string FileName(string mapName)
    {
        return WFMappingShipNaming.FileName(mapName);
    }

    public static VesselSize SizeFor(int tiles)
    {
        return WFMappingShipNaming.SizeFor(tiles);
    }

    /// <summary>The appraisal raised and rounded up; see <see cref="WFMappingShipNaming.PriceFor"/>.</summary>
    public static int PriceFor(double appraisal)
    {
        return WFMappingShipNaming.PriceFor(appraisal);
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
        return WFMappingShipNaming.ShuttlePath(mapName);
    }

    /// <summary>
    /// The fields the file gets: the mapper's details where they set one, else the generator's guess from the scan.
    /// <paramref name="placeholderDescription"/> stands in while the description is empty.
    /// </summary>
    public static WFMappingVesselFields Resolve(string mapName, WFMappingVesselScan scan, WFMappingShipDetails? details,
        string placeholderDescription)
    {
        details ??= new WFMappingShipDetails();
        var guessed = new List<string>();
        var name = details.Name.Length > 0 ? details.Name : mapName;

        var description = details.Description;
        if (description.Length == 0)
        {
            description = placeholderDescription;
            guessed.Add("description");
        }

        var price = details.Price;
        if (price <= 0)
        {
            price = PriceFor(scan.Appraisal);
            guessed.Add("price");
        }

        var category = details.Category;
        if (category == VesselSize.All)
        {
            category = SizeFor(scan.Tiles);
            guessed.Add("category");
        }

        var classes = new List<VesselClass>(details.Classes);
        if (classes.Count == 0)
        {
            classes = ClassesFor(scan);
            guessed.Add("class");
        }

        var engines = details.AutoEngines ? scan.Engines.OrderBy(e => e).ToList() : new List<VesselEngine>(details.Engines);
        if (details.AutoEngines)
            guessed.Add("engine");

        return new WFMappingVesselFields(name, description, price, category, classes, details.Group, engines,
            details.Access, details.RequireCrew, guessed);
    }

    /// <summary>The prototype file's text from the generator's guesses alone: header comment, the vessel and its game map.</summary>
    public static string Build(string mapName, WFMappingVesselScan scan, WFMappingVesselText text)
    {
        return Build(Resolve(mapName, scan, null, text.Description), scan, text.Header);
    }

    /// <summary>The prototype file's text: header comment, the vessel and its game map.</summary>
    public static string Build(WFMappingVesselFields fields, WFMappingVesselScan scan, IReadOnlyList<string> header)
    {
        var id = fields.Id;
        var path = ShuttlePath(fields.Name);
        var builder = new StringBuilder();

        foreach (var line in header)
        {
            builder.Append("# ").Append(OneLine(line)).Append('\n');
        }

        builder.Append('\n');
        builder.Append("- type: vessel\n");
        builder.Append("  id: ").Append(id).Append('\n');
        builder.Append("  parent: BaseVessel\n");
        builder.Append("  name: ").Append(Quote(fields.Name)).Append('\n');
        builder.Append("  description: ").Append(Quote(fields.Description)).Append('\n');
        builder.Append("  price: ").Append(fields.Price.ToString(CultureInfo.InvariantCulture))
            .Append(" # appraised at ").Append(Math.Round(scan.Appraisal).ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("  category: ").Append(fields.Category).Append(" # ")
            .Append(scan.Tiles.ToString(CultureInfo.InvariantCulture)).Append(" tiles\n");
        builder.Append("  group: ").Append(fields.Group).Append('\n');
        if (fields.Access.Length > 0)
            builder.Append("  access: ").Append(fields.Access).Append('\n');

        if (fields.RequireCrew)
            builder.Append("  requireCrew: true\n");

        builder.Append("  shuttlePath: ").Append(path).Append('\n');
        builder.Append("  guidebookPage: null\n");
        builder.Append("  class:\n");
        foreach (var vesselClass in fields.Classes)
        {
            builder.Append("  - ").Append(vesselClass).Append('\n');
        }

        if (fields.Engines.Count > 0)
        {
            builder.Append("  engine:\n");
            foreach (var engine in fields.Engines)
            {
                builder.Append("  - ").Append(engine).Append('\n');
            }
        }

        builder.Append('\n');
        builder.Append("- type: gameMap\n");
        builder.Append("  id: ").Append(id).Append('\n');
        builder.Append("  mapName: ").Append(Quote(fields.Name)).Append('\n');
        builder.Append("  mapPath: ").Append(path).Append('\n');
        builder.Append("  minPlayers: 0\n");
        builder.Append("  stations:\n");
        builder.Append("    ").Append(id).Append(":\n");
        builder.Append("      stationProto: StandardFrontierVessel\n");
        builder.Append("      components:\n");
        builder.Append("      - type: StationNameSetup\n");
        builder.Append("        mapNameTemplate: ").Append(Quote(fields.Name + " {1}")).Append('\n');
        builder.Append("        nameGenerator:\n");
        builder.Append("          !type:NanotrasenNameGenerator\n");
        builder.Append("          prefixCreator: '14'\n");
        builder.Append("      - type: StationJobs\n");
        builder.Append("        availableJobs:\n");
        builder.Append("          Contractor: [ 0, 0 ]\n");
        return builder.ToString();
    }

    /// <summary>A single-quoted YAML scalar on one line.</summary>
    public static string Quote(string value)
    {
        return "'" + OneLine(value).Replace("'", "''") + "'";
    }

    private static string OneLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}
