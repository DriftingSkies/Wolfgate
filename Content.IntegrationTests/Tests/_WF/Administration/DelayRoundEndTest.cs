#nullable enable
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// delayroundend keeps the round running through a shuttle countdown and stops the auto-call.
/// </summary>
[TestOf(typeof(EmergencyShuttleSystem))]
public sealed class DelayRoundEndTest
{
    [Test]
    public async Task DelayedRoundSurvivesCountdown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            Dirty = true,
        });

        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var roundEnd = server.System<RoundEndSystem>();
        var shuttle = server.System<EmergencyShuttleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.EmergencyShuttleEnabled), Is.False);
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

            Assert.That(shuttle.DelayEmergencyRoundEnd(), Is.True);
            Assert.That(shuttle.DelayEmergencyRoundEnd(), Is.False, "A second delay should report it is already delayed");

            roundEnd.RequestRoundEnd(TimeSpan.FromMilliseconds(100), checkCooldown: false);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "The countdown ended the delayed round");

            roundEnd.CancelRoundEndCountdown(checkCooldown: false);
            roundEnd.AutoCallStartTime = server.Timing.CurTime - TimeSpan.FromDays(30);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(roundEnd.ExpectedCountdownEnd, Is.Null, "The auto-call fired while the round was delayed");
            ticker.RestartRound();
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(shuttle.RoundEndDelayed, Is.False, "The delay outlived the round"));

        await pair.CleanReturnAsync();
    }
}
