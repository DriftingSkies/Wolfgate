#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Showers;
using Content.Server.Forensics;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Showers;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Showers;

/// <summary>What a running shower does to whoever stands under it, and how its tank runs dry and refills.</summary>
[TestFixture]
[TestOf(typeof(WFShowerSystem))]
public sealed class WFShowerTest : WolfmedGameTest
{
    /// <summary>
    /// Clean water rinses a chemical burn off, takes stamina damage away and washes off fingerprints.
    /// </summary>
    [Test]
    public async Task ShowerRinsesChemicalBurnTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await CreateTestMap();

        EntityUid body = default;
        EntityUid torso = default;
        EntityUid shower = default;

        await server.WaitPost(() =>
        {
            var coords = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            body = entities.SpawnEntity("MobHuman", coords);
            shower = entities.SpawnEntity("WFShower", coords);
            torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .First(part => part.Component.PartType == BodyPartType.Torso).Id;

            entities.EnsureComponent<WolfmedChemicalBurnComponent>(torso);
            entities.EnsureComponent<ForensicsComponent>(body).Fingerprints.Add("deadbeef");
            entities.System<StaminaSystem>().TakeStaminaDamage(body, 50f, visual: false);

            Assert.That(entities.System<WFShowerSystem>().TrySetOn((shower, entities.GetComponent<WFShowerComponent>(shower)), true));
        });

        // Two spray ticks land before the stamina decay cooldown runs out.
        await RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WFShowerComponent>(shower);
            entities.System<SharedSolutionContainerSystem>().TryGetSolution(shower, comp.Solution, out _, out var tank);

            Assert.Multiple(() =>
            {
                Assert.That(comp.ActuallyOn, Is.True);
                Assert.That(entities.HasComponent<WolfmedChemicalBurnComponent>(torso), Is.False,
                    "the water rinsed the residue off.");
                Assert.That(entities.GetComponent<StaminaComponent>(body).StaminaDamage, Is.LessThan(50f),
                    "standing under warm water restores stamina.");
                Assert.That(entities.GetComponent<ForensicsComponent>(body).Fingerprints, Is.Empty);
                Assert.That(tank!.Volume, Is.LessThan(FixedPoint2.New(200)), "spraying uses the tank.");
            });
        });
    }

    /// <summary>A freezing shower chills its user, a scalding one burns them.</summary>
    [Test]
    public async Task ShowerTemperatureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await CreateTestMap();

        EntityUid cold = default;
        EntityUid hot = default;
        float coldBefore = 0;

        await server.WaitPost(() =>
        {
            var system = entities.System<WFShowerSystem>();
            var coldCoords = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            var hotCoords = map.GridCoords.Offset(new Vector2(1.5f, 0.5f));
            entities.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            cold = entities.SpawnEntity("MobHuman", coldCoords);
            hot = entities.SpawnEntity("MobHuman", hotCoords);
            coldBefore = entities.GetComponent<TemperatureComponent>(cold).CurrentTemperature;

            var coldShower = entities.SpawnEntity("WFShower", coldCoords);
            var coldComp = entities.GetComponent<WFShowerComponent>(coldShower);
            coldComp.Temperature = WFShowerTemperature.Freezing;
            Assert.That(system.TrySetOn((coldShower, coldComp), true));

            var hotShower = entities.SpawnEntity("WFShower", hotCoords);
            var hotComp = entities.GetComponent<WFShowerComponent>(hotShower);
            hotComp.Temperature = WFShowerTemperature.Boiling;
            Assert.That(system.TrySetOn((hotShower, hotComp), true));
        });

        await RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<TemperatureComponent>(cold).CurrentTemperature, Is.LessThan(coldBefore - 1f));
                Assert.That(entities.GetComponent<DamageableComponent>(hot).Damage.DamageDict.GetValueOrDefault("Heat"),
                    Is.GreaterThan(FixedPoint2.Zero), "scalding water burns.");
            });
        });
    }

    /// <summary>A dry shower won't start, runs until empty, then the recycler refills it.</summary>
    [Test]
    public async Task ShowerRunsDryAndRefillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await CreateTestMap();

        EntityUid shower = default;

        await server.WaitPost(() =>
        {
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var system = entities.System<WFShowerSystem>();
            shower = entities.SpawnEntity("WFShower", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var comp = entities.GetComponent<WFShowerComponent>(shower);
            solutions.TryGetSolution(shower, comp.Solution, out var tankEnt, out _);

            solutions.RemoveAllSolution(tankEnt!.Value);
            Assert.That(system.TrySetOn((shower, comp), true), Is.False, "an empty tank can't start.");

            solutions.TryAddReagent(tankEnt.Value, "Water", 12);
            Assert.That(system.TrySetOn((shower, comp), true), Is.True);
        });

        await RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WFShowerComponent>(shower);
            entities.System<SharedSolutionContainerSystem>().TryGetSolution(shower, comp.Solution, out _, out var tank);
            Assert.Multiple(() =>
            {
                Assert.That(comp.ActuallyOn, Is.False, "it stopped once the tank ran low.");
                Assert.That(comp.IntendedOn, Is.False, "and stays off in the default mode.");
                Assert.That(tank!.Volume, Is.GreaterThan(FixedPoint2.Zero), "the recycler is refilling it.");
            });
        });
    }
}
