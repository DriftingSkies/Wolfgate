using System.Linq;
using Content.Server._WF.Fragments;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server.GameTicking;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Fragments;

/// <summary>A fragment arrives with its heart and Remade and no Monolith, clears when the heart breaks and leaves with the rule.</summary>
[TestFixture]
public sealed class WFFragmentRuleTest
{
    [Test]
    public async Task FragmentHoldsAHeartAndNoScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        EntityUid rule = default;

        await server.WaitPost(() => Assert.That(ticker.StartGameRule("WFFragmentEvent", out rule)));
        await pair.RunSeconds(45f);

        EntityUid grid = default;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFFragmentRuleComponent>(rule);
            var maps = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            Assert.That(maps.MapExists(ticker.DefaultMap), $"no default map ({ticker.DefaultMap}); run level {ticker.RunLevel}");
            Assert.That(ticker.IsGameRuleActive(rule), "rule not active");
            Assert.That(comp.Grid, Is.Not.Null, "the fragment never arrived.");
            grid = comp.Grid!.Value;
            Assert.That(comp.HeartUid is { } heart && entMan.GetComponent<TransformComponent>(heart).GridUid == grid,
                "the heart isn't on the fragment.");
            Assert.That(entMan.EntityQuery<WFMonolithComponent>(), Is.Empty, "a fragment holds a Monolith.");
            Assert.That(comp.Remade.All(entMan.HasComponent<WFRemadeComponent>), Is.True);

            entMan.DeleteEntity(comp.HeartUid!.Value);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFFragmentRuleComponent>(rule).Cleared, Is.True, "breaking the heart didn't clear it.");
            ticker.EndGameRule(rule);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() => Assert.That(entMan.Deleted(grid) || entMan.IsQueuedForDeletion(grid), "the fragment outlived its rule."));
        await pair.CleanReturnAsync();
    }
}
