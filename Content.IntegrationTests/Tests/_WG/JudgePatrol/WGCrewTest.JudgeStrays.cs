#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>A judge ship lets a captain of any colours land its profile's stray hits; an NPC-crewed vessel gets none.</summary>
public sealed partial class WGCrewTest
{
    [Test]
    public async Task JudgesLetAnyCaptainLandManyStrayHits()
    {
        var deck = await CreateDeck(new Vector2(500, 2900), 5, true);
        var captain = await CreateDeck(new Vector2(600, 2900), 5, true);
        var raider = await CreateDeck(new Vector2(700, 2900), 5, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WGCrewPilot", "judges");
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(deck).Profile = "WGCrewProfileJudge";
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(deck).CompanyName = "WGJudges";
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(captain).CompanyName = "TSF";
            SEntMan.EnsureComponent<WGCrewShipProfileComponent>(raider).Profile = "WGCrewProfileTsf";
        });

        var alerts = Server.System<WGCrewAlertSystem>();
        for (var hit = 0; hit < 40; hit++)
        {
            await Server.WaitPost(() =>
            {
                var ev = new WGCrewHullHitEvent(deck, captain);
                SEntMan.EventBus.RaiseLocalEvent(deck, ref ev, true);
            });
            await RunTicks(Ticks(0.6));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsHostileShip(deck, "judges", captain), Is.False, "Forty strays from a captain of other colours are let pass.");
            var npc = new WGCrewHullHitEvent(deck, raider);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref npc, true);
            Assert.That(alerts.IsHostileShip(deck, "judges", raider), Is.True, "An NPC-crewed vessel's first hit is an attack.");
            var more = new WGCrewHullHitEvent(deck, captain);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref more, true);
            Assert.That(alerts.IsHostileShip(deck, "judges", captain), Is.True, "One more is an attack.");
        });
    }
}
