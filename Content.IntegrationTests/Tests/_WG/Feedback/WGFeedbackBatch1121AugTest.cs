#nullable enable
using System.Linq;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WG.Synthetics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._WG.Biotype;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Power.Components;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Augment, cyberware and synthetic-body reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121AugTest
{
    /// <summary>#1130: a synth's own cell, in its battery organ slot, charges inside a cyborg recharger.</summary>
    [Test]
    public async Task SynthChargesInsideBorgCharger()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var batteries = entMan.System<BatterySystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var synthBattery = entMan.System<SynthBatterySystem>();

        var synth = EntityUid.Invalid;
        await server.WaitPost(() => synth = entMan.SpawnEntity("MobSynth", map.GridCoords));

        // SynthBatteryPowerSystem seats the starting cell on its once-a-second update.
        await server.WaitRunTicks(90);

        var cell = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            entMan.GetComponent<SynthBatteryComponent>(synth).DrawRate = 0f;
            Assert.That(synthBattery.TryGetBattery(synth, out var battery), Is.True, "the synth has no cell");
            cell = battery!.Value.Owner;
            batteries.SetCharge(cell, battery.Value.Comp.MaxCharge * 0.1f, battery.Value.Comp);

            var charger = entMan.SpawnEntity("BorgCharger", map.GridCoords);
            var receiver = entMan.EnsureComponent<ApcPowerReceiverComponent>(charger);
            receiver.NeedsPower = false;
            receiver.Powered = true;

            Assert.That(containers.TryGetContainer(charger, "entity_storage", out var container), Is.True);
            Assert.That(containers.Insert(synth, container!), Is.True);
        });

        var before = 0f;
        await server.WaitPost(() => before = entMan.GetComponent<BatteryComponent>(cell).CurrentCharge);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<BatteryComponent>(cell).CurrentCharge, Is.GreaterThan(before + 500f),
                "the synth's cell did not charge in the cyborg recharger"));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// #1209, #1158: Nanite Repair Drones follow the body's biotype, not the species, so every character can take
    /// exactly one of the drones and Platelet Factories.
    /// </summary>
    [TestCase("Human", WGBiotype.Default, false)]
    [TestCase("Human", WGBiotype.Synthetic, true)]
    [TestCase("Avali", WGBiotype.Default, false)]
    [TestCase("Avali", WGBiotype.Synthetic, true)]
    [TestCase("IPC", WGBiotype.Default, true)]
    [TestCase("IPC", WGBiotype.Organic, false)]
    [TestCase("Synth", WGBiotype.Default, true)]
    public async Task NaniteDronesFollowBiotype(string species, WGBiotype biotype, bool drones)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(species).WithBiotype(biotype);
            var nanites = proto.Index<TraitPrototype>("NaniteRepairDrones");
            var platelets = proto.Index<TraitPrototype>("PlateletFactories");

            bool Allowed(TraitPrototype trait) =>
                !trait.SpeciesBlacklist.Contains(species) && !WGBiotypeGates.TraitBlocked(trait, profile, proto);

            Assert.Multiple(() =>
            {
                Assert.That(Allowed(nanites), Is.EqualTo(drones), "Nanite Repair Drones");
                Assert.That(Allowed(platelets), Is.EqualTo(!drones), "Platelet Factories");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1216: a cuffed chassis can't isolate a hand from its own panel to drop the cuffs.</summary>
    [Test]
    public async Task CuffedChassisCannotIsolateHand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var cuffs = entMan.System<SharedCuffableSystem>();
        var body = entMan.System<SharedBodySystem>();
        var panel = entMan.System<WGChassisPanelSystem>();

        var ipc = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            ipc = entMan.SpawnEntity("MobIPC", map.GridCoords);
            var handcuffs = entMan.SpawnEntity("Handcuffs", map.GridCoords);
            Assert.That(cuffs.TryAddNewCuffs(ipc, ipc, handcuffs), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<CuffableComponent>(ipc).CuffedHandCount, Is.GreaterThan(0), "precondition: cuffed");
            var hand = body.GetBodyChildrenOfType(ipc, BodyPartType.Hand).First().Id;
            Assert.That(panel.SetIsolated(ipc, hand, true, ipc), Is.False, "a cuffed hand was isolated");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<CuffableComponent>(ipc).CuffedHandCount, Is.GreaterThan(0),
                "the cuffs came off"));

        await pair.CleanReturnAsync();
    }
}
