#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Ame.Components;
using Content.Server.Storage.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Ame.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string FuelPrototypes = @"
- type: wfEncounter
  id: WFTestFuelHub
  name: wf-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  ships:
  - key: hub
    vessel: Saintie
    fuelStores: 6

- type: wfEncounter
  id: WFTestFuelSmallHub
  name: wf-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  ships:
  - key: hub
    vessel: WFDredger

- type: wfEncounter
  id: WFTestFuelTransient
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: hub
    vessel: Saintie
";

    private const string Hub = "WFTestFuelHub";

    /// <summary>The crate of fuel stowed on a ship, if it has one.</summary>
    private EntityUid? FuelCrate(EntityUid ship)
    {
        var crates = SEntMan.EntityQueryEnumerator<EntityStorageComponent, MetaDataComponent, TransformComponent>();
        while (crates.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (xform.GridUid == ship && meta.EntityPrototype?.ID == "WFCrewFuelStores")
                return uid;
        }

        return null;
    }

    private List<EntityUid> Stored(EntityUid crate)
    {
        return SEntMan.GetComponent<EntityStorageComponent>(crate).Contents.ContainedEntities.ToList();
    }

    private int Jars(EntityUid crate)
    {
        return Stored(crate).Count(item => SEntMan.HasComponent<AmeFuelContainerComponent>(item));
    }

    private EntityUid SpawnEncounter(string id, Vector2 at)
    {
        Assert.That(Server.System<WFEncounterSystem>().TrySpawn(
            Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(id),
            new MapCoordinates(at, MapData.MapId), out var encounter), Is.True);
        return encounter;
    }

    private EntityUid AmeOn(EntityUid ship)
    {
        var ames = SEntMan.EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (ames.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ship)
                return uid;
        }

        Assert.Fail("The hull has no AME.");
        return default;
    }

    private bool InjectingFresh(EntityUid ame)
    {
        var controller = SEntMan.GetComponent<AmeControllerComponent>(ame);
        return controller.Injecting && controller.FuelSlot.Item is { } jar
            && SEntMan.GetComponent<AmeFuelContainerComponent>(jar).FuelAmount >= 200;
    }

    /// <summary>A persistent ship stows as many jars as its prototype asks for, more than the two a transient ship carries.</summary>
    [Test]
    public async Task PersistentShipStocksMoreFuelThanATransientOne()
    {
        await Server.WaitAssertion(() =>
        {
            var hub = SpawnEncounter(Hub, new Vector2(-39000, -39000));
            var other = SpawnEncounter("WFTestFuelTransient", new Vector2(-41000, -41000));
            var hubCrate = FuelCrate(SEntMan.GetComponent<WFEncounterComponent>(hub).Ships["hub"].Grid);
            var otherCrate = FuelCrate(SEntMan.GetComponent<WFEncounterComponent>(other).Ships["hub"].Grid);
            Assert.That(hubCrate, Is.Not.Null);
            Assert.That(otherCrate, Is.Not.Null);
            Assert.That(Jars(otherCrate!.Value), Is.EqualTo(2), "A transient ship carries the usual two.");
            Assert.That(Jars(hubCrate!.Value), Is.EqualTo(6), "A persistent one what its prototype asks for.");

            Server.System<WFEncounterSystem>().End(hub);
            Server.System<WFEncounterSystem>().End(other);
        });
        await RunTicks(10);
    }

    /// <summary>A persistent ship's emptied stores are topped up on the next check, once, to the usual two a reactor.</summary>
    [Test]
    public async Task PersistentShipRestocksItsEmptiedStores()
    {
        EntityUid hub = default, ship = default;
        await Server.WaitAssertion(() =>
        {
            hub = SpawnEncounter("WFTestFuelSmallHub", new Vector2(-43000, -43000));
            var state = SEntMan.GetComponent<WFEncounterComponent>(hub).Ships["hub"];
            ship = state.Grid;
            Assert.That(state.AutoRefuel, Is.False, "It has an engineer.");

            // The dredger burns chemicals; an antimatter reactor with a full jar is set in its hold, with no jars stowed for it.
            var tiles = Server.System<Content.Server._WF.NpcCrew.Systems.WFCrewPlannerSystem>().HoldTiles(ship, 8);
            var ame = SEntMan.SpawnEntity("AmeController", tiles[^1]);
            Assert.That(SEntMan.GetComponent<TransformComponent>(ame).Anchored, Is.True);
            var jar = SEntMan.SpawnEntity("AmeJar", tiles[^1]);
            Assert.That(SEntMan.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>()
                .TryInsert(ame, SEntMan.GetComponent<AmeControllerComponent>(ame).FuelSlot, jar, null), Is.True);
            Assert.That(Jars(FuelCrate(ship)!.Value), Is.Zero);
            state.NextStock = TimeSpan.Zero;
        });

        // The check comes with the next poll of the encounter, every five seconds.
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Jars(FuelCrate(ship)!.Value), Is.EqualTo(2), "The check stowed the usual two.");
            Server.System<WFEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }

    /// <summary>A persistent ship that lost its engineer swaps its spent jar itself, the way a fighter's crew does.</summary>
    [Test]
    public async Task PersistentShipWithoutAnEngineerRefuelsItself()
    {
        EntityUid hub = default, ame = default;
        await Server.WaitAssertion(() =>
        {
            hub = SpawnEncounter(Hub, new Vector2(-45000, -45000));
            var state = SEntMan.GetComponent<WFEncounterComponent>(hub).Ships["hub"];
            var ship = state.Grid;
            Assert.That(state.AutoRefuel, Is.False);
            Assert.That(Server.System<WFEncounterSystem>().HasLivingEngineer(state), Is.True);

            var engineers = new List<EntityUid>();
            var crew = SEntMan.EntityQueryEnumerator<WFCrewEngineerComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == ship)
                    engineers.Add(uid);
            }

            Assert.That(engineers, Is.Not.Empty);
            foreach (var uid in engineers)
                SEntMan.DeleteEntity(uid);
            Assert.That(Server.System<WFEncounterSystem>().HasLivingEngineer(state), Is.False);

            ame = AmeOn(ship);
            var jar = SEntMan.GetComponent<AmeControllerComponent>(ame).FuelSlot.Item;
            SEntMan.GetComponent<AmeFuelContainerComponent>(jar!.Value).FuelAmount = 50;
        });

        var fresh = false;
        for (var i = 0; i < 8 && !fresh; i++)
        {
            await RunTicks(60);
            await Server.WaitAssertion(() => fresh = InjectingFresh(ame));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(fresh, Is.True, "With nobody left to do it the crew swap the jar themselves.");
            Server.System<WFEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }
}
