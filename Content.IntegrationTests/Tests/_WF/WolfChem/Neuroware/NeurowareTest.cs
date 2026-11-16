using Content.Server._WF.WolfChem.Narcotics;
using Content.Server._WF.WolfChem.Neuroware;
using Content.Server._WF.Synthetics;
using Content.Server.Body.Components;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared._WF.WolfChem.Neuroware;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.WolfChem.Neuroware;

/// <summary>
/// A stick loads only into a host and feeds its profile at the stick's integrity with the drug's rush, copies lose
/// integrity and wear their source, a blank reads a drug in at its grade, and a panel reboot clears what is running.
/// </summary>
[TestFixture]
[TestOf(typeof(WFNeurowareSystem))]
public sealed class NeurowareTest
{
    private const string Profile = "WFCocaine";

    [Test]
    public async Task LoadCopyRebootTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid host = default;

        await server.WaitAssertion(() =>
        {
            var neuro = server.System<WFNeurowareSystem>();
            var intox = server.System<WFIntoxicationSystem>();
            var body = server.System<SharedBodySystem>();
            var appearance = server.System<SharedAppearanceSystem>();

            Entity<WFNeurowareStickComponent> Stick(string proto)
            {
                var uid = entMan.SpawnEntity(proto, map.GridCoords);
                return (uid, entMan.GetComponent<WFNeurowareStickComponent>(uid));
            }

            var organic = entMan.SpawnEntity("MobHuman", map.GridCoords);
            host = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WFNeurowareHostComponent>(body.GetBodyOrganEntityComps<BrainComponent>(host)[0].Owner);

            var stick = Stick("WFNeurowareStickWhiteline");
            Assert.That(neuro.Load(stick, organic, organic), Is.False, "An organic brain has no port.");
            Assert.That(neuro.Load(stick, host, host), Is.True);
            Assert.That(stick.Comp.Loads, Is.EqualTo(2));

            neuro.Tick(1f);
            Assert.That(intox.GetLevel(host, Profile), Is.EqualTo(stick.Comp.Rate * stick.Comp.Integrity).Within(0.001f),
                "A program feeds its rate each second.");
            intox.Tick(1f);
            Assert.That(intox.GetLevel(host, Profile), Is.GreaterThan(stick.Comp.Rate * 2f),
                "A program's dose gives the drug's rush.");

            var copy = Stick("WFNeurowareStick");
            var copyOfCopy = Stick("WFNeurowareStick");
            Assert.That(neuro.TryCopy(stick, copy, null), Is.True);
            Assert.That(neuro.TryCopy(copy, copyOfCopy, null), Is.True);
            Assert.That(neuro.TryCopy(stick, copy, null), Is.False, "Only a blank takes a copy.");
            Assert.That(stick.Comp.Loads, Is.EqualTo(1), "A copy spends a load of its source.");
            Assert.That(copyOfCopy.Comp.Loads, Is.EqualTo(1));
            Assert.That(copy.Comp.Loads, Is.EqualTo(0), "A copy's one load goes into its own copy.");
            Assert.That(neuro.TryCopy(copy, Stick("WFNeurowareStick"), null), Is.False, "A spent stick has nothing to copy.");
            Assert.Multiple(() =>
            {
                Assert.That(stick.Comp.Integrity, Is.EqualTo(0.9f).Within(0.001f), "A copy wears its source.");
                Assert.That(copy.Comp.Integrity, Is.EqualTo(0.85f * 0.9f).Within(0.001f));
                Assert.That(copyOfCopy.Comp.Integrity, Is.EqualTo(0.7225f).Within(0.001f), "Each generation loses 15%.");
                Assert.That(appearance.TryGetData<bool>(copy, WFNeurowareStickVisuals.Copied, out var taped) && taped,
                    Is.True, "A written blank shows its tape.");
            });

            server.System<WFChassisPanelSystem>().FinishReboot(host);
            Assert.That(intox.GetLevel(host, Profile), Is.EqualTo(0f), "A reboot clears the levels.");
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFNeurowareComponent>(host), Is.False, "A reboot stops running programs.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EncodeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var neuro = server.System<WFNeurowareSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var tags = server.System<TagSystem>();

            Entity<WFNeurowareStickComponent> Blank()
            {
                var uid = entMan.SpawnEntity("WFNeurowareStick", map.GridCoords);
                return (uid, entMan.GetComponent<WFNeurowareStickComponent>(uid));
            }

            // Ten units of cocaine at 60%: more than one full load of 8, so two loads of 5.
            var baggie = entMan.SpawnEntity("WFBaggieCocaine", map.GridCoords);
            Assert.That(solutions.TryGetSolution(baggie, "baggie", out var bag, out var bagSolution), Is.True);
            bagSolution!.SetPurity("WFCocaine", 0.6f);
            var stick = Blank();
            Assert.That(neuro.Encode(stick, baggie, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(stick.Comp.Profile?.Id, Is.EqualTo("WFCocaine"));
                Assert.That(stick.Comp.Integrity, Is.EqualTo(0.6f).Within(0.001f), "Integrity is the drug's grade.");
                Assert.That(stick.Comp.Loads, Is.EqualTo(2));
                Assert.That(stick.Comp.Amount, Is.EqualTo(5f).Within(0.001f), "No load is larger than a full one.");
                Assert.That(bag!.Value.Comp.Solution.Volume, Is.EqualTo(FixedPoint2.Zero), "The drug is used up.");
                Assert.That(entMan.IsQueuedForDeletion(baggie), Is.False, "A vessel is left empty.");
                Assert.That(tags.HasTag(stick, WFNarcoticGoodsSystem.Tag), Is.True, "A written blank is a narcotic.");
            });
            Assert.That(neuro.Encode(stick, entMan.SpawnEntity("WFBaggieCocaine", map.GridCoords), null), Is.False,
                "Only a blank reads a drug in.");

            // A rock is one small load, and goes with its drug.
            var rock = entMan.SpawnEntity("WFCrackRock", map.GridCoords);
            var small = Blank();
            Assert.That(neuro.Encode(small, rock, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(small.Comp.Loads, Is.EqualTo(1));
                Assert.That(small.Comp.Amount, Is.EqualTo(5f).Within(0.001f));
                Assert.That(entMan.IsQueuedForDeletion(rock), Is.True, "A solid is used up with its drug.");
            });

            // A beaker of meth fills three full loads of Burst and keeps the rest.
            var beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var glass, out _), Is.True);
            solutions.TryAddReagent(glass!.Value, "Desoxyephedrine", FixedPoint2.New(50));
            var full = Blank();
            Assert.That(neuro.Encode(full, beaker, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(full.Comp.Profile?.Id, Is.EqualTo("WFCrystal"));
                Assert.That(full.Comp.Loads, Is.EqualTo(WFNeurowareSystem.EncodeLoads));
                Assert.That(full.Comp.Amount, Is.EqualTo(10f).Within(0.001f));
                Assert.That(full.Comp.Integrity, Is.EqualTo(1f).Within(0.001f), "Standard grade reads in whole.");
                Assert.That(glass.Value.Comp.Solution.Volume, Is.EqualTo(FixedPoint2.New(20)));
            });

            // Hash reads in as Idle, which is legal like hash, and so are its copies.
            var hash = Blank();
            Assert.That(neuro.Encode(hash, entMan.SpawnEntity("WFHashBrick", map.GridCoords), null), Is.True);
            var hashCopy = Blank();
            Assert.That(neuro.TryCopy(hash, hashCopy, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(hash.Comp.Profile?.Id, Is.EqualTo("WFCannabis"));
                Assert.That(tags.HasTag(hash, WFNarcoticGoodsSystem.Tag), Is.False, "An Idle stick is legal.");
                Assert.That(tags.HasTag(hashCopy, WFNarcoticGoodsSystem.Tag), Is.False, "So is a copy of one.");
            });

            var water = entMan.SpawnEntity("Beaker", map.GridCoords);
            solutions.TryGetSolution(water, "beaker", out var wet, out _);
            solutions.TryAddReagent(wet!.Value, "Water", FixedPoint2.New(30));
            solutions.TryAddReagent(wet.Value, "Desoxyephedrine", FixedPoint2.New(0.5));
            Assert.That(neuro.Encode(Blank(), water, null), Is.False, "A trace is too little to read.");
        });

        await pair.CleanReturnAsync();
    }
}
