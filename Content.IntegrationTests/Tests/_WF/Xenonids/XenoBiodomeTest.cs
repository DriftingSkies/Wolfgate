#nullable enable
using Content.Shared._Mono.Fixtures;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The outpost biodome shield burns xenos the way it burns chimeras.</summary>
[TestFixture]
public sealed class XenoBiodomeTest
{
    [Test]
    public async Task BiodomeTargetsXenos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var whitelist = entities.System<EntityWhitelistSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dome = entities.SpawnEntity("ShieldGeneratorPOIBio", map.GridCoords);
            var dealer = entities.GetComponent<FixtureDamageDealerComponent>(dome);
            var drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(whitelist.IsWhitelistPass(dealer.Whitelist, drone), "The biodome's field burns a xeno.");
            Assert.That(whitelist.IsWhitelistPass(dealer.Whitelist, human), Is.False, "It leaves people alone.");
        });

        await pair.CleanReturnAsync();
    }
}
