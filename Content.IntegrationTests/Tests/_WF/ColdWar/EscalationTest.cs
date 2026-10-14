#nullable enable
using Content.Server._Mono.AlertLevel;
using Content.Server._Mono.WarDeclarator;
using Content.Server._NF.SectorServices;
using Content.Server._WF.ColdWar;
using Content.Server.GameTicking;
using Content.Shared._Mono.Company;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ColdWar;

/// <summary>
/// Escalation breaks talks down on its timer: the war level goes HOT and the declarators can no longer end it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFEscalationRuleSystem))]
public sealed class EscalationTest
{
    [Test]
    public async Task TalksBreakDownAndStayBroken()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var warLevel = entMan.System<WarLevelSystem>();
        var ticker = entMan.System<GameTicker>();

        EntityUid host = default, portstrike = default, escalation = default, marine = default, tsf = default;
        WFEscalationRuleComponent war = default!;

        await server.WaitAssertion(() =>
        {
            // The war level lives on the sector service entity, which a station's host component brings up.
            host = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationSectorServiceHostComponent>(host);
            portstrike = entMan.SpawnEntity("ManualPortstrikeTsfPdv", MapCoordinates.Nullspace);
            entMan.EnsureComponent<WFColdWarComponent>(portstrike).Cooldown = TimeSpan.Zero;
            marine = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<CompanyComponent>(marine).CompanyName = "TSF";
            tsf = entMan.SpawnEntity("WarDeclaratorTsf", MapCoordinates.Nullspace);

            warLevel.SetLevel(false);
            Assert.That(ticker.StartGameRule("WFEscalationRule", out escalation));
            war = entMan.GetComponent<WFEscalationRuleComponent>(escalation);
            Assert.That(war.BreakAt, Is.EqualTo(timing.CurTime + war.BreakDelay));

            // Five minutes out: the ten-minute warning is due, the one-minute warning and the break are not.
            war.BreakAt = timing.CurTime + TimeSpan.FromMinutes(5);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(war.WarningsSent, Is.EqualTo(1));
            Assert.That(war.Broken, Is.False);
            Assert.That(Hot(), Is.False, "Talks hold until the timer runs out.");

            war.BreakAt = timing.CurTime;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var status = entMan.GetComponent<ManualPortstrikeRuleComponent>(portstrike).SectorStatus;
            Assert.That(war.Broken, Is.True);
            Assert.That(war.WarningsSent, Is.EqualTo(war.Warnings.Count), "The skipped warning is not announced late.");
            Assert.That(Hot(), Is.True, "Talks breaking down sets the war level HOT.");

            var stance = status["TSF"];
            Use();
            Assert.That(status["TSF"], Is.EqualTo(stance), "The declarator refuses a ceasefire offer after the break.");
            Assert.That(Hot(), Is.True);

            // An admin can still set the level; the declarators then work as usual.
            warLevel.SetLevel(false);
            Use();
            Assert.That(status["TSF"], Is.True, "With the level COLD again the declarator declares war.");

            ticker.EndGameRule(escalation);
            warLevel.SetLevel(false);
            foreach (var uid in new[] { escalation, portstrike, marine, tsf, host })
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
        return;

        bool Hot() => warLevel.GetWarLevel(EntityUid.Invalid);

        void Use() => entMan.EventBus.RaiseLocalEvent(tsf, new UseInHandEvent(marine));
    }
}
