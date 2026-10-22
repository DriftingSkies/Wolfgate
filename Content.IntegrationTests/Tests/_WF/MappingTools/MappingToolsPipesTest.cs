#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// Pipe mode's draws on the server: a path becomes the right plain pipes, joins and upgrades the pipes around it,
/// is painted, paints whole networks, undoes as one edit and stops at a mapper's limits.
/// </summary>
[TestFixture]
public sealed class MappingToolsPipesTest
{
    private static readonly Color Distro = Color.FromHex("#0055CC");
    private static readonly Color Waste = Color.FromHex("#990000");

    private readonly record struct PipeInfo(string? Id, PipeDirection Directions, Color? Color, int Count);

    private static PipeInfo PipeAt(IEntityManager entMan, EntityUid grid, Vector2i cell,
        AtmosPipeLayer layer = AtmosPipeLayer.Primary)
    {
        var mapSys = entMan.System<MapSystem>();
        var anchored = new List<EntityUid>();
        mapSys.GetAnchoredEntities((grid, entMan.GetComponent<MapGridComponent>(grid)), cell, anchored);

        string? id = null;
        Color? color = null;
        var directions = PipeDirection.None;
        var count = 0;
        foreach (var uid in anchored)
        {
            if (!entMan.TryGetComponent(uid, out NodeContainerComponent? nodes))
                continue;

            foreach (var node in nodes.Nodes.Values)
            {
                if (node is not PipeNode pipe || pipe.CurrentPipeLayer != layer)
                    continue;

                directions |= pipe.CurrentPipeDirection;
                id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                color = entMan.TryGetComponent(uid, out AtmosPipeColorComponent? paint) ? paint.Color : null;
                count++;
            }
        }

        return new PipeInfo(id, directions, color, count);
    }

