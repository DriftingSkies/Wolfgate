using Content.Server.Atmos.Monitor.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.SMES;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._WF.MappingMode.Issues;
using Content.Shared.Maps;
using Content.Shared.NodeContainer;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.MappingMode.Issues;

/// <summary>The rules a shipyard vessel needs: one grid, the parts that fly and power it, and a sane price.</summary>
public sealed partial class WFMappingIssuesSystem
{
    /// <summary>
    /// The markup over the appraisal a vessel's price must reach; the shipyard arbitrage test's default
    /// <c>minPriceMarkup</c>.
    /// </summary>
    public const double PriceFloorMarkup = 1.0;

    /// <summary>The name <c>SharedMapSystem</c> gives a new grid, which counts as none.</summary>
    public const string DefaultGridName = "grid";

    private void CheckShip(Report report, int levels, List<(EntityUid Grid, int Depth)> grids, int? price)
    {
        if (levels != 1 || grids.Count != 1)
        {
            Add(report, WFMappingIssueCatalog.ShipGridCount, null, null, null, ("grids", grids.Count), ("levels", levels));
            return;
        }

        var (grid, depth) = grids[0];
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        // The engine names every new grid "grid".
        var name = MetaData(grid).EntityName;
        if (string.IsNullOrWhiteSpace(name) || name == DefaultGridName)
            Add(report, WFMappingIssueCatalog.ShipNoName, grid, null, depth);

        CheckFloor(report, (grid, gridComp), depth);
        CheckParts(report, (grid, gridComp), depth);
        CheckPrice(report, grid, depth, price);
    }

    /// <summary>A crew needs a tile that isn't space to stand on.</summary>
    private void CheckFloor(Report report, Entity<MapGridComponent> grid, int depth)
    {
        var tiles = _map.GetAllTiles(grid, grid.Comp);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is { } t && _tileDefs[t.Tile.TypeId] is ContentTileDefinition def && !def.MapAtmosphere)
                return;
        }

        Add(report, WFMappingIssueCatalog.ShipNoFloor, grid, null, depth);
    }

    /// <summary>One pass over the grid's entities for the parts a vessel needs and the guns on it.</summary>
    private void CheckParts(Report report, Entity<MapGridComponent> grid, int depth)
    {
        var console = false;
        var dock = false;
        var thruster = false;
        var gyroscope = false;
        var source = false;
        var apc = false;
        var battery = false;
        var atmos = false;
        var spawn = false;

        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (HasComp<ShuttleConsoleComponent>(child) && xform.Anchored)
                console = true;

            if (HasComp<DockingComponent>(child))
                dock = true;

            if (TryComp<ThrusterComponent>(child, out var thrust))
            {
                if (thrust.Type == ThrusterType.Angular)
                    gyroscope = true;
                else
                    thruster = true;
            }

            if (HasComp<PowerSupplierComponent>(child))
                source = true;

            var isApc = HasComp<ApcComponent>(child);
            if (isApc)
                apc = true;

            // An SMES, or a substation: a network battery that isn't an APC.
            if (HasComp<SmesComponent>(child) || !isApc && HasComp<PowerNetworkBatteryComponent>(child))
                battery = true;

            if (HasComp<AirAlarmComponent>(child) || HasPipe(child))
                atmos = true;

            if (HasComp<SpawnPointComponent>(child))
                spawn = true;

            if (xform.Anchored && TryComp<HardpointAnchorableOnlyComponent>(child, out var gun) &&
                !OnHardpoint(grid, xform.Coordinates, gun))
            {
                Add(report, WFMappingIssueCatalog.ShipGunOffHardpoint, child, xform.Coordinates, depth,
                    ("name", NameOf(child)));
            }
        }

        if (!console)
            Add(report, WFMappingIssueCatalog.ShipNoConsole, grid, null, depth);
        if (!dock)
            Add(report, WFMappingIssueCatalog.ShipNoDock, grid, null, depth);
        if (!thruster)
            Add(report, WFMappingIssueCatalog.ShipNoThruster, grid, null, depth);
        if (!gyroscope)
            Add(report, WFMappingIssueCatalog.ShipNoGyroscope, grid, null, depth);
        if (!source)
            Add(report, WFMappingIssueCatalog.ShipNoPowerSource, grid, null, depth);
        if (!apc)
            Add(report, WFMappingIssueCatalog.ShipNoApc, grid, null, depth);
        if (!battery)
            Add(report, WFMappingIssueCatalog.ShipNoBattery, grid, null, depth);
        if (!atmos)
            Add(report, WFMappingIssueCatalog.ShipNoAtmos, grid, null, depth);
        if (!spawn)
            Add(report, WFMappingIssueCatalog.ShipNoSpawnPoint, grid, null, depth);
    }

    /// <summary>Whether the entity carries a gas pipe node.</summary>
    private bool HasPipe(EntityUid uid)
    {
        if (!TryComp<NodeContainerComponent>(uid, out var nodes))
            return false;

        foreach (var node in nodes.Nodes.Values)
        {
            if (node is PipeNode)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a free hardpoint the gun fits sits on its tile, as <c>SharedHardpointSystem.TryAnchorToHardpoint</c>
    /// decides at map init; the session's maps haven't run it.
    /// </summary>
    private bool OnHardpoint(Entity<MapGridComponent> grid, EntityCoordinates coords, HardpointAnchorableOnlyComponent gun)
    {
        var indices = _map.TileIndicesFor(grid, grid.Comp, coords);
        var anchored = _map.GetAnchoredEntities(grid, indices);
        while (anchored.MoveNext(out var uid))
        {
            if (!TryComp<HardpointComponent>(uid, out var hardpoint))
                continue;

            if ((hardpoint.CompatibleTypes & gun.CompatibleTypes) != 0 && hardpoint.CompatibleSizes >= gun.CompatibleSizes)
                return true;
        }

        return false;
    }

    /// <summary>The appraisal must be positive, and the price at or over its floor.</summary>
    private void CheckPrice(Report report, EntityUid grid, int depth, int? price)
    {
        var appraisal = report.Appraisal;
        if (appraisal <= 0)
        {
            Add(report, WFMappingIssueCatalog.ShipAppraisalZero, grid, null, depth);
            return;
        }

        var floor = (int) Math.Ceiling(appraisal * PriceFloorMarkup);
        var actual = price ?? WFMappingVesselYaml.PriceFor(appraisal);
        if (actual < floor)
        {
            Add(report, WFMappingIssueCatalog.ShipPriceFloor, grid, null, depth,
                ("price", actual), ("floor", floor), ("appraisal", (int) Math.Round(appraisal)));
        }
    }
}
