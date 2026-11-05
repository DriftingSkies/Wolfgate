using Content.Client._WF.Defcon;
using Content.Server._WF.Defcon;
using Content.Server.EUI;
using Content.Shared._WF.Defcon;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Defcon;

/// <summary>The DEFCON scores the threat, rises after confirmation, announces and honours an admin lock.</summary>
[TestFixture]
public sealed class WFDefconTest
{
    [Test]
    public async Task BiasRaisesLockHolds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var defcon = server.System<WFDefconSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(defcon.Profile(), Is.Not.Null);
            var now = timing.CurTime;

            defcon.Bias = 65f;
            Assert.That(defcon.Evaluate(now), Is.False);
            Assert.That(defcon.Evaluate(now + TimeSpan.FromSeconds(61)), Is.True);
            Assert.That(defcon.Level, Is.EqualTo(2));
            Assert.That(defcon.Score, Is.GreaterThanOrEqualTo(65f));
            Assert.That(defcon.PdaLine(), Does.Contain("2"));

            defcon.SetLevel(4, locked: true);
            Assert.That(defcon.Evaluate(now + TimeSpan.FromMinutes(2)), Is.False);
            Assert.That(defcon.Level, Is.EqualTo(4));

            defcon.Unlock();
            defcon.Bias = 0f;
            defcon.SetLevel(5, locked: false, announce: false);
            Assert.That(defcon.Level, Is.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PanelOpensAndShowsState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var defcon = pair.Server.System<WFDefconSystem>();
        WFDefconEuiState state = default!;
        Content.Server._WF.Defcon.WFDefconEui eui = default!;

        await pair.Server.WaitAssertion(() =>
        {
            defcon.Bias = 12f;
            defcon.Evaluate(pair.Server.ResolveDependency<IGameTiming>().CurTime);
            state = defcon.PanelState();
            Assert.That(state.Levels, Has.Count.EqualTo(5));
            Assert.That(state.Reasons, Has.Count.GreaterThanOrEqualTo(1));

            eui = new Content.Server._WF.Defcon.WFDefconEui();
            pair.Server.ResolveDependency<EuiManager>().OpenEui(eui, pair.Player!);
            Assert.That(defcon.Panels, Has.Count.EqualTo(1));
        });

        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            var window = new WFDefconWindow();
            window.OpenCentered();
            window.UpdateState(state);
            Assert.That(window.LevelButtons.ChildCount, Is.EqualTo(5));
            Assert.That(window.ReasonList.ChildCount, Is.EqualTo(state.Reasons.Count));
            window.Close();
        });

        await pair.Server.WaitAssertion(() =>
        {
            defcon.Bias = 0f;
            pair.Server.ResolveDependency<EuiManager>().CloseEui(eui);
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() => Assert.That(defcon.Panels, Is.Empty));

        await pair.CleanReturnAsync();
    }
}
