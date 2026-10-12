using System.Linq;
using Content.Server._Goobstation.Sandevistan;
using Content.Server._WF.Genitals;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>An organ autosurgeon replaces the heart of a body whose torso has empty genital slots.</summary>
[TestFixture]
[TestOf(typeof(GenitalOrganSystem))]
public sealed class GenitalAutosurgeonTest
{
    private const string Autosurgeon = "AutosurgeonSandevistanMieyo";
    private const string NewHeart = "OrganHeartMMCSandevistan";

    [Test]
    public async Task HeartGoesIntoHeartSlotTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mob = EntityUid.Invalid;
        var torso = EntityUid.Invalid;
        var oldHeart = EntityUid.Invalid;
        var surgeon = EntityUid.Invalid;
        var heartSlot = SharedBodySystem.GetOrganContainerId("heart");

        await server.WaitAssertion(() =>
        {
            var containers = entMan.System<SharedContainerSystem>();

            // Opted in with nothing configured: five empty torso slots.
            mob = entMan.Spawn("MobHuman", map.MapCoords);
            GrantConsent(entMan, mob);
            Assert.That(HasAllSlots(entMan, mob), "The torso has no genital slots.");
            Assert.That(GenitalOrgans(entMan, mob), Is.Empty);

            torso = RootPart(entMan, mob)!.Value;
            oldHeart = containers.GetContainer(torso, heartSlot).ContainedEntities.Single();

            // The do-after itself is not under test: raise its completion directly.
            surgeon = entMan.Spawn(Autosurgeon, map.MapCoords);
            var ev = new AutoSurgeonDoAfterEvent();
            var args = new DoAfterArgs(entMan, mob, TimeSpan.Zero, ev, surgeon, mob, surgeon);
            ev.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
            entMan.EventBus.RaiseLocalEvent(surgeon, ev);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var containers = entMan.System<SharedContainerSystem>();
            var inHeartSlot = containers.GetContainer(torso, heartSlot).ContainedEntities;

            Assert.Multiple(() =>
            {
                Assert.That(inHeartSlot, Has.Count.EqualTo(1), "Heart slot");
                Assert.That(inHeartSlot.Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID),
                    Is.EqualTo(new[] { NewHeart }), "Organ in the heart slot");
                Assert.That(entMan.GetComponent<OrganComponent>(oldHeart).Body, Is.Null, "Old heart is still in the body.");

                foreach (var slot in GenitalOrganSystem.Slots)
                {
                    var id = SharedBodySystem.GetOrganContainerId(GenitalOrganSystem.SlotId(slot));
                    Assert.That(containers.GetContainer(torso, id).ContainedEntities, Is.Empty, $"{slot} slot");
                }

                Assert.That(entMan.HasComponent<SandevistanUserComponent>(mob), "The implant gave the body nothing.");
                Assert.That(entMan.GetComponent<AutoSurgeonComponent>(surgeon).Used, "Autosurgeon was not spent.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
