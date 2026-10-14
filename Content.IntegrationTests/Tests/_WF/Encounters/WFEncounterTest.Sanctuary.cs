#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>A player ship docked at a station is in sanctuary: no hunt, extorter or zone goes for it, and one that berths mid-run is let go.</summary>
public sealed partial class WFEncounterTest
{
    /// <summary>A ship and a station beside it, ready to dock, with the test player aboard the ship.</summary>
    private (EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock) BerthPair(Vector2 at)
    {
        var ship = ClampDeck(at);
        var station = ClampDeck(at + new Vector2(10f, 0f));
        SEntMan.EnsureComponent<StationMemberComponent>(station.Grid);
        Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(ship.Grid, new Vector2(1.5f, 0.5f)));
        return (ship.Grid, ship.Dock, station.Grid, station.Dock);
    }

    private void Berth((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock) pair)
    {
        Server.System<DockingSystem>().Dock((pair.ShipDock, SEntMan.GetComponent<DockingComponent>(pair.ShipDock)),
            (pair.StationDock, SEntMan.GetComponent<DockingComponent>(pair.StationDock)));
    }

    private void Unberth((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock) pair)
    {
        Server.System<DockingSystem>().Undock((pair.ShipDock, SEntMan.GetComponent<DockingComponent>(pair.ShipDock)));
    }

    /// <summary>Only a ship docked to a station, a station member without a deed, is in sanctuary.</summary>
    [Test]
    public async Task OnlyAShipDockedToAStationIsInSanctuary()
    {
        var pair = default((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock));
        await Server.WaitAssertion(() => pair = BerthPair(new Vector2(-61000, 61000)));
        var system = Server.System<WFEncounterSystem>();
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(system.DockedAtStation(pair.Ship), Is.False, "Not docked yet.");
            Berth(pair);
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(system.DockedAtStation(pair.Ship), Is.True, "Docked to a station.");
            Assert.That(system.DockedAtStation(pair.Station), Is.False, "The station is not docked to one.");
            Unberth(pair);
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(system.DockedAtStation(pair.Ship), Is.False, "Cast off.");

            // A ship alongside another ship is not in sanctuary.
            SEntMan.RemoveComponent<StationMemberComponent>(pair.Station);
            Berth(pair);
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(system.DockedAtStation(pair.Ship), Is.False, "The other grid is a ship.");
            Unberth(pair);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            SEntMan.DeleteEntity(pair.Ship);
            SEntMan.DeleteEntity(pair.Station);
        });
        await RunTicks(5);
    }

    /// <summary>A free hunter passes over a ship berthed at a station, and goes for it once it casts off.</summary>
    [Test]
    public async Task HunterPassesOverAShipBerthedAtAStation()
    {
        var pair = default((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock));
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            pair = BerthPair(new Vector2(-62000, 62000));
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Berth(pair);
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterHunter");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype,
                new MapCoordinates(new Vector2(-62000, 62000) + new Vector2(-1500f, 0f), MapData.MapId), out encounter), Is.True);
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey, Is.Null,
                "It does not go for a ship berthed at a station.");
            Unberth(pair);
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey == pair.Ship, 900);
        await Server.WaitAssertion(() =>
        {
            Server.System<WFEncounterSystem>().End(encounter);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            SEntMan.DeleteEntity(pair.Ship);
            SEntMan.DeleteEntity(pair.Station);
        });
        await RunTicks(10);
    }

    /// <summary>A hunter running down a ship drops it when it berths at a station, and goes back to it once it casts off.</summary>
    [Test]
    public async Task HunterDropsAPreyThatBerthsAtAStation()
    {
        var pair = default((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock));
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            pair = BerthPair(new Vector2(-63000, 63000));
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterHunter");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype,
                new MapCoordinates(new Vector2(-63000, 63000) + new Vector2(-1500f, 0f), MapData.MapId), out encounter), Is.True);
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey == pair.Ship, 900);
        await Server.WaitAssertion(() => Berth(pair));
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey == null, 900);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"];
            var orders = Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == state.Group).Objectives;
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.Hold }), "The run at it is called off.");
            Unberth(pair);
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey == pair.Ship, 900);
        await Server.WaitAssertion(() =>
        {
            Server.System<WFEncounterSystem>().End(encounter);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            SEntMan.DeleteEntity(pair.Ship);
            SEntMan.DeleteEntity(pair.Station);
        });
        await RunTicks(10);
    }

    /// <summary>An extorter lets go of a mark that berths at a station, hangs up, and does not pick it again while it is there.</summary>
    [Test]
    public async Task ExtorterReleasesAMarkThatBerthsAtAStation()
    {
        var at = new Vector2(-43000, 43000);
        var scene = await StartExtortion("WFTestExtorter", at, false);
        var station = default((EntityUid Grid, EntityUid Dock, EntityUid Helm));
        EntityUid shipDock = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Mark, Is.EqualTo(scene.PlayerShip));
            station = ClampDeck(at + new Vector2(0f, 10f));
            SEntMan.EnsureComponent<StationMemberComponent>(station.Grid);
            shipDock = SEntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(scene.PlayerShip, new Vector2(0.5f, 2.5f)));
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Server.System<DockingSystem>().Dock((shipDock, SEntMan.GetComponent<DockingComponent>(shipDock)),
                (station.Dock, SEntMan.GetComponent<DockingComponent>(station.Dock)));
            Assert.That(Server.System<WFEncounterSystem>().DockedAtStation(scene.PlayerShip), Is.True);
        });
        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Roam, 300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Mark, Is.Null, "The mark is let go.");
            Assert.That(scene.Extort.Tolls, Is.Zero);
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "The hail is hung up.");

            // Due another mark at once, and the only ship about is berthed.
            scene.Extort.NextMark = TimeSpan.Zero;
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam), "It does not go for a ship berthed at a station.");
            Assert.That(scene.Extort.Mark, Is.Null);
            Server.System<DockingSystem>().Undock((shipDock, SEntMan.GetComponent<DockingComponent>(shipDock)));
            scene.Extort.NextMark = TimeSpan.Zero;
        });
        await WaitUntilServer(() => scene.Extort.Mark == scene.PlayerShip, 900);
        await Server.WaitAssertion(() => SEntMan.DeleteEntity(station.Grid));
        await EndExtortion(scene);
    }

    /// <summary>A ship berthed at a station inside a ship's attack zone is neither warned nor fired on; cast off, it is.</summary>
    [Test]
    public async Task ZonesIgnoreAShipBerthedAtAStation()
    {
        var pair = default((EntityUid Ship, EntityUid ShipDock, EntityUid Station, EntityUid StationDock));
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-64000, 64000), MapData.MapId), out encounter), Is.True);
            pair = BerthPair(new Vector2(-63900, 64000));
        });
        await RunTicks(20);
        await Server.WaitAssertion(() => Berth(pair));
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Warned, Is.Empty, "A ship berthed at a station is not warned.");
            Assert.That(state.Engaged, Is.Empty, "Nor fired on.");
            Unberth(pair);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Engaged.Contains(pair.Ship), Is.True, "Cast off, it is an intruder.");
            Server.System<WFEncounterSystem>().End(encounter);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            SEntMan.DeleteEntity(pair.Ship);
            SEntMan.DeleteEntity(pair.Station);
        });
        await RunTicks(10);
    }
}
