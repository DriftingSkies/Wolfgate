using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Content.Shared._WF.MappingMode.Issues;

namespace Content.Server._WF.MappingMode.Export;

/// <summary>One file inside the export zip, by its path under the repository root.</summary>
public readonly record struct WFMappingExportEntry(string Path, byte[] Data)
{
    public WFMappingExportEntry(string path, string text) : this(path, new UTF8Encoding(false).GetBytes(text))
    {
    }
}

/// <summary>An issue as the README prints it: its severity and the localized title and hint.</summary>
public readonly record struct WFMappingExportIssueLine(WFMappingIssueSeverity Severity, string Title, string Hint);

/// <summary>
/// Pure helpers of the export package: the entry paths that mirror the repository, the map prototype text, the
/// README and the zip itself.
/// </summary>
public static class WFMappingExportPackage
{
    /// <summary>The repository folders a map's level files and its prototype go in.</summary>
    public const string MapRepoFolder = "Resources/Maps/_WF/Community";
    public const string PrototypeRepoFolder = WFMappingVesselYaml.PrototypeRepoFolder;

    /// <summary>The resource folder of the level files, as <c>mapPath</c> and a <c>zMap</c>'s <c>maps</c> name them.</summary>
    public const string MapResourceFolder = "/Maps/_WF/Community";

    public const string ReadmeName = "README.txt";

    /// <summary>The ship grid's entry: <c>Resources/SharedMaps/_WF/Community/&lt;file&gt;.yml</c>.</summary>
    public static string ShipGridEntry(string fileName)
    {
        return $"{WFMappingVesselYaml.GridRepoFolder}/{fileName}.yml";
    }

    /// <summary>The prototype file's entry, for a ship or a map: <c>Resources/Prototypes/_WF/Community/&lt;file&gt;.yml</c>.</summary>
    public static string PrototypeEntry(string fileName)
    {
        return $"{PrototypeRepoFolder}/{fileName}.yml";
    }

    /// <summary>
    /// A level's entry: <c>Resources/Maps/_WF/Community/&lt;file&gt;.yml</c> for a map of one level, else
    /// <c>Resources/Maps/_WF/Community/&lt;file&gt;/z&lt;D&gt;.yml</c>.
    /// </summary>
    public static string MapLevelEntry(string fileName, int depth, bool singleLevel)
    {
        return "Resources" + MapLevelResource(fileName, depth, singleLevel);
    }

    /// <summary>A level's resource path, as the prototype names it.</summary>
    public static string MapLevelResource(string fileName, int depth, bool singleLevel)
    {
        return singleLevel
            ? $"{MapResourceFolder}/{fileName}.yml"
            : $"{MapResourceFolder}/{fileName}/z{depth.ToString(CultureInfo.InvariantCulture)}.yml";
    }

    /// <summary>
    /// The prototype of a map export: a <c>gameMap</c> with a plain station for one level, else a <c>zMap</c> listing
    /// the levels bottom-up, the ground level named in a comment since the prototype has no field for it.
    /// </summary>
    public static string BuildMapPrototype(string mapName, string fileName, IReadOnlyList<int> depths,
        IReadOnlyList<string> header)
    {
        var id = WFMappingVesselYaml.VesselId(mapName);
        var builder = new StringBuilder();
        foreach (var line in header)
        {
            builder.Append("# ").Append(OneLine(line)).Append('\n');
        }

        builder.Append('\n');
        if (depths.Count == 1)
        {
            builder.Append("- type: gameMap\n");
            builder.Append("  id: ").Append(id).Append('\n');
            builder.Append("  mapName: ").Append(WFMappingVesselYaml.Quote(mapName)).Append('\n');
            builder.Append("  mapPath: ").Append(MapLevelResource(fileName, depths[0], true)).Append('\n');
            builder.Append("  minPlayers: 0\n");
            builder.Append("  stations:\n");
            builder.Append("    ").Append(id).Append(":\n");
            builder.Append("      stationProto: StandardNanotrasenStation\n");
            builder.Append("      components:\n");
            builder.Append("      - type: StationNameSetup\n");
            builder.Append("        mapNameTemplate: ").Append(WFMappingVesselYaml.Quote(mapName)).Append('\n');
            return builder.ToString();
        }

        var ground = 0;
        for (var i = 0; i < depths.Count; i++)
        {
            if (depths[i] == 0)
                ground = i;
        }

        builder.Append("- type: zMap\n");
        builder.Append("  id: ").Append(id).Append('\n');
        builder.Append("  # Levels bottom-up; the ground level (depth 0) is index ")
            .Append(ground.ToString(CultureInfo.InvariantCulture)).Append(".\n");
        builder.Append("  maps:\n");
        foreach (var depth in depths)
        {
            builder.Append("  - ").Append(MapLevelResource(fileName, depth, false)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The README: what the package is, where to unzip it, the files and what to check, then the issues report by
    /// severity. Every line comes localized; <paramref name="issues"/> null means no check ran.
    /// </summary>
    public static string BuildReadme(IReadOnlyList<string> intro, IReadOnlyList<string> files,
        IReadOnlyList<string> checks, string issuesHeading, IReadOnlyList<WFMappingExportIssueLine>? issues,
        string noIssuesLine, Func<WFMappingIssueSeverity, string> severityName)
    {
        var builder = new StringBuilder();
        foreach (var line in intro)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append('\n');
        foreach (var line in files)
        {
            builder.Append("  ").Append(line).Append('\n');
        }

        builder.Append('\n');
        foreach (var line in checks)
        {
            builder.Append("- ").Append(line).Append('\n');
        }

        builder.Append('\n').Append(issuesHeading).Append('\n');
        if (issues == null || issues.Count == 0)
        {
            builder.Append("  ").Append(noIssuesLine).Append('\n');
            return builder.ToString();
        }

        foreach (var severity in new[] { WFMappingIssueSeverity.Error, WFMappingIssueSeverity.Warning, WFMappingIssueSeverity.Note })
        {
            var any = false;
            foreach (var issue in issues)
            {
                if (issue.Severity != severity)
                    continue;

                if (!any)
                {
                    builder.Append('\n').Append(severityName(severity)).Append('\n');
                    any = true;
                }

                builder.Append("  - ").Append(OneLine(issue.Title)).Append('\n');
                if (issue.Hint.Length > 0)
                    builder.Append("      ").Append(OneLine(issue.Hint)).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>The zip holding every entry, deflated, built in one memory stream.</summary>
    public static byte[] Zip(IReadOnlyList<WFMappingExportEntry> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Path, CompressionLevel.Optimal);
                using var stream = zipEntry.Open();
                stream.Write(entry.Data, 0, entry.Data.Length);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Reads a zip back: each entry's path and data. For tests and checks.</summary>
    public static List<WFMappingExportEntry> Unzip(byte[] data)
    {
        var entries = new List<WFMappingExportEntry>();
        using var memory = new MemoryStream(data);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries.Add(new WFMappingExportEntry(entry.FullName, buffer.ToArray()));
        }

        return entries;
    }

    private static string OneLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}
