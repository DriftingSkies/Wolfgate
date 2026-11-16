using System.Linq;
using Content.Server._WF.Ordnance;
using Content.Server._WF.Warheads;
using Content.Server._WF.Warheads.Components;
using Content.Shared._WF.Warheads;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Ordnance;

/// <summary>The faction warhead counts down for at least 90 s and sounds its klaxon only while armed.</summary>
public sealed class WFOrdnanceTimerKlaxonTest
{
    private const string Warhead = "WFWarheadTacticalKeyed";

    [Test]
    public async Task TimersStartAtNinetySeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var warhead = entMan.SpawnEntity(Warhead, map.GridCoords);
            var beacon = entMan.SpawnEntity("WFOrdnanceBeaconTactical", map.GridCoords);

            Assert.That(entMan.GetComponent<WFTimerFuzeComponent>(warhead).Options.Min(), Is.GreaterThanOrEqualTo(90f));
            Assert.That(entMan.GetComponent<WFRemoteFuzeComponent>(warhead).SignalTimer, Is.GreaterThanOrEqualTo(90f));
            Assert.That(entMan.GetComponent<WFOrdnanceBeaconComponent>(beacon).TimerOptions.Min(), Is.GreaterThanOrEqualTo(90f));

            // The panel's nudge buttons can't take it under 90 s either.
            entMan.EventBus.RaiseLocalEvent(warhead, new WFWarheadSetTimerMessage(30f));
            Assert.That(entMan.GetComponent<WFWarheadComponent>(warhead).Timer, Is.EqualTo(90f),
                "the control panel set the timer under 90 s.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KlaxonSoundsOnlyWhileArmed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WFWarheadSystem>();

        var uid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(Warhead, map.GridCoords);
            warheads.StartCountdown((uid, entMan.GetComponent<WFWarheadComponent>(uid)), null);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFOrdnanceKlaxonComponent>(uid).Stream, Is.Not.Null, "an armed warhead is silent."));

        await server.WaitPost(() => warheads.Disarm((uid, entMan.GetComponent<WFWarheadComponent>(uid)), null));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFOrdnanceKlaxonComponent>(uid).Stream, Is.Null, "a safe warhead kept sounding."));

        await pair.CleanReturnAsync();
    }
}
