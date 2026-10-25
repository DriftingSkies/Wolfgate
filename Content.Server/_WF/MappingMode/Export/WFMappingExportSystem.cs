using System.IO;
using System.Linq;
using Content.Server._WF.MappingMode.Issues;
using Content.Server._WF.MappingTools;
using Content.Server.Administration.Logs;
using Content.Shared._CE.ZLevels.Mapping.Prototypes;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;
using Content.Shared.Database;
using Content.Shared.Maps;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode.Export;

/// <summary>
/// Packages a library version as one zip laid out like the repository (a ship's grid and vessel prototype, or a map's
/// levels and its <c>gameMap</c> or <c>zMap</c>, plus a README with the issues report), or sends a version's one grid
/// file bare, and streams it through <see cref="WFMappingDownloadSystem"/>. Also moves a mapper to an issue's place.
/// </summary>
public sealed class WFMappingExportSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFMappingDownloadSystem _download = default!;
    [Dependency] private WFMappingIssuesSystem _issues = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;
    [Dependency] private WFMappingVesselSystem _vessel = default!;

    /// <summary>One export per five seconds per player; a zip of a station takes a moment to deflate.</summary>
    private readonly WFMappingThrottle<NetUserId> _exportThrottle = new(1 / 5.0, 1);

    private readonly WFMappingThrottle<NetUserId> _goToThrottle = new(2, 4);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFMappingExportRequest>(OnExportRequest);
        SubscribeNetworkEvent<WFMappingGoToIssueRequest>(OnGoToIssueRequest);
    }

    private void OnExportRequest(WFMappingExportRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !Enum.IsDefined(ev.Kind))
            return;

        if (!_exportThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            _mapping.Notify(player, false, "wf-mapping-export-too-soon");
            return;
        }

        if (_download.IsDownloading(player.UserId))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-busy");
            return;
        }

        if (_mapping.IsTesting(player))
        {
            _mapping.Notify(player, false, "wf-mapping-export-while-testing");
            return;
        }

        WFMappingLibrarySystem.LibraryMap? map;
        WFMappingLibrarySystem.LibraryVersion? version;
        try
        {
            if (!_library.TryFindVersion(player.UserId, ev.MapName, ev.Version, out map, out version))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading the mapping library of {player} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-mode-library-read-failed");
            return;
        }

        try
        {
            var data = TryBuild(player, map, version, ev.Kind, ev.Details);
            if (data == null)
                return;

            var fileName = WFMappingLibraryNames.ExportFile(map.Name, ev.Kind);
            if (!_download.TryStartData(player, map.Name, version.Name.Folder, fileName, data, $"export as {ev.Kind}"))
                return;

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{player:player} exported their library map \"{map.Name}\" {version.Name.Folder} as a {ev.Kind}: {fileName} ({data.Length} bytes)");
        }
        catch (Exception e)
        {
            Log.Error($"Exporting {player}'s library map \"{map.Name}\" {version.Name.Folder} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-export-failed");
        }
    }

    /// <summary>
    /// The session the export can be checked against: the one the player owns and is present in, when it goes by the
    /// map's name and the version is the map's newest with nothing changed since, so its live grids are the version's.
    /// Only the owner's saves clear <see cref="WFMappingSession.Dirty"/>, so a member's library says nothing about it.
    /// </summary>
    private WFMappingSession? SessionOf(ICommonSession player, WFMappingLibrarySystem.LibraryMap map,
        WFMappingLibrarySystem.LibraryVersion version)
    {
        return _mapping.TryGetPresent(player, out var session) &&
               session.Owner == player.UserId &&
               string.Equals(session.Name, map.Name, StringComparison.OrdinalIgnoreCase) &&
               !session.Dirty &&
               map.Versions.LastOrDefault(v => v.Levels.ContainsKey(0))?.Name.Folder == version.Name.Folder
            ? session
            : null;
    }

    /// <summary>Builds the package (the bare grid file for <see cref="WFMappingExportKind.Grid"/>), or tells the player why not and returns null.</summary>
    public byte[]? TryBuild(ICommonSession player, WFMappingLibrarySystem.LibraryMap map,
        WFMappingLibrarySystem.LibraryVersion version, WFMappingExportKind kind, WFMappingShipDetails? details)
    {
        switch (kind)
        {
            case WFMappingExportKind.Ship:
                return TryBuildShip(player, map, version, SessionOf(player, map, version),
                    WFMappingExportLimits.Sanitize(details ?? new WFMappingShipDetails()));
            case WFMappingExportKind.Grid:
                return TryReadGrid(player, map, version);
            default:
                return BuildMap(player, map, version, SessionOf(player, map, version));
        }
    }

    /// <summary>The version's one grid file as saved, under the same one-grid rule as a ship; no check runs.</summary>
    private byte[]? TryReadGrid(ICommonSession player, WFMappingLibrarySystem.LibraryMap map,
        WFMappingLibrarySystem.LibraryVersion version)
    {
        if (!WFMappingDownloadSystem.TryGetFiles(map.Name, version, true, out _, out var error, out var errorArgs))
        {
            _mapping.Notify(player, false, error, errorArgs);
            return null;
        }

        var data = ReadFile(version.Grids[version.ShipDepth!.Value]);
        if (data == null)
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-too-large",
                ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
        }

        return data;
    }

    private byte[]? TryBuildShip(ICommonSession player, WFMappingLibrarySystem.LibraryMap map,
        WFMappingLibrarySystem.LibraryVersion version, WFMappingSession? session, WFMappingShipDetails details)
    {
        if (!WFMappingDownloadSystem.TryGetFiles(map.Name, version, true, out _, out var error, out var errorArgs))
        {
            _mapping.Notify(player, false, error, errorArgs);
            return null;
        }

        var depth = version.ShipDepth!.Value;
        var scan = LiveGrid(session) is { } grid ? _vessel.Scan(grid) : _library.ReadScan(version, depth);
        var scanned = scan != null;
        scan ??= new WFMappingVesselScan();

        var fields = WFMappingVesselYaml.Resolve(map.Name, scan, details, string.Empty);
        List<WFMappingIssue>? issues = null;
        if (session != null)
        {
            issues = _issues.Check(session, WFMappingIssueScope.Ship, fields.Price);
            var errors = issues.Count(issue => issue.Severity == WFMappingIssueSeverity.Error);
            if (errors > 0)
            {
                _mapping.Notify(player, false, "wf-mapping-export-errors", ("count", errors));
                return null;
            }
        }

        _library.TryWriteShipDetails(player.UserId, map, version, details);
        var vessel = _vessel.Generate(scan, map.Name, details, out fields);
        var gridData = ReadFile(version.Grids[depth]);
        if (gridData == null)
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-too-large",
                ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
            return null;
        }

        var gridEntry = WFMappingExportPackage.ShipGridEntry(fields.FileName);
        var protoEntry = WFMappingExportPackage.PrototypeEntry(fields.FileName);
        var files = new List<string>
        {
            Loc.GetString("wf-mapping-export-readme-file-grid", ("path", gridEntry)),
            Loc.GetString("wf-mapping-export-readme-file-prototype", ("path", protoEntry), ("id", fields.Id)),
            Loc.GetString("wf-mapping-export-readme-file-readme"),
        };

        var checks = new List<string>();
        if (fields.Guessed.Count > 0)
            checks.Add(Loc.GetString("wf-mapping-export-readme-check-guessed", ("fields", string.Join(", ", fields.Guessed))));

        checks.Add(Loc.GetString("wf-mapping-export-readme-check-price", ("price", fields.Price),
            ("appraisal", (int) Math.Round(scan.Appraisal))));
        if (!scanned)
            checks.Add(Loc.GetString("wf-mapping-export-readme-check-no-scan"));

        if (_prototypes.HasIndex<VesselPrototype>(fields.Id))
            checks.Add(Loc.GetString("wf-mapping-export-readme-check-taken", ("id", fields.Id)));

        checks.Add(Loc.GetString("wf-mapping-export-readme-check-test"));

        var readme = Readme(Loc.GetString("wf-mapping-export-readme-ship-intro", ("name", map.Name),
                ("version", version.Name.Folder)), files, checks, issues, WFMappingIssueScope.Ship);
        return WFMappingExportPackage.Zip(new List<WFMappingExportEntry>
        {
            new(gridEntry, gridData),
            new(protoEntry, vessel),
            new(WFMappingExportPackage.ReadmeName, readme),
        });
    }

    private byte[]? BuildMap(ICommonSession player, WFMappingLibrarySystem.LibraryMap map,
        WFMappingLibrarySystem.LibraryVersion version, WFMappingSession? session)
    {
        var fileName = WFMappingVesselYaml.FileName(map.Name);
        var depths = version.Levels.Keys.ToList();
        var single = depths.Count == 1;
        var entries = new List<WFMappingExportEntry>();
        var files = new List<string>();
        foreach (var (depth, path) in version.Levels)
        {
            var data = ReadFile(path);
            if (data == null)
            {
                _mapping.Notify(player, false, "wf-mapping-mode-download-too-large",
                    ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
                return null;
            }

            var entry = WFMappingExportPackage.MapLevelEntry(fileName, depth, single);
            entries.Add(new WFMappingExportEntry(entry, data));
            files.Add(Loc.GetString("wf-mapping-export-readme-file-level", ("path", entry), ("depth", depth)));
        }

        var protoEntry = WFMappingExportPackage.PrototypeEntry(fileName);
        var id = WFMappingVesselYaml.VesselId(map.Name);
        files.Add(Loc.GetString(single ? "wf-mapping-export-readme-file-gamemap" : "wf-mapping-export-readme-file-zmap",
            ("path", protoEntry), ("id", id)));
        files.Add(Loc.GetString("wf-mapping-export-readme-file-readme"));

        var header = new List<string>
        {
            Loc.GetString("wf-mapping-export-proto-header", ("name", map.Name), ("version", version.Name.Folder)),
        };
        entries.Add(new WFMappingExportEntry(protoEntry,
            WFMappingExportPackage.BuildMapPrototype(map.Name, fileName, depths, header)));

        var checks = new List<string>
        {
            Loc.GetString(single ? "wf-mapping-export-readme-check-station" : "wf-mapping-export-readme-check-zmap",
                ("ground", depths.IndexOf(0))),
        };
        if (single ? _prototypes.HasIndex<GameMapPrototype>(id) : _prototypes.HasIndex<CEZLevelMapPrototype>(id))
            checks.Add(Loc.GetString("wf-mapping-export-readme-check-taken", ("id", id)));

        var issues = session != null ? _issues.Check(session, WFMappingIssueScope.Map) : null;
        var readme = Readme(Loc.GetString("wf-mapping-export-readme-map-intro", ("name", map.Name),
            ("version", version.Name.Folder), ("levels", depths.Count)), files, checks, issues, WFMappingIssueScope.Map);
        entries.Add(new WFMappingExportEntry(WFMappingExportPackage.ReadmeName, readme));
        return WFMappingExportPackage.Zip(entries);
    }

    /// <summary>The README text with the common intro, the files and checks given, and the issues localized.</summary>
    private string Readme(string intro, List<string> files, List<string> checks, List<WFMappingIssue>? issues,
        WFMappingIssueScope scope)
    {
        var lines = new List<string>
        {
            intro,
            Loc.GetString("wf-mapping-export-readme-unzip"),
        };

        List<WFMappingExportIssueLine>? issueLines = null;
        if (issues != null)
        {
            issueLines = new List<WFMappingExportIssueLine>(issues.Count);
            foreach (var issue in issues)
            {
                var hint = WFMappingIssueCatalog.TryGet(issue.RuleId, out var rule) && rule.Id != WFMappingIssueCatalog.More
                    ? Loc.GetString(rule.HintKey)
                    : string.Empty;
                issueLines.Add(new WFMappingExportIssueLine(issue.Severity, Loc.GetString(issue.LocKey, issue.Args()), hint));
            }
        }

        var heading = issues == null
            ? Loc.GetString("wf-mapping-export-readme-unchecked")
            : Loc.GetString("wf-mapping-export-readme-issues", ("scope", scope.ToString()), ("count", issues.Count));
        return WFMappingExportPackage.BuildReadme(lines, files, checks, heading, issueLines,
            Loc.GetString("wf-mapping-export-readme-no-issues"),
            severity => Loc.GetString($"wf-mapping-export-readme-severity-{severity.ToString().ToLowerInvariant()}"));
    }

    /// <summary>The session's one grid when it has exactly one level holding exactly one grid; else null.</summary>
    private EntityUid? LiveGrid(WFMappingSession? session)
    {
        if (session == null || TerminatingOrDeleted(session.RootMap))
            return null;

        var levels = _zLevels.GetLevels(session.RootMap, out _);
        if (levels.Count != 1)
            return null;

        EntityUid? found = null;
        var children = Transform(levels[0]).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!HasComp<MapGridComponent>(child))
                continue;

            if (found != null)
                return null;

            found = child;
        }

        return found;
    }

    private byte[]? ReadFile(ResPath path)
    {
        using var stream = _res.UserData.OpenRead(path);
        return WFMappingDownloadSystem.ReadCapped(stream, WFMappingDownloadAssembler.MaxFileBytes);
    }

    private void OnGoToIssueRequest(WFMappingGoToIssueRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session) ||
            !_goToThrottle.TryTake(player.UserId, _timing.CurTime) ||
            player.AttachedEntity is not { } observer)
        {
            return;
        }

        // A tester is on their character, not the observer, so there is nothing to move.
        if (_mapping.IsTesting(player))
        {
            _mapping.Notify(player, false, "wf-mapping-export-while-testing");
            return;
        }

        MapCoordinates target;
        if (ev.Entity is { } netEntity)
        {
            if (!TryGetEntity(netEntity, out var entity) || TerminatingOrDeleted(entity))
            {
                _mapping.Notify(player, false, "wf-mapping-export-goto-gone");
                return;
            }

            target = _transform.GetMapCoordinates(entity.Value);
        }
        else if (ev.Where is { } netCoords)
        {
            var coords = GetCoordinates(netCoords);
            if (!coords.IsValid(EntityManager))
            {
                _mapping.Notify(player, false, "wf-mapping-export-goto-gone");
                return;
            }

            target = _transform.ToMapCoordinates(coords);
        }
        else
        {
            return;
        }

        if (!_map.TryGetMap(target.MapId, out var map) || !_mapping.IsSessionMap(map.Value, session))
        {
            _mapping.Notify(player, false, "wf-mapping-ship-goto-not-yours");
            return;
        }

        _transform.SetMapCoordinates(observer, target);
    }
}
