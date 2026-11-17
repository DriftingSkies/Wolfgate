#nullable enable
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.Shipyard.Components;
using Content.Server._WG.Traders;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Traders;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Reports from the 2026-11-15 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1115Test
{
    /// <summary>#950: the ASF headset talks on the ASF channel by default.</summary>
    [Test]
    public async Task AsfHeadsetDefaultsToAsf()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var headset = server.EntMan.SpawnEntity("WGAsfClothingHeadset", map.GridCoords);
            var holder = server.EntMan.GetComponent<EncryptionKeyHolderComponent>(headset);
            Assert.That(holder.DefaultChannel, Is.EqualTo("WGAsf"));
            server.EntMan.DeleteEntity(headset);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#978: job vouchers list a ship; Mono vouchers offer nothing by access alone.</summary>
    [TestCase("ShipVoucherFrontierJanitor", "Bucket")]
    [TestCase("ShipVoucherFrontierMailCarrier", "Hermes")]
    public async Task JobVoucherListsItsShip(string voucher, string vessel)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>(voucher);
            Assert.That(proto.TryComp<ShipyardVoucherComponent>(out var comp, server.EntMan.ComponentFactory), Is.True);
            Assert.That(comp!.Vessels, Is.EquivalentTo(new[] { new ProtoId<VesselPrototype>(vessel) }));
            Assert.That(server.ProtoMan.HasIndex<VesselPrototype>(vessel), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#976: clearing a hosted listing drops the card in its slot instead of deleting it.</summary>
    [Test]
    public async Task ClearingHostedListingKeepsCard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var shipyard = entMan.System<ShipyardSystem>();
            var traders = entMan.System<TraderSystem>();
            var trader = entMan.SpawnEntity("WGTraderShipyard", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            var card = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<TraderComponent>(trader);

            Assert.That(traders.TryHoldItem((trader, comp), card), Is.True);
            Assert.That(shipyard.TryHostConsole(trader, "ComputerShipyard", out _), Is.True);
            Assert.That(shipyard.TryInsertHostedId(trader, card), Is.True);

            shipyard.ClearHostedConsole(trader);
            Assert.That(entMan.Deleted(card), Is.False, "The card must survive the listing being cleared.");

            traders.ReturnHeldItems((trader, comp));
            Assert.That(entMan.GetComponent<TransformComponent>(card).ParentUid, Is.EqualTo(map.Grid.Owner));
        });

        await pair.CleanReturnAsync();
    }
}
