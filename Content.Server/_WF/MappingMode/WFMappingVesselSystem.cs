using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Salvage.Magnet;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Looks over a grid for <see cref="WFMappingVesselYaml"/>: tiles, appraisal, cargo pallets, mining gear, mounted guns
/// and generators, and writes its vessel prototype with the player-facing text localized.
/// </summary>
public sealed class WFMappingVesselSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Reads a grid. Only entities standing on it count, not container contents.</summary>
    public WFMappingVesselScan Scan(EntityUid grid)
    {
        var scan = new WFMappingVesselScan();
        if (TryComp<MapGridComponent>(grid, out var gridComp))
        {
            var tiles = _map.GetAllTiles(grid, gridComp);
            while (tiles.MoveNext(out _))
            {
                scan.Tiles++;
            }
        }

        scan.Appraisal = _pricing.AppraiseGrid(grid);

        var ancestry = new Dictionary<string, List<string>>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<CargoPalletComponent>(child))
                scan.CargoPallet = true;

            if (HasComp<SalvageMagnetComponent>(child))
                scan.Mining = true;

            if (HasComp<GunComponent>(child) && Transform(child).Anchored)
                scan.MountedGuns = true;

            if (MetaData(child).EntityPrototype?.ID is not { } id)
                continue;

            if (!ancestry.TryGetValue(id, out var ids))
            {
                ids = new List<string>();
                foreach (var (parent, _) in _prototypes.EnumerateAllParents<EntityPrototype>(id, includeSelf: true))
                {
                    ids.Add(parent);
                }

                ancestry[id] = ids;
            }

            foreach (var ancestor in ids)
            {
                if (WFMappingVesselYaml.IsMiningId(ancestor))
                    scan.Mining = true;

                if (WFMappingVesselYaml.EngineFor(ancestor) is { } engine)
                {
                    scan.Engines.Add(engine);
                    break;
                }
            }
        }

        return scan;
    }

    /// <summary>The vessel prototype file for a grid saved under <paramref name="mapName"/>, from the guesses alone.</summary>
    public string Generate(EntityUid grid, string mapName)
    {
        return Generate(Scan(grid), mapName, null, out _);
    }

    /// <summary>
    /// The vessel prototype file for a scanned grid, the mapper's details overriding the guesses. The header says where
    /// the files go and which fields are still guesses; <paramref name="fields"/> is what went in.
    /// </summary>
    public string Generate(WFMappingVesselScan scan, string mapName, WFMappingShipDetails? details,
        out WFMappingVesselFields fields)
    {
        fields = WFMappingVesselYaml.Resolve(mapName, scan, details,
            Loc.GetString("wf-mapping-ship-vessel-description"));
        var fileName = fields.FileName;
        var header = new List<string>
        {
            Loc.GetString("wf-mapping-ship-vessel-header-made", ("name", mapName)),
            Loc.GetString("wf-mapping-ship-vessel-header-grid",
                ("download", WFMappingLibraryNames.DownloadMapFile(mapName)),
                ("path", $"{WFMappingVesselYaml.GridRepoFolder}/{fileName}.yml")),
            Loc.GetString("wf-mapping-ship-vessel-header-prototype",
                ("download", WFMappingLibraryNames.DownloadVesselFile(mapName)),
                ("path", $"{WFMappingVesselYaml.PrototypeRepoFolder}/{fileName}.yml")),
        };

        if (fields.Guessed.Count > 0)
        {
            header.Add(Loc.GetString("wf-mapping-ship-vessel-header-guesses",
                ("fields", string.Join(", ", fields.Guessed)), ("tiles", scan.Tiles)));
        }

        var set = new List<string>();
        foreach (var key in new[] { "name", "description", "price", "category", "class", "group", "engine", "access", "requireCrew" })
        {
            if (!fields.Guessed.Contains(key))
                set.Add(key);
        }

        header.Add(Loc.GetString("wf-mapping-ship-vessel-header-set", ("fields", string.Join(", ", set))));
        if (_prototypes.HasIndex<VesselPrototype>(fields.Id))
            header.Add(Loc.GetString("wf-mapping-ship-vessel-header-taken", ("id", fields.Id)));

        return WFMappingVesselYaml.Build(fields, scan, header);
    }
}
