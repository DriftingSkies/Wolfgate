#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._WF.MappingTools;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
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
/// Link mode's requests: signal links, device lists and access set as undoable edits on a mapper's own maps, refused
/// for anything outside the session, without the component or in nullspace; and access set before map init reaching
/// the airlock's electronics once the map starts.
/// </summary>
[TestFixture]
public sealed class MappingToolsLinkTest
{
    private static readonly ProtoId<AccessLevelPrototype> Engineering = "Engineering";
    private static readonly ProtoId<AccessLevelPrototype> Medical = "Medical";

    /// <summary>A grid of plating on a map, from (0, 0) to (7, 3).</summary>
    private static EntityUid MakeGrid(IEntityManager entMan, MapSystem mapSys, ITileDefinitionManager tileDefs,
        EntityUid map)
    {
        var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(map).MapId);
        grid.Comp.CanSplit = false;
        var plating = new Tile(tileDefs["Plating"].TileId);
        mapSys.SetTiles(grid.Owner, grid.Comp, Enumerable.Range(0, 8)
            .SelectMany(x => Enumerable.Range(0, 4).Select(y => (new Vector2i(x, y), plating)))
            .ToList());
        return grid.Owner;
    }

    private static EntityUid SpawnAt(IEntityManager entMan, string prototype, EntityUid grid, int x, int y)
    {
        return entMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(x + 0.5f, y + 0.5f)));
    }

    private static HashSet<(string, string)> Links(IEntityManager entMan, EntityUid source, EntityUid sink)
    {
        var links = new HashSet<(string, string)>();
        var linked = entMan.GetComponent<DeviceLinkSourceComponent>(source).LinkedPorts;
        if (linked.TryGetValue(sink, out var ports))
        {
            foreach (var (from, to) in ports)
            {
                links.Add((from.Id, to.Id));
            }
        }

        return links;
    }

    [Test]
    public async Task SignalLinks()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var deviceLink = entMan.System<DeviceLinkSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid button = default, door = default, foreignButton = default, foreignDoor = default, lost = default;
        List<(string Source, string Sink)> defaults = new();

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Link Signals", out var created), Is.True);
            var grid = MakeGrid(entMan, mapSys, tileDefs, created!.RootMap);
            button = SpawnAt(entMan, "SignalButton", grid, 1, 1);
            door = SpawnAt(entMan, "Airlock", grid, 3, 1);

            var foreignMap = mapSys.CreateMap(out _, runMapInit: false);
            var foreignGrid = MakeGrid(entMan, mapSys, tileDefs, foreignMap);
            foreignButton = SpawnAt(entMan, "SignalButton", foreignGrid, 1, 1);
            foreignDoor = SpawnAt(entMan, "Airlock", foreignGrid, 3, 1);
            lost = entMan.SpawnEntity("Airlock", MapCoordinates.Nullspace);

            var doorPorts = entMan.GetComponent<DeviceLinkSinkComponent>(door).Ports;
            defaults = deviceLink.GetDefaults(deviceLink.GetSourcePorts(button))
                .Where(l => doorPorts.Contains(l.sink))
                .Select(l => (l.source, l.sink))
                .ToList();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(defaults, Is.EquivalentTo(new[] { ("Pressed", "Toggle") }),
                "A button toggles a door by default.");

            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(button), entMan.GetNetEntity(door),
                new List<(string Source, string Sink)>(defaults)));
            Assert.Multiple(() =>
            {
                Assert.That(Links(entMan, button, door), Is.EquivalentTo(defaults), "The button links to the door.");
                Assert.That(entMan.GetComponent<DeviceLinkSinkComponent>(door).LinkedSources, Does.Contain(button),
                    "The door's sink knows its source.");
            });

            tools.StepHistory(a, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(Links(entMan, button, door), Is.Empty, "Undo unlinks them.");
                Assert.That(entMan.GetComponent<DeviceLinkSinkComponent>(door).LinkedSources, Does.Not.Contain(button));
            });

            tools.StepHistory(a, redo: true);
            Assert.That(Links(entMan, button, door), Is.EquivalentTo(defaults), "Redo links them again.");

            // Unlink: an empty list.
            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(button), entMan.GetNetEntity(door),
                new List<(string Source, string Sink)>()));
            Assert.Multiple(() =>
            {
                Assert.That(Links(entMan, button, door), Is.Empty, "Unlink removes every link between them.");
                Assert.That(entMan.GetComponent<DeviceLinkSinkComponent>(door).LinkedSources, Does.Not.Contain(button));
            });

            tools.StepHistory(a, redo: false);
            Assert.That(Links(entMan, button, door), Is.EquivalentTo(defaults), "Undoing the unlink brings it back.");
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var toggle = new List<(string Source, string Sink)> { ("Pressed", "Toggle") };

            // Outside the session.
            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(foreignButton),
                entMan.GetNetEntity(foreignDoor), toggle));
            Assert.That(Links(entMan, foreignButton, foreignDoor), Is.Empty, "A map outside the session is refused.");

            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(button),
                entMan.GetNetEntity(foreignDoor), toggle));
            Assert.That(Links(entMan, button, foreignDoor), Is.Empty, "A sink outside the session is refused.");

            // In nullspace.
            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(button), entMan.GetNetEntity(lost),
                toggle));
            Assert.That(Links(entMan, button, lost), Is.Empty, "A sink in nullspace is refused.");

            // Without the component: the button takes no signals.
            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(door), entMan.GetNetEntity(button),
                new List<(string Source, string Sink)> { ("DoorStatus", "Toggle") }));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<DeviceLinkSinkComponent>(button), Is.False);
                Assert.That(Links(entMan, door, button), Is.Empty, "A sink without the component is refused.");
            });

            // A port the sink doesn't have.
            RaiseAs(entMan, a, new MappingToolsLinkEvent(entMan.GetNetEntity(button), entMan.GetNetEntity(door),
                new List<(string Source, string Sink)> { ("Pressed", "Trigger") }));
            Assert.That(Links(entMan, button, door), Is.EquivalentTo(defaults), "An unknown port changes nothing.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeviceListsAndAccess()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        EntityUid alarm = default, vent = default, scrubber = default, wall = default;
        EntityUid door = default, door2 = default, foreignAlarm = default, foreignVent = default, foreignDoor = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Link Devices", out var created), Is.True);
            var grid = MakeGrid(entMan, mapSys, tileDefs, created!.RootMap);
            alarm = SpawnAt(entMan, "AirAlarm", grid, 0, 0);
            vent = SpawnAt(entMan, "GasVentPump", grid, 2, 0);
            scrubber = SpawnAt(entMan, "GasVentScrubber", grid, 4, 0);
            wall = SpawnAt(entMan, "WallSolid", grid, 6, 0);
            door = SpawnAt(entMan, "Airlock", grid, 2, 2);
            door2 = SpawnAt(entMan, "Airlock", grid, 4, 2);

            var foreignMap = mapSys.CreateMap(out _, runMapInit: false);
            var foreignGrid = MakeGrid(entMan, mapSys, tileDefs, foreignMap);
            foreignAlarm = SpawnAt(entMan, "AirAlarm", foreignGrid, 0, 0);
            foreignVent = SpawnAt(entMan, "GasVentPump", foreignGrid, 2, 0);
            foreignDoor = SpawnAt(entMan, "Airlock", foreignGrid, 2, 2);
        });
        await pair.RunTicksSync(5);

        HashSet<EntityUid> Devices(EntityUid holder) =>
            new(entMan.GetComponent<DeviceListComponent>(holder).Devices);

        List<NetEntity> Nets(params EntityUid[] uids) => uids.Select(uid => entMan.GetNetEntity(uid)).ToList();

        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(alarm), Nets(vent, scrubber),
                MappingDeviceListOp.Add));
            Assert.That(Devices(alarm), Is.EquivalentTo(new[] { vent, scrubber }), "The air alarm watches both.");

            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(alarm), Nets(vent),
                MappingDeviceListOp.Remove));
            Assert.That(Devices(alarm), Is.EquivalentTo(new[] { scrubber }), "The vent is taken out.");

            tools.StepHistory(a, redo: false);
            Assert.That(Devices(alarm), Is.EquivalentTo(new[] { vent, scrubber }), "Undo puts the vent back.");

            tools.StepHistory(a, redo: true);
            Assert.That(Devices(alarm), Is.EquivalentTo(new[] { scrubber }), "Redo takes it out again.");

            // Without the component: a wall is no network device, a vent has no device list.
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(alarm), Nets(wall),
                MappingDeviceListOp.Add));
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(vent), Nets(scrubber),
                MappingDeviceListOp.Add));
            Assert.That(Devices(alarm), Is.EquivalentTo(new[] { scrubber }), "A wall can't be added.");

            // Outside the session.
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(alarm), Nets(foreignVent),
                MappingDeviceListOp.Add));
            RaiseAs(entMan, a, new MappingToolsDeviceListEvent(entMan.GetNetEntity(foreignAlarm), Nets(foreignVent),
                MappingDeviceListOp.Add));
            Assert.Multiple(() =>
            {
                Assert.That(Devices(alarm), Is.EquivalentTo(new[] { scrubber }), "A device on another map is refused.");
                Assert.That(Devices(foreignAlarm), Is.Empty, "A holder outside the session is refused.");
            });
        });
        await pair.RunTicksSync(60);

        var accessSystem = entMan.System<AccessReaderSystem>();

        List<HashSet<ProtoId<AccessLevelPrototype>>> AccessOf(EntityUid uid)
        {
            Assert.That(accessSystem.GetMainAccessReader(uid, out var main), Is.True);
            return main!.Value.Comp.AccessLists;
        }

        await server.WaitAssertion(() =>
        {
            RaiseAs(entMan, a, new MappingToolsAccessEvent(Nets(door), new List<string> { Engineering }));
            Assert.Multiple(() =>
            {
                Assert.That(AccessOf(door), Has.Count.EqualTo(1));
                Assert.That(AccessOf(door)[0], Is.EquivalentTo(new[] { Engineering }), "The airlock wants Engineering.");
                Assert.That(entMan.HasComponent<WFMappingAccessFillComponent>(door), Is.True,
                    "Before map init the airlock is marked for its electronics.");
            });

            // Apply to selection: both doors at once, either access opens them.
            RaiseAs(entMan, a, new MappingToolsAccessEvent(Nets(door, door2),
                new List<string> { Engineering, Medical }));
            Assert.Multiple(() =>
            {
                foreach (var target in new[] { door, door2 })
                {
                    Assert.That(AccessOf(target).Select(set => set.Single()),
                        Is.EquivalentTo(new[] { Engineering, Medical }));
                }
            });

            tools.StepHistory(a, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(AccessOf(door).Select(set => set.Single()), Is.EquivalentTo(new[] { Engineering }),
                    "Undo restores the first door's access.");
                Assert.That(AccessOf(door2), Is.Empty, "Undo restores the second door's access.");
                Assert.That(entMan.HasComponent<WFMappingAccessFillComponent>(door2), Is.False);
            });

            tools.StepHistory(a, redo: true);
            Assert.That(AccessOf(door2).Select(set => set.Single()), Is.EquivalentTo(new[] { Engineering, Medical }),
                "Redo sets it again.");

            // Refusals: an unknown level, no reader, outside the session.
            RaiseAs(entMan, a, new MappingToolsAccessEvent(Nets(door), new List<string> { "WFNoSuchAccess" }));
            RaiseAs(entMan, a, new MappingToolsAccessEvent(Nets(wall), new List<string> { Engineering }));
            RaiseAs(entMan, a, new MappingToolsAccessEvent(Nets(foreignDoor), new List<string> { Engineering }));
            Assert.Multiple(() =>
            {
                Assert.That(AccessOf(door).Select(set => set.Single()), Is.EquivalentTo(new[] { Engineering, Medical }),
                    "An unknown access level changes nothing.");
                Assert.That(entMan.HasComponent<AccessReaderComponent>(wall), Is.False);
                Assert.That(AccessOf(foreignDoor), Is.Empty, "A reader outside the session is refused.");
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AccessReachesElectronicsAtMapInit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var accessSystem = entMan.System<AccessReaderSystem>();
        var testMap = await pair.CreateTestMap(initialized: false);
        EntityUid door = default;

        await server.WaitAssertion(() =>
        {
            door = entMan.SpawnEntity("Airlock", testMap.GridCoords);
            tools.SetAccess(pair.Player!, new MappingToolsAccessEvent(
                new List<NetEntity> { entMan.GetNetEntity(door) }, new List<string> { Engineering }));
            Assert.That(entMan.HasComponent<WFMappingAccessFillComponent>(door), Is.True);

            mapSys.InitializeMap(testMap.MapId);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(accessSystem.GetMainAccessReader(door, out var main), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(main!.Value.Owner, Is.Not.EqualTo(door), "Map init put electronics in the airlock.");
                Assert.That(main.Value.Comp.AccessLists.Select(set => set.Single()),
                    Is.EquivalentTo(new[] { Engineering }), "The electronics took the access set before map init.");
                Assert.That(entMan.HasComponent<WFMappingAccessFillComponent>(door), Is.False, "The marker is gone.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
