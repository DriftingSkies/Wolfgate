#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Ame.Components;
using Content.Server.Storage.Components;
using Content.Shared._WG.Encounters;
using Content.Shared.Ame.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    [TestPrototypes]
    private const string FuelPrototypes = @"
- type: wgEncounter
  id: WGTestFuelHub
  name: wg-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  ships:
  - key: hub
    vessel: Saintie
    fuelStores: 6

- type: wgEncounter
  id: WGTestFuelSmallHub
  name: wg-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  ships:
  - key: hub
    vessel: WGDredger

- type: wgEncounter
  id: WGTestFuelTransient
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: hub
    vessel: Saintie
";

    private const string Hub = "WGTestFuelHub";

    /// <summary>The crate of fuel stowed on a ship, if it has one.</summary>
    private EntityUid? FuelCrate(EntityUid ship)
    {
        var crates = SEntMan.EntityQueryEnumerator<EntityStorageComponent, MetaDataComponent, TransformComponent>();
        while (crates.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (xform.GridUid == ship && meta.EntityPrototype?.ID == "WGCrewFuelStores")
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
        Assert.That(Server.System<WGEncounterSystem>().TrySpawn(
            Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(id),
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
            var other = SpawnEncounter("WGTestFuelTransient", new Vector2(-41000, -41000));
            var hubCrate = FuelCrate(SEntMan.GetComponent<WGEncounterComponent>(hub).Ships["hub"].Grid);
            var otherCrate = FuelCrate(SEntMan.GetComponent<WGEncounterComponent>(other).Ships["hub"].Grid);
            Assert.That(hubCrate, Is.Not.Null);
            Assert.That(otherCrate, Is.Not.Null);
            Assert.That(Jars(otherCrate!.Value), Is.EqualTo(2), "A transient ship carries the usual two.");
            Assert.That(Jars(hubCrate!.Value), Is.EqualTo(6), "A persistent one what its prototype asks for.");

            Server.System<WGEncounterSystem>().End(hub);
            Server.System<WGEncounterSystem>().End(other);
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
            hub = SpawnEncounter("WGTestFuelSmallHub", new Vector2(-43000, -43000));
            var state = SEntMan.GetComponent<WGEncounterComponent>(hub).Ships["hub"];
            ship = state.Grid;
            Assert.That(state.AutoRefuel, Is.False, "It has an engineer.");

            // The dredger burns chemicals; an antimatter reactor with a full jar is set in its hold, with no jars stowed for it.
            var tiles = Server.System<Content.Server._WG.NpcCrew.Systems.WGCrewPlannerSystem>().HoldTiles(ship, 8);
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
            Server.System<WGEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }

    /// <summary>A persistent ship whose engineer lets the reactor burn dry has its jar swapped by the crew all the same.</summary>
    [Test]
    public async Task PersistentShipWithADryPlantRefuelsItself()
    {
        EntityUid hub = default, ame = default;
        await Server.WaitAssertion(() =>
        {
            hub = SpawnEncounter(Hub, new Vector2(-47000, -47000));
            var state = SEntMan.GetComponent<WGEncounterComponent>(hub).Ships["hub"];
            var ship = state.Grid;
            Assert.That(Server.System<WGEncounterSystem>().HasLivingEngineer(state), Is.True);

            // No jars for the engineer to fetch and no restock coming: only the backstop can refuel it.
            state.NextStock = TimeSpan.MaxValue;
            foreach (var item in Stored(FuelCrate(ship)!.Value))
                SEntMan.DeleteEntity(item);

            ame = AmeOn(ship);
            var jar = SEntMan.GetComponent<AmeControllerComponent>(ame).FuelSlot.Item;
            SEntMan.GetComponent<AmeFuelContainerComponent>(jar!.Value).FuelAmount = 0;
        });

        var fresh = false;
        for (var i = 0; i < 8 && !fresh; i++)
        {
            await RunTicks(60);
            await Server.WaitAssertion(() => fresh = InjectingFresh(ame));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(fresh, Is.True, "A dry reactor is refuelled though the engineer lives.");
            Server.System<WGEncounterSystem>().End(hub);
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
            var state = SEntMan.GetComponent<WGEncounterComponent>(hub).Ships["hub"];
            var ship = state.Grid;
            Assert.That(state.AutoRefuel, Is.False);
            Assert.That(Server.System<WGEncounterSystem>().HasLivingEngineer(state), Is.True);

            var engineers = new List<EntityUid>();
            var crew = SEntMan.EntityQueryEnumerator<WGCrewEngineerComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == ship)
                    engineers.Add(uid);
            }

            Assert.That(engineers, Is.Not.Empty);
            foreach (var uid in engineers)
                SEntMan.DeleteEntity(uid);
            Assert.That(Server.System<WGEncounterSystem>().HasLivingEngineer(state), Is.False);

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
            Server.System<WGEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }
}
