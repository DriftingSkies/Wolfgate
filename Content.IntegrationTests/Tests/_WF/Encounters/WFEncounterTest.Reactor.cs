#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Radiation.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._WF.Encounters;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// A commissioned fission reactor is fuelled in the slots its prefab left for rods and its coolant loop charged
    /// once its pipes appear; the engineer at his post trims the control rods to the casing temperature, and a crew
    /// with no engineer swaps its spent rods for fresh ones. Crews take no radiation from any of it.
    /// </summary>
    [Test]
    public async Task ReactorIsFuelledChargedAndTrimmed()
    {
        EntityUid encounter = default, ship = default, reactor = default;
        var rods = 0f;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryRival");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-37000, -37000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["rival"].Grid;

            var crew = SEntMan.EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
            var crewmen = 0;
            while (crew.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != ship)
                    continue;
                crewmen++;
                Assert.That(SEntMan.HasComponent<RadiationReceiverComponent>(uid), Is.False, "A crewman takes no radiation.");
            }
            Assert.That(crewmen, Is.GreaterThan(0));

            // A small reactor on plating laid well beyond the hull, where no crewman's path runs.
            var grid = SEntMan.GetComponent<MapGridComponent>(ship);
            var maps = Server.System<SharedMapSystem>();
            var tile = new Vector2i((int) MathF.Ceiling(grid.LocalAABB.Right) + 8, 0);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                    maps.SetTile(ship, grid, tile + new Vector2i(dx, dy), new Tile(1));
            }
            reactor = SEntMan.SpawnEntity("NuclearReactorSmall", maps.GridTileToLocal(ship, grid, tile));
            Assert.That(SEntMan.GetComponent<TransformComponent>(reactor).Anchored, Is.True);
            var fission = SEntMan.GetComponent<NuclearReactorComponent>(reactor);
            Assert.That(CountFuelRods(fission), Is.EqualTo(0), "The shipyard's reactor comes without fuel.");

            Server.System<WFCrewWorkSystem>().Commission(ship);
            Assert.That(CountFuelRods(fission), Is.EqualTo(4), "Commissioning fuels the four slots the 5x5 prefab leaves for rods.");
            Assert.That(fission.ControlRodInsertion, Is.EqualTo(1f), "Control rods half out to warm her up.");
        });

        // The reactor spawns its pipes on its first atmos tick; the loop is charged on the tend tick after.
        await RunTicks(450);
        await Server.WaitAssertion(() =>
        {
            var fission = SEntMan.GetComponent<NuclearReactorComponent>(reactor);
            Assert.That(fission.InletEnt, Is.Not.Null, "The reactor has grown its inlet.");
            Assert.That(Server.System<NodeContainerSystem>().TryGetNode(fission.InletEnt!.Value, fission.PipeName, out PipeNode? inlet), Is.True);
            Assert.That(Server.System<NodeContainerSystem>().TryGetNode(fission.OutletEnt!.Value, fission.PipeName, out PipeNode? outlet), Is.True);
            // The core pumps the inlet's charge through to the outlet; with no pipes between them it piles up there.
            Assert.That(inlet!.Air.GetMoles(Gas.Nitrogen) + outlet!.Air.GetMoles(Gas.Nitrogen), Is.GreaterThan(0f), "The coolant loop is charged with nitrogen.");

            // Cold so far, so the engineer has been drawing the rods out; now too hot, and he pushes them in.
            Assert.That(fission.ControlRodInsertion, Is.LessThan(1f), "A cold casing has had the rods drawn out.");
            rods = fission.ControlRodInsertion;
        });
        // The parts around it draw a set casing temperature down within seconds, so it is held through a tend tick.
        await HoldCasing(reactor, 1190f);
        await Server.WaitAssertion(() =>
        {
            var fission = SEntMan.GetComponent<NuclearReactorComponent>(reactor);
            Assert.That(fission.ControlRodInsertion, Is.GreaterThan(rods), "A hot casing has the rods pushed in.");
        });
        await HoldCasing(reactor, 1900f);
        await Server.WaitAssertion(() =>
        {
            var fission = SEntMan.GetComponent<NuclearReactorComponent>(reactor);
            Assert.That(fission.ControlRodInsertion, Is.EqualTo(2f), "An overheating casing has them all the way in.");
        });
        await HoldCasing(reactor, 500f);
        await Server.WaitAssertion(() =>
        {
            var fission = SEntMan.GetComponent<NuclearReactorComponent>(reactor);
            Assert.That(fission.ControlRodInsertion, Is.LessThan(2f), "A cold casing has them drawn back out again.");

            // Spent fuel is swapped out by a crew with no engineer the way it tops up its own generators.
            foreach (var slot in FuelSlots(fission))
            {
                var part = fission.ComponentGrid[slot.X, slot.Y]!;
                part.Properties ??= new Content.Shared._FarHorizons.Materials.MaterialProperties();
                part.Properties.Radioactivity = 0f;
                part.Properties.NeutronRadioactivity = 0f;
            }
            Server.System<WFCrewWorkSystem>().Refuel(ship);
            foreach (var slot in FuelSlots(fission))
            {
                var part = fission.ComponentGrid[slot.X, slot.Y]!;
                Assert.That(part.Properties, Is.Not.Null);
                Assert.That(part.Properties!.Radioactivity + part.Properties.NeutronRadioactivity, Is.GreaterThanOrEqualTo(1f), "Every spent rod is replaced by a fresh one.");
            }
            Assert.That(CountFuelRods(fission), Is.EqualTo(4));

            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>Keeps the casing at a temperature for six seconds, long enough for one tend tick to read it there.</summary>
    private async Task HoldCasing(EntityUid reactor, float temperature)
    {
        for (var i = 0; i < 6; i++)
        {
            await Server.WaitAssertion(() => SEntMan.GetComponent<NuclearReactorComponent>(reactor).Temperature = temperature);
            await RunTicks(30);
        }
    }

    private static int CountFuelRods(NuclearReactorComponent reactor) => FuelSlots(reactor).Count;

    private static List<Vector2i> FuelSlots(NuclearReactorComponent reactor)
    {
        var slots = new List<Vector2i>();
        for (var x = 0; x < reactor.ReactorGridWidth; x++)
        {
            for (var y = 0; y < reactor.ReactorGridHeight; y++)
            {
                if (reactor.ComponentGrid[x, y] is { } part && part.HasRodType(ReactorPartComponent.RodTypes.FuelRod))
                    slots.Add(new Vector2i(x, y));
            }
        }

        return slots;
    }
}
