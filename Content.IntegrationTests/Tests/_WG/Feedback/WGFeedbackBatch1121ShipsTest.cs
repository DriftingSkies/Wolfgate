#nullable enable
using Content.IntegrationTests.Tests._WG.Shipyard;
using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Ship reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121ShipsTest
{
    /// <summary>#1173: a voucher with no redemptions left is refused without starting its cooldown; a sale that goes through starts it.</summary>
    [Test]
    public async Task RefusedVoucherStartsNoCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var shipyard = entMan.System<ShipyardSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var console = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var voucherUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            (console, customer, _) = IronmanShipPurchaseTest.SpawnShipyard(entMan, map, session);
            var slots = entMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(console, ShipyardConsoleComponent.TargetIdCardSlotId, null, out var card), Is.True);
            entMan.DeleteEntity(card!.Value);
            voucherUid = entMan.SpawnEntity("ShipVoucherFrontierIronMan", map.GridCoords);
            Assert.That(slots.TryInsert(console, ShipyardConsoleComponent.TargetIdCardSlotId, voucherUid, null), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<BankAccountComponent>(customer);
            var voucher = entMan.GetComponent<ShipyardVoucherComponent>(voucherUid);
            voucher.RedemptionsLeft = 0;
            voucher.DestroyOnEmpty = false;
            var before = voucher.NextBuyAt;
            IronmanShipPurchaseTest.OpenListing(entMan, console, customer);

            shipyard.LastConsolePopup = null;
            IronmanShipPurchaseTest.Purchase(entMan, console, customer);
            Assert.That(shipyard.LastConsolePopup, Is.EqualTo(loc.GetString("shipyard-console-no-voucher-redemptions")));
            Assert.That(voucher.NextBuyAt, Is.EqualTo(before), "A refused redemption must not start the cooldown.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(voucherUid), Is.False);

            voucher.RedemptionsLeft = 1;
            IronmanShipPurchaseTest.Purchase(entMan, console, customer);
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(voucherUid), Is.True,
                $"The voucher should buy the ship, but the console said: {shipyard.LastConsolePopup}");
            Assert.That(voucher.RedemptionsLeft, Is.EqualTo(0u));
            Assert.That(voucher.NextBuyAt, Is.GreaterThan(timing.CurTime), "A redemption that goes through starts the cooldown.");
        });

        await pair.CleanReturnAsync();
    }
}
