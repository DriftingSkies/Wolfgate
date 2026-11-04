#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.Atmos.Monitor.Components;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// Link mode's alarm thresholds: set through an air alarm on every monitor it lists, as an undoable edit, only on kinds
/// a monitor watches, refused outside the session; and values set before map init kept over the prototype's.
/// </summary>
[TestFixture]
public sealed class MappingToolsThresholdTest
{
    private const string StationTemperature = "stationTemperature";
    private const string FreezerTemperature = "freezerTemperature";

    private static EntityUid SpawnAt(IEntityManager entMan, string prototype, EntityUid grid, int x)
    {
        return entMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(x + 0.5f, 0.5f)));
    }

    private static EntityUid MakeGrid(IEntityManager entMan, MapSystem mapSys, ITileDefinitionManager tileDefs,
        EntityUid map)
    {
        var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(map).MapId);
        grid.Comp.CanSplit = false;
        var plating = new Tile(tileDefs["Plating"].TileId);
        mapSys.SetTiles(grid.Owner, grid.Comp,
            Enumerable.Range(0, 8).Select(x => (new Vector2i(x, 0), plating)).ToList());
        return grid.Owner;
    }

    private static float UpperOf(MappingToolsSystem tools, IEntityManager entMan, EntityUid uid)
    {
        return tools.EffectiveThresholds(entMan.GetComponent<AtmosMonitorComponent>(uid)).Temperature!.UpperBound.Value;
    }

    [Test]
    public async Task ThroughAirAlarm()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid alarm = default, vent = default, scrubber = default, wall = default, foreignVent = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Thresholds", out var created), Is.True);
            var grid = MakeGrid(entMan, mapSys, tileDefs, created!.RootMap);
            alarm = SpawnAt(entMan, "AirAlarm", grid, 0);
            vent = SpawnAt(entMan, "GasVentPumpFreezer", grid, 2);
            scrubber = SpawnAt(entMan, "GasVentScrubber", grid, 4);
            wall = SpawnAt(entMan, "WallSolid", grid, 6);

            var foreignMap = mapSys.CreateMap(out _, runMapInit: false);
            foreignVent = SpawnAt(entMan, "GasVentPumpFreezer", MakeGrid(entMan, mapSys, tileDefs, foreignMap), 2);
        });
        await pair.RunTicksSync(5);

        List<NetEntity> Nets(params EntityUid[] uids) => uids.Select(uid => entMan.GetNetEntity(uid)).ToList();

        var station = new AtmosAlarmThreshold(protoMan.Index<AtmosAlarmThresholdPrototype>(StationTemperature));
        var freezer = new AtmosAlarmThreshold(protoMan.Index<AtmosAlarmThresholdPrototype>(FreezerTemperature));

        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(alarm), Nets(vent, scrubber),
                MappingDeviceListOp.Add));

            var ventMonitor = entMan.GetComponent<AtmosMonitorComponent>(vent);
            Assert.Multiple(() =>
            {
                Assert.That(ventMonitor.TemperatureThreshold, Is.Null, "Before map init nothing is stored.");
                Assert.That(UpperOf(tools, entMan, vent), Is.EqualTo(freezer.UpperBound.Value),
                    "The freezer vent reads its prototype's thresholds.");
            });

            // A set carrying only a temperature: pressure and gases stay as they were.
            var set = new MappingThresholdSet { Temperature = station };
            var scrubberGases = tools.EffectiveThresholds(entMan.GetComponent<AtmosMonitorComponent>(scrubber)).Gases.Count;
            RaiseAs(entMan, a, new MappingToolsSetThresholdsEvent(Nets(alarm), set));
            Assert.Multiple(() =>
            {
                Assert.That(UpperOf(tools, entMan, vent), Is.EqualTo(station.UpperBound.Value),
                    "Setting the air alarm sets the vent.");
                Assert.That(UpperOf(tools, entMan, scrubber), Is.EqualTo(station.UpperBound.Value),
                    "And the scrubber.");
                Assert.That(ventMonitor.TemperatureThreshold, Is.Not.Null, "The value is stored on the vent.");
                Assert.That(ventMonitor.PressureThreshold, Is.Null, "Pressure wasn't in the set.");
                Assert.That(tools.EffectiveThresholds(entMan.GetComponent<AtmosMonitorComponent>(scrubber)).Gases,
                    Has.Count.EqualTo(scrubberGases), "No gas threshold is added.");
            });

            tools.StepHistory(a, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(ventMonitor.TemperatureThreshold, Is.Null, "Undo goes back to the prototype's.");
                Assert.That(UpperOf(tools, entMan, vent), Is.EqualTo(freezer.UpperBound.Value));
            });

            tools.StepHistory(a, redo: true);
            Assert.That(UpperOf(tools, entMan, vent), Is.EqualTo(station.UpperBound.Value), "Redo sets it again.");

            // Refusals: no monitor, outside the session, a bad value.
            var bad = new AtmosAlarmThreshold(station);
            bad.SetLimit(AtmosMonitorLimitType.UpperDanger, float.NaN);
            RaiseAs(entMan, a, new MappingToolsSetThresholdsEvent(Nets(wall), set));
            RaiseAs(entMan, a, new MappingToolsSetThresholdsEvent(Nets(foreignVent), set));
            RaiseAs(entMan, a, new MappingToolsSetThresholdsEvent(Nets(vent),
                new MappingThresholdSet { Temperature = bad }));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<AtmosMonitorComponent>(foreignVent).TemperatureThreshold, Is.Null,
                    "A monitor outside the session is refused.");
                Assert.That(UpperOf(tools, entMan, vent), Is.EqualTo(station.UpperBound.Value),
                    "A non-finite value changes nothing.");
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KeptAtMapInit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var testMap = await pair.CreateTestMap(initialized: false);
        var station = new AtmosAlarmThreshold(protoMan.Index<AtmosAlarmThresholdPrototype>(StationTemperature));
        EntityUid vent = default;

        await server.WaitAssertion(() =>
        {
            vent = entMan.SpawnEntity("GasVentPumpFreezer", testMap.GridCoords);
            tools.SetThresholds(pair.Player!, new MappingToolsSetThresholdsEvent(
                new List<NetEntity> { entMan.GetNetEntity(vent) }, new MappingThresholdSet { Temperature = station }));
            mapSys.InitializeMap(testMap.MapId);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var monitor = entMan.GetComponent<AtmosMonitorComponent>(vent);
            Assert.That(monitor.TemperatureThreshold!.UpperBound.Value, Is.EqualTo(station.UpperBound.Value),
                "Map init keeps the value set before it over the freezer prototype's.");
        });

        await pair.CleanReturnAsync();
    }
}
