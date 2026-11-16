#nullable enable
using Content.Server._WF.Operations;
using Content.Server._WF.ShipTiers;
using Content.Shared._Mono.Company;
using Content.Shared.Mind;
using Content.Shared.Store;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipTiers;

/// <summary>
/// The store hands listing conditions the buyer's mind; Wolfgate's conditions must still find the body behind it, or
/// operation listings vanish and per-player cooldowns never apply.
/// </summary>
[TestFixture]
public sealed class WFListingBuyerTest
{
    [Test]
    public async Task ConditionsSeeTheBodyBehindTheMind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var minds = server.System<SharedMindSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<CompanyComponent>(body).CompanyName = "TSF";
            var player = new NetUserId(System.Guid.NewGuid());
            var mind = minds.CreateMind(player);
            minds.TransferTo(mind, body);

            var listing = server.ResolveDependency<IPrototypeManager>().Index<ListingPrototype>("WFUplinkOperationSalvageTsf");
            var args = new ListingConditionArgs(mind, null, listing, entities);

            Assert.Multiple(() =>
            {
                Assert.That(WFListingBuyer.Body(args), Is.EqualTo(body), "A mind buyer should resolve to its body.");
                Assert.That(WFListingBuyer.Player(entities, mind), Is.EqualTo(player));
                Assert.That(new WFOperationReadyCondition { Operation = "WFOperationSalvageTsf" }.Condition(args), Is.True,
                    "A TSF member's mind was refused an operation any member may call.");
            });

            entities.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }
}