    private static List<Vector2i> Run(int x0, int x1, int y)
    {
        return Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2i(x, y)).ToList();
    }

    private static MappingToolsDrawEvent Pipes(NetEntity grid, List<Vector2i> cells, WFMappingPipeSpec spec,
        MappingDrawOp op = MappingDrawOp.Pipes)
    {
        return new MappingToolsDrawEvent(grid, op, cells, null, 0, null, Angle.Zero) { Pipes = spec };
    }

    [Test]
    public async Task DrawJoinPaintUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);
        var distro = new WFMappingPipeSpec(AtmosPipeLayer.Primary, Distro, false, true, true);
        var waste = distro with { Color = Waste };

        PipeInfo At(int x, int y, AtmosPipeLayer layer = AtmosPipeLayer.Primary) =>
            PipeAt(entMan, grid, new Vector2i(x, y), layer);

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(-2, 16)
                .SelectMany(x => Enumerable.Range(-2, 12).Select(y => (new Vector2i(x, y), plating)))
                .ToList());

            // A straight run: halves at the ends, straights between, all painted.
            tools.Draw(session, Pipes(net, Run(0, 4, 0), distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.East, Distro, 1)));
                for (var x = 1; x <= 3; x++)
                {
                    Assert.That(At(x, 0), Is.EqualTo(new PipeInfo("GasPipeStraight", PipeDirection.Lateral, Distro, 1)), $"{x}");
                }

                Assert.That(At(4, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.West, Distro, 1)));
            });

            // An L turns with a bend.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(0, 3), new(1, 3), new(1, 4) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(1, 3).Id, Is.EqualTo("GasPipeBend"));
                Assert.That(At(1, 3).Directions, Is.EqualTo(PipeDirection.NWBend));
                Assert.That(At(0, 3).Directions, Is.EqualTo(PipeDirection.East));
                Assert.That(At(1, 4).Directions, Is.EqualTo(PipeDirection.South));
            });

            // A run starting beside an existing straight turns it into a T.
            var straight = entMan.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid, new Vector2(8.5f, 0.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(straight).Anchored, Is.True);
            Assert.That(At(8, 0).Directions, Is.EqualTo(PipeDirection.Longitudinal));
            tools.Draw(session, Pipes(net, Run(9, 11, 0), waste));
            Assert.Multiple(() =>
            {
                Assert.That(At(8, 0), Is.EqualTo(new PipeInfo("GasPipeTJunction", PipeDirection.TEast, Waste, 1)));
                Assert.That(At(9, 0).Directions, Is.EqualTo(PipeDirection.Lateral));
                Assert.That(At(11, 0).Directions, Is.EqualTo(PipeDirection.West));
            });

            // A second network, apart from the first.
            tools.Draw(session, Pipes(net, Run(0, 3, 6), distro));
            Assert.That(At(2, 6).Color, Is.EqualTo(Distro));
        });

        await server.WaitAssertion(() =>
        {
            // Painting a pipe paints its run, not the run next door or the L.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, 0) }, waste, MappingDrawOp.PipeColor));
            Assert.Multiple(() =>
            {
                for (var x = 0; x <= 4; x++)
                {
                    Assert.That(At(x, 0).Color, Is.EqualTo(Waste), $"{x}");
                }

                Assert.That(At(2, 6).Color, Is.EqualTo(Distro), "A network that doesn't touch keeps its colour.");
                Assert.That(At(1, 3).Color, Is.EqualTo(Distro));
            });

            // Undo walks back each edit whole.
            tools.StepHistory(session, redo: false);
            Assert.That(Run(0, 4, 0).All(c => At(c.X, c.Y).Color == Distro), Is.True, "Undo puts the old colour back.");

            tools.StepHistory(session, redo: false);
            Assert.That(Run(0, 3, 6).All(c => At(c.X, c.Y).Count == 0), Is.True, "Undo removes the drawn run.");

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(At(8, 0).Id, Is.EqualTo("GasPipeStraight"), "The T is a straight again.");
                Assert.That(At(8, 0).Directions, Is.EqualTo(PipeDirection.Longitudinal));
                Assert.That(At(8, 0).Color, Is.EqualTo(Color.White));
                Assert.That(Run(9, 11, 0).All(c => At(c.X, c.Y).Count == 0), Is.True);
            });

            tools.StepHistory(session, redo: true);
            Assert.That(At(8, 0), Is.EqualTo(new PipeInfo("GasPipeTJunction", PipeDirection.TEast, Waste, 1)),
                "Redo builds the T again.");
        });

        await server.WaitAssertion(() =>
        {
            // The second layer uses its own prototypes and leaves the first layer alone.
            var second = distro with { Layer = AtmosPipeLayer.Secondary };
            tools.Draw(session, Pipes(net, Run(0, 2, 8), second));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 8, AtmosPipeLayer.Secondary).Id, Is.EqualTo("GasPipeHalfAlt1"));
                Assert.That(At(1, 8, AtmosPipeLayer.Secondary), Is.EqualTo(
                    new PipeInfo("GasPipeStraightAlt1", PipeDirection.Lateral, Distro, 1)));
                Assert.That(At(1, 8).Count, Is.Zero);
            });

            // A path across a pipe that isn't replaced keeps that pipe, and lays halves on either side of it.
            var keep = distro with { Replace = false, Color = Waste };
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, -1), new(2, 0), new(2, 1) }, keep));
            Assert.Multiple(() =>
            {
                Assert.That(At(2, 0).Id, Is.EqualTo("GasPipeStraight"), "Replace off leaves the pipe.");
                Assert.That(At(2, 0).Color, Is.EqualTo(Distro));
                Assert.That(At(2, -1).Directions, Is.EqualTo(PipeDirection.North));
                Assert.That(At(2, 1).Directions, Is.EqualTo(PipeDirection.South));
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-laid-skipped"));
            });

            // With replace on the crossing becomes a four-way.
            var cross = distro with { Color = Waste };
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(2, -1), new(2, 0), new(2, 1) }, cross));
            Assert.Multiple(() =>
            {
                Assert.That(At(2, 0), Is.EqualTo(new PipeInfo("GasPipeFourway", PipeDirection.Fourway, Waste, 1)));
                Assert.That(At(2, -1).Directions, Is.EqualTo(PipeDirection.North));
                Assert.That(At(2, 1).Directions, Is.EqualTo(PipeDirection.South));
            });

            // Auto takes the colour of the pipe it joins.
            var auto = distro with { Color = null, AutoColor = true };
            tools.Draw(session, Pipes(net, Run(12, 13, 0), auto));
            Assert.That(At(13, 0).Color, Is.EqualTo(Waste), "The run joins the waste pipe at 11.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapperPipeLimits()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid grid = default;
        var spec = new WFMappingPipeSpec(AtmosPipeLayer.Secondary, Distro, false, true, true);

        PipeInfo At(int x, int y) => PipeAt(entMan, grid, new Vector2i(x, y), AtmosPipeLayer.Secondary);

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Pipe Limits", out var created), Is.True);
            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(created!.RootMap).MapId);
            grid = newGrid.Owner;
            newGrid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, newGrid.Comp, Enumerable.Range(0, 8)
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => (new Vector2i(x, y), new Tile(tileDefs["Plating"].TileId))))
                .ToList());
        });

        await server.WaitAssertion(() =>
        {
            var net = entMan.GetNetEntity(grid);

            // A mapper lays second-layer pipes on their paused map.
            tools.Draw(a, Pipes(net, Run(0, 2, 0), spec));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 0), Is.EqualTo(new PipeInfo("GasPipeHalfAlt1", PipeDirection.East, Distro, 1)));
                Assert.That(At(1, 0), Is.EqualTo(new PipeInfo("GasPipeStraightAlt1", PipeDirection.Lateral, Distro, 1)));
                Assert.That(At(2, 0), Is.EqualTo(new PipeInfo("GasPipeHalfAlt1", PipeDirection.West, Distro, 1)));
            });

            // max_selection_cells caps the path.
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, 2);
            tools.Draw(a, Pipes(net, Run(0, 3, 2), spec));
            Assert.That(Run(0, 3, 2).All(c => At(c.X, c.Y).Count == 0), Is.True, "Four cells is over the cap of two.");
            cfg.SetCVar(WFMappingModeCVars.MaxSelectionCells, WFMappingModeCVars.MaxSelectionCells.DefaultValue);

            // Pipes need a floor.
            tools.Draw(a, Pipes(net, Run(6, 9, 1), spec));
            Assert.That(At(6, 1).Count, Is.Zero, "A path running off the floor is refused whole.");

            // max_edit_entities caps a paint.
            cfg.SetCVar(WFMappingModeCVars.MaxEditEntities, 2);
            tools.Draw(a, Pipes(net, new List<Vector2i> { new(1, 0) }, spec with { Color = Waste }, MappingDrawOp.PipeColor));
            Assert.That(Run(0, 2, 0).All(c => At(c.X, c.Y).Color == Distro), Is.True, "Three pipes is over the cap of two.");
            cfg.SetCVar(WFMappingModeCVars.MaxEditEntities, WFMappingModeCVars.MaxEditEntities.DefaultValue);

            tools.Draw(a, Pipes(net, new List<Vector2i> { new(1, 0) }, spec with { Color = Waste }, MappingDrawOp.PipeColor));
            Assert.That(Run(0, 2, 0).All(c => At(c.X, c.Y).Color == Waste), Is.True, "Under the cap the paint goes through.");

            // A mapper's layer-2 device is the hidden Alt1 prototype, allowed because the check names the primary.
            tools.Draw(a, Device(net, new Vector2i(4, 0), "GasPressurePump", spec));
            Assert.That(At(4, 0).Id, Is.EqualTo("GasPressurePumpAlt1"), tools.LastPipeReport);

            // A device needs a floor.
            tools.Draw(a, Device(net, new Vector2i(9, 0), "GasPressurePump", spec));
            Assert.Multiple(() =>
            {
                Assert.That(At(9, 0).Count, Is.Zero);
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-device-no-floor"));
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    private static MappingToolsDrawEvent Device(NetEntity grid, Vector2i cell, string prototype, WFMappingPipeSpec spec)
    {
        return new MappingToolsDrawEvent(grid, MappingDrawOp.PipeDevice, new List<Vector2i> { cell }, null, 0, prototype,
            Angle.Zero) { Pipes = spec };
    }

    /// <summary>Every device in the box exists, shows in the spawn menu and has pipe ports.</summary>
    [Test]
    public async Task DeviceListIsReal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in MappingToolsPipeDevices.Ids)
                {
                    Assert.That(prototypes.TryIndex<EntityPrototype>(id, out var prototype), Is.True, id);
                    Assert.That(prototype!.Abstract || prototype.HideSpawnMenu, Is.False, id);
                    Assert.That(prototype.TryGetComponent<NodeContainerComponent>(out var nodes, factory), Is.True, id);
                    Assert.That(nodes!.Nodes.Values.OfType<PipeNode>().Any(), Is.True, $"{id} has pipe ports.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The causes of "already like that": a lone click laid nothing, and a drag across kept pipes dropped the cells
    /// beside them. Now a click lays a half, skipped cells are counted, and refusals say why.
    /// </summary>
    [Test]
    public async Task ClicksAndSkippedCells()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);
        var distro = new WFMappingPipeSpec(AtmosPipeLayer.Primary, Distro, false, true, true);
        var keep = distro with { Replace = false };

        PipeInfo At(int x, int y) => PipeAt(entMan, grid, new Vector2i(x, y));

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(-2, 16)
                .SelectMany(x => Enumerable.Range(-2, 12).Select(y => (new Vector2i(x, y), plating)))
                .ToList());

            // A click with nothing around lays a half facing north, or the facing asked for.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(0, 0) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.North, Distro, 1)));
                Assert.That(tools.LastPipeReport, Is.Null);
            });

            tools.Draw(session, Pipes(net, new List<Vector2i> { new(8, 8) }, distro with { Facing = PipeDirection.East }));
            Assert.That(At(8, 8), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.East, Distro, 1)));

            // A click beside a straight joins it, turning it into a T.
            tools.Draw(session, Pipes(net, Run(0, 2, 6), distro));
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(1, 7) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(1, 7).Directions, Is.EqualTo(PipeDirection.South));
                Assert.That(At(1, 6), Is.EqualTo(new PipeInfo("GasPipeTJunction", PipeDirection.TNorth, Distro, 1)));
            });

            // Clicking that pipe again with Replace on changes nothing, and says so.
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(1, 7) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-nothing"));
                Assert.That(At(1, 7).Count, Is.EqualTo(1));
            });

            // A path over existing pipes with Replace off lays the rest and counts the pipes it skipped.
            tools.Draw(session, Pipes(net, Run(0, 4, 3), distro));
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(1, 2), new(1, 3), new(1, 4), new(2, 4), new(3, 4), new(3, 3), new(3, 2) }, keep));
            Assert.Multiple(() =>
            {
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-laid-skipped"));
                Assert.That(At(1, 3).Directions, Is.EqualTo(PipeDirection.Lateral), "The skipped straight is left alone.");
                Assert.That(At(3, 3).Directions, Is.EqualTo(PipeDirection.Lateral));
                Assert.That(At(1, 2).Directions, Is.EqualTo(PipeDirection.North), "The cell cut off by it is still laid.");
                Assert.That(At(3, 2).Directions, Is.EqualTo(PipeDirection.North));
                Assert.That(At(1, 4).Directions, Is.EqualTo(PipeDirection.East), "It doesn't open towards the kept pipe.");
                Assert.That(At(2, 4).Directions, Is.EqualTo(PipeDirection.Lateral));
            });

            // A path only over existing pipes with Replace off lays nothing and says why.
            tools.Draw(session, Pipes(net, Run(0, 4, 3), keep));
            Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-all-skipped"));

            // A device in the way is never rebuilt.
            entMan.SpawnEntity("GasPressurePump", new EntityCoordinates(grid, new Vector2(10.5f, 0.5f)));
            tools.Draw(session, Pipes(net, new List<Vector2i> { new(10, 0) }, distro));
            Assert.Multiple(() =>
            {
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-all-blocked"));
                Assert.That(At(10, 0).Id, Is.EqualTo("GasPressurePump"));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The device box: a device turns to meet the pipes around it, joins a plain pipe beside it, takes the layer's
    /// prototype and the colour, undoes as one edit, and nothing outside the device list is placed.
    /// </summary>
    [Test]
    public async Task DevicesJoinTurnAndUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;
        var net = entMan.GetNetEntity(grid.Owner);
        var distro = new WFMappingPipeSpec(AtmosPipeLayer.Primary, Distro, false, true, true);

        PipeInfo At(int x, int y, AtmosPipeLayer layer = AtmosPipeLayer.Primary) =>
            PipeAt(entMan, grid, new Vector2i(x, y), layer);

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(-2, 16)
                .SelectMany(x => Enumerable.Range(-2, 12).Select(y => (new Vector2i(x, y), plating)))
                .ToList());

            // A vent past the end of a run faces it, and the run's end half becomes a straight into it.
            tools.Draw(session, Pipes(net, Run(0, 3, 0), distro));
            tools.Draw(session, Device(net, new Vector2i(4, 0), "GasVentPump", distro));
            Assert.Multiple(() =>
            {
                Assert.That(At(4, 0), Is.EqualTo(new PipeInfo("GasVentPump", PipeDirection.West, Distro, 1)),
                    tools.LastPipeReport);
                Assert.That(At(3, 0), Is.EqualTo(new PipeInfo("GasPipeStraight", PipeDirection.Lateral, Distro, 1)));
            });

            // Undo takes the vent away and gives the half back.
            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(At(4, 0).Count, Is.Zero);
                Assert.That(At(3, 0), Is.EqualTo(new PipeInfo("GasPipeHalf", PipeDirection.West, Distro, 1)));
            });

            // A pump dropped on a straight in a run swaps it, in line with the run, keeping its facing's flow.
            tools.Draw(session, Pipes(net, Run(0, 6, 2), distro));
            tools.Draw(session, Device(net, new Vector2i(3, 2), "GasPressurePump", distro with { Facing = PipeDirection.West }));
            Assert.Multiple(() =>
            {
                Assert.That(At(3, 2).Id, Is.EqualTo("GasPressurePump"), tools.LastPipeReport);
                Assert.That(At(3, 2).Directions, Is.EqualTo(PipeDirection.Lateral));
                Assert.That(At(3, 2).Count, Is.EqualTo(2), "Its two ports.");
                Assert.That(At(2, 2).Directions, Is.EqualTo(PipeDirection.Lateral), "Its neighbours are untouched.");
                Assert.That(At(4, 2).Directions, Is.EqualTo(PipeDirection.Lateral));
            });

            // Without Replace the straight stays and the device is refused.
            tools.Draw(session, Device(net, new Vector2i(5, 2), "GasPressurePump", distro with { Replace = false }));
            Assert.Multiple(() =>
            {
                Assert.That(At(5, 2).Id, Is.EqualTo("GasPipeStraight"));
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-device-occupied"));
            });

            // Layer 2 places the Alt1 prototype; a device without one is moved to the layer before it anchors.
            var second = distro with { Layer = AtmosPipeLayer.Secondary };
            tools.Draw(session, Device(net, new Vector2i(0, 5), "GasPressurePump", second));
            tools.Draw(session, Device(net, new Vector2i(2, 5), "GasVentScrubber", second));
            Assert.Multiple(() =>
            {
                Assert.That(At(0, 5, AtmosPipeLayer.Secondary).Id, Is.EqualTo("GasPressurePumpAlt1"));
                Assert.That(At(2, 5, AtmosPipeLayer.Secondary).Id, Is.EqualTo("GasVentScrubber"));
                Assert.That(At(2, 5).Count, Is.Zero, "Nothing is left on the first layer.");
                Assert.That(At(2, 5, AtmosPipeLayer.Secondary).Directions, Is.EqualTo(PipeDirection.North),
                    "No pipes around: it faces the requested way.");
            });

            // A colour preset paints the device.
            tools.Draw(session, Device(net, new Vector2i(6, 6), "GasPassiveVent", distro with { Color = Waste }));
            Assert.That(At(6, 6).Color, Is.EqualTo(Waste));

            // A radiator only has the first layer.
            tools.Draw(session, Device(net, new Vector2i(8, 6), "HeatExchanger", second));
            Assert.Multiple(() =>
            {
                Assert.That(At(8, 6, AtmosPipeLayer.Secondary).Count, Is.Zero);
                Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-device-no-layer"));
            });

            // Only the device list is placed: a plain pipe, another entity and an unknown id are refused.
            foreach (var id in new[] { "GasPipeStraight", "GasCanister", "WFNoSuchPrototype" })
            {
                tools.Draw(session, Device(net, new Vector2i(10, 6), id, distro));
                Assert.Multiple(() =>
                {
                    Assert.That(At(10, 6).Count, Is.Zero, id);
                    Assert.That(tools.LastPipeReport, Is.EqualTo("wf-mapping-tools-pipes-device-bad"), id);
                });
            }
        });

        await pair.CleanReturnAsync();
    }
}
