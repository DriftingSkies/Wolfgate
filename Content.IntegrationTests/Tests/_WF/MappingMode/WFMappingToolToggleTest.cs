#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Doors.Components;
using Content.Shared.Lock;
using Content.Shared.Tools.Components;
using Content.Shared.Wires;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Tool mode flips bolts, door, weld, panel and lock on a paused session map for a plain mapper, each one undo step,
/// and refuses an outsider, an entity without the state, welding an open door and opening a welded one.
/// </summary>
[TestFixture]
public sealed class WFMappingToolToggleTest
{
    [Test]
    public async Task TogglesFlipAndUndo()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        await Enable(pair);
        var dummies = await AddMappers(pair, 2);
        var mapper = dummies[0];
        var outsider = dummies[1];

        EntityUid airlock = default;
        EntityUid locker = default;
        EntityUid wall = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(mapper, "Tool Test", out var session), Is.True);
            var mapId = entMan.GetComponent<TransformComponent>(session!.RootMap).MapID;
            var grid = mapSys.CreateGridEntity(mapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            for (var x = 0; x < 3; x++)
            {
                mapSys.SetTile(grid, grid.Comp, new Vector2i(x, 0), plating);
            }

            airlock = entMan.SpawnEntity("Airlock", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
            locker = entMan.SpawnEntity("LockerSecurity", new EntityCoordinates(grid.Owner, 1.5f, 0.5f));
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid.Owner, 2.5f, 0.5f));
        });

        void Use(EntityUid target, MappingToggle toggle, ICommonSession? who = null)
        {
            RaiseAs(entMan, who ?? mapper, new MappingToolsToggleEvent(entMan.GetNetEntity(target), toggle));
        }

        void Undo() => RaiseAs(entMan, mapper, new MappingToolsHistoryEvent(false));
        void Redo() => RaiseAs(entMan, mapper, new MappingToolsHistoryEvent(true));
        DoorState Door() => entMan.GetComponent<DoorComponent>(airlock).State;
        bool Bolted() => entMan.GetComponent<DoorBoltComponent>(airlock).BoltsDown;
        bool Welded(EntityUid uid) => entMan.GetComponent<WeldableComponent>(uid).IsWelded;
        bool Solid() => entMan.GetComponent<PhysicsComponent>(airlock).CanCollide;

        await server.WaitAssertion(() =>
        {
            Use(airlock, MappingToggle.Bolts, outsider);
            Assert.That(Bolted(), Is.False, "An outsider's tool does nothing.");

            Use(wall, MappingToggle.Bolts);
            Use(wall, MappingToggle.Door);

            Use(airlock, MappingToggle.Bolts);
            Assert.That(Bolted(), Is.True, "Bolts go down without power.");
            Undo();
            Assert.That(Bolted(), Is.False, "Undo lifts them.");
            Redo();
            Assert.That(Bolted(), Is.True, "Redo drops them again.");
            Use(airlock, MappingToggle.Bolts);
            Assert.That(Bolted(), Is.False);
        });

        // Tool requests are throttled to a burst of ten.
        await WaitThrottle(pair, timing);
        await server.WaitAssertion(() =>
        {
            Use(airlock, MappingToggle.Door);
            Assert.That(Door(), Is.EqualTo(DoorState.Open), "The door opens straight away on a paused map.");
            Assert.That(Solid(), Is.False, "An open door doesn't collide.");

            Use(airlock, MappingToggle.Weld);
            Assert.That(Welded(airlock), Is.False, "An open door can't be welded.");

            Undo();
            Assert.That(Door(), Is.EqualTo(DoorState.Closed), "Undo closes it.");
            Assert.That(Solid(), Is.True, "A closed door collides again.");

            Use(airlock, MappingToggle.Weld);
            Assert.That(Welded(airlock), Is.True);
            Assert.That(Door(), Is.EqualTo(DoorState.Welded));
            Use(airlock, MappingToggle.Door);
            Assert.That(Door(), Is.EqualTo(DoorState.Welded), "A welded door won't open.");
            Undo();
            Assert.That(Welded(airlock), Is.False);
            Assert.That(Door(), Is.EqualTo(DoorState.Closed), "Unwelding leaves it closed.");
        });

        // Tool requests are throttled to a burst of ten.
        await WaitThrottle(pair, timing);
        await server.WaitAssertion(() =>
        {
            var panel = entMan.GetComponent<WiresPanelComponent>(airlock).Open;
            Use(airlock, MappingToggle.Panel);
            Assert.That(entMan.GetComponent<WiresPanelComponent>(airlock).Open, Is.EqualTo(!panel));
            Undo();
            Assert.That(entMan.GetComponent<WiresPanelComponent>(airlock).Open, Is.EqualTo(panel));

            var locked = entMan.GetComponent<LockComponent>(locker).Locked;
            Use(locker, MappingToggle.Lock);
            Assert.That(entMan.GetComponent<LockComponent>(locker).Locked, Is.EqualTo(!locked));
            Undo();
            Assert.That(entMan.GetComponent<LockComponent>(locker).Locked, Is.EqualTo(locked));

            Use(locker, MappingToggle.Weld);
            Assert.That(Welded(locker), Is.True, "A closed locker welds.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Runs the server past the tool request throttle's refill.</summary>
    private static async Task WaitThrottle(TestPair pair, IGameTiming timing)
    {
        var start = timing.CurTime;
        for (var i = 0; i < 600 && timing.CurTime - start < TimeSpan.FromSeconds(1.2); i++)
        {
            await pair.RunTicksSync(1);
        }
    }
}
