#nullable enable
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Roles;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Shipyard;

/// <summary>Buying a ship leaves the card's job title alone, and keeps a title its holder chose.</summary>
[TestFixture]
[TestOf(typeof(ShipyardSystem))]
public sealed class ShipPurchaseTitleTest
{
    private const string VesselProto = "Guppy";
    private const string RankTitle = "Quartermaster";

    [TestCase(false, "Passenger")]
    [TestCase(true, RankTitle)]
    public async Task PurchaseTitlesCard(bool customTitle, string expected)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var console = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var idCard = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            (console, customer, idCard) = IronmanShipPurchaseTest.SpawnShipyard(entMan, map, session);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var price = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(VesselProto).Price;
            var bank = entMan.EnsureComponent<BankAccountComponent>(customer);
            if (bank.Balance < price)
                Assert.That(entMan.System<BankSystem>().TryBankDeposit(customer, price, tax: false), Is.True, "Could not fund the account.");

            if (customTitle)
            {
                Assert.That(entMan.System<SharedMindSystem>().TryGetMind(customer, out var mind, out _), Is.True);
                var custom = entMan.EnsureComponent<CustomJobTitleComponent>(mind);
                custom.Job = "Contractor";
                custom.Title = RankTitle;
                entMan.System<SharedIdCardSystem>().TryChangeJobTitle(idCard, RankTitle);
            }

            IronmanShipPurchaseTest.OpenListing(entMan, console, customer);
            IronmanShipPurchaseTest.Purchase(entMan, console, customer);

            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.True, "The sale should have gone through.");
            Assert.That(entMan.GetComponent<IdCardComponent>(idCard).LocalizedJobTitle, Is.EqualTo(expected));
        });

        await pair.CleanReturnAsync();
    }
}
