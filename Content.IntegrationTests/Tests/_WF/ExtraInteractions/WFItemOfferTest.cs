#nullable enable
using Content.Shared._WF.ExtraInteractions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ExtraInteractions;

/// <summary>An offered item moves straight from the offerer's hand to the taker's, and the offer ends when it leaves the hand.</summary>
[TestFixture]
[TestOf(typeof(WFItemOfferSystem))]
public sealed class WFItemOfferTest
{
    private const string Item = "Crowbar";

    [Test]
    public async Task TakeOfferedItemTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var offers = entMan.System<WFItemOfferSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();

        EntityUid offerer = default, taker = default, item = default;
        await server.WaitAssertion(() =>
        {
            offerer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            taker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            item = entMan.SpawnEntity(Item, map.GridCoords);
            Assert.That(hands.TryPickup(offerer, item), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(offers.TryOffer(offerer), Is.True);
            Assert.That(entMan.GetComponent<WFItemOfferComponent>(offerer).Item, Is.EqualTo(item));

            // An empty-handed click on the offerer takes it, ahead of the hug popup.
            interaction.InteractHand(taker, offerer);

            Assert.Multiple(() =>
            {
                Assert.That(hands.IsHolding(taker, item), Is.True, "The taker did not get the item.");
                Assert.That(hands.IsHolding(offerer, item), Is.False, "The offerer still holds the item.");
                Assert.That(entMan.HasComponent<WFItemOfferComponent>(offerer), Is.False, "The offer outlived the take.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OfferEndsOnDropTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var offers = entMan.System<WFItemOfferSystem>();
        var hands = entMan.System<SharedHandsSystem>();

        EntityUid offerer = default, taker = default, item = default;
        await server.WaitAssertion(() =>
        {
            offerer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            taker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            item = entMan.SpawnEntity(Item, map.GridCoords);
            Assert.That(hands.TryPickup(offerer, item), Is.True);
            Assert.That(offers.TryOffer(offerer), Is.True);
            Assert.That(hands.TryDrop(offerer, item), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFItemOfferComponent>(offerer), Is.False, "Dropping the item did not end the offer.");
            Assert.That(offers.TryOffer(offerer), Is.False, "Offered with an empty hand.");
            Assert.That(hands.IsHolding(taker, item), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
