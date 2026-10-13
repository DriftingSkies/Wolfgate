#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._Mono.FireControl;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>A hub that goes dark with nobody attacking it is not broken; one that is shot at and goes dark is.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverStarvedTest : WFReaverTestBase
{
    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    private EntityUid Lead(EntityUid encounter) => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"].Grid;

    /// <summary>Takes the guns off a hull after the status system has seen them, so it counts as out of power once the delay passes.</summary>
    private async Task Starve(EntityUid grid)
    {
        await RunTicks(Ticks(2));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewShipStatusSystem>().IsDisabled(grid), Is.False, "Armed, it is judged and passes.");
            var weapons = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    weapons.Add(uid);
            }

            Assert.That(weapons, Is.Not.Empty, "The hull has guns to lose.");
            foreach (var uid in weapons)
                SEntMan.DeleteEntity(uid);
        });
        await WaitUntilServer(() => Server.System<WFCrewShipStatusSystem>().IsStarved(grid), Ticks(30));
    }

    /// <summary>No hits on the hub: out of power it is left standing and flagged.</summary>
    [Test]
    public async Task HubOutOfPowerWithNoHitsIsNotBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await Starve(Lead(stronghold));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(stronghold);
            Assert.That(Server.System<WFEncounterSystem>().InFight(encounter.Ships["lead"]), Is.False, "It is out of the fight.");
            campaign.CheckStrongholds();
            Assert.That(encounter.Resolution, Is.Null, "But nobody has attacked it.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Starved, Is.True);
            Assert.That(campaign.State()!.Broken, Is.Zero);
        });
    }

    /// <summary>A hub that has been shot at and goes dark is broken as before.</summary>
    [Test]
    public async Task HubShotAtAndOutOfPowerIsBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            ShootAt(stronghold, "lead", ship);
        });
        await Starve(Lead(stronghold));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
        });
    }
}
