using Content.Server._WF.RoundSetup;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class ShipScenarioTest
{
    /// <summary>An expedition ship started from the lobby gets offers on its console like a bought one.</summary>
    [Test]
    public async Task ExpeditionShipStartGetsOffers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var system = server.System<WFShipScenarioSystem>();
        await StartEmptyRound(pair, initializeServices: false);

        EntityUid station = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(19140, "Sagittarius", out station, out var reason), Is.True, reason);
            Assert.That(server.EntMan.HasComponent<SalvageExpeditionDataComponent>(station), "The start has no expedition data.");
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(station).EntityName, Does.Match(@"^HT Sagittarius EXP\S+$"),
                "The start gets the numbered name a bought ship has.");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<SalvageExpeditionDataComponent>(station).Missions, Is.Not.Empty,
                "The start made no expedition offers.");
            system.Rollback(19140);
        });
        await ReturnToLobby(pair);
        await pair.CleanReturnAsync();
    }
}
