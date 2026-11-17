using System.Linq;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Interaction;
using Content.Shared.Roles;
using Content.Server.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Reports from the 2026-11-10 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1110Test
{
    /// <summary>Report 708: a body with no legs is down on the floor but can still use what it reaches.</summary>
    [Test]
    public async Task LeglessBodyCanStillInteract()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid body = default, locker = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            locker = entMan.SpawnEntity("ClosetEmergency", map.GridCoords);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitPost(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var limbs = bodySystem.GetBodyChildrenOfType(body, BodyPartType.Leg)
                .Concat(bodySystem.GetBodyChildrenOfType(body, BodyPartType.Foot))
                .Select(part => part.Id)
                .ToList();
            Assert.That(limbs, Is.Not.Empty, "Precondition: the body has no legs to take.");
            foreach (var limb in limbs)
                entMan.DeleteEntity(limb);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var consciousness = entMan.GetComponent<WolfmedConsciousnessComponent>(body);
            Assert.That(consciousness.State, Is.EqualTo(WolfmedConsciousness.Downed), "Precondition: a legless body is not down.");
            Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.Legs), "Precondition: something besides the legs holds the body down.");
            Assert.That(entMan.System<ActionBlockerSystem>().CanInteract(body, locker), Is.True,
                "a legless body cannot use the locker beside it.");
            entMan.System<SharedInteractionSystem>().InteractionActivate(body, locker);
            Assert.That(entMan.GetComponent<EntityStorageComponent>(locker).Open, Is.True,
                "a legless body could not open the locker beside it.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 727: the Director of Care's starting kit, jaws included, fits every bag they can pick.</summary>
    [Test]
    public async Task DocKitFitsEveryBag()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var storage = entMan.System<SharedStorageSystem>();
            var kit = server.ProtoMan.Index<StartingGearPrototype>("DocGear").Storage["back"];
            Assert.That(kit, Does.Contain("SyndicateJawsOfLife"), "Precondition: the kit has no jaws.");
            foreach (var bagId in new[] { "ClothingBackpackMedical", "ClothingBackpackDuffelMedical", "ClothingBackpackSatchelMedical", "ClothingBackpackMessengerMedical" })
            {
                var bag = entMan.SpawnEntity(bagId, map.GridCoords);
                foreach (var item in kit)
                {
                    var ent = entMan.SpawnEntity(item, map.GridCoords);
                    Assert.That(storage.Insert(bag, ent, out _, playSound: false), Is.True, $"{item} does not fit in a {bagId} with the rest of the kit.");
                }
                entMan.DeleteEntity(bag);
            }
        });

        await pair.CleanReturnAsync();
    }
}
