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
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>A hub that goes dark with nobody attacking it is not broken, within a grace; one that is shot at and goes dark, or has its guns torn out, is.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverStarvedTest : WFReaverTestBase
{
    /// <summary>A ship gun that takes mains power.</summary>
    private const string PoweredGun = "ShuttleGunKinetic";

    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    private EntityUid Lead(EntityUid encounter) => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"].Grid;

    /// <summary>The ship weapons aboard a hull, after the status system has seen it armed.</summary>
    private async Task<List<EntityUid>> Guns(EntityUid grid)
    {
        await RunTicks(Ticks(2));
        var weapons = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewShipStatusSystem>().IsDisabled(grid), Is.False, "Armed, it is judged and passes.");
            var query = SEntMan.EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    weapons.Add(uid);
            }

            Assert.That(weapons, Is.Not.Empty, "The hull has guns to lose.");
        });
        return weapons;
    }

    /// <summary>
    /// Swaps every gun aboard for one that takes mains power, spawned with none, so the hull counts as out of power
    /// once the delay passes: its guns are there, dead for want of power.
    /// </summary>
    private async Task Starve(EntityUid grid)
    {
        var weapons = await Guns(grid);
        await Server.WaitAssertion(() =>
        {
            var power = Server.System<PowerReceiverSystem>();
            foreach (var uid in weapons)
            {
                var coordinates = SEntMan.GetComponent<TransformComponent>(uid).Coordinates;
                SEntMan.DeleteEntity(uid);
                // Switched off at the breaker, so the hub's own mains never bring it up.
                var gun = SEntMan.SpawnEntity(PoweredGun, coordinates);
                power.TogglePower(gun, false);
                Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(gun).Powered, Is.False, "Spawned with no power.");
            }
        });
        var status = Server.System<WFCrewShipStatusSystem>();
        await WaitUntilServer(() => status.IsStarved(grid), Ticks(30));
    }

    /// <summary>Tears every gun out of a hull, as a boarding party with tools would, leaving no power to restore.</summary>
    private async Task TearOutGuns(EntityUid grid)
    {
        var weapons = await Guns(grid);
        await Server.WaitAssertion(() =>
        {
            foreach (var uid in weapons)
                SEntMan.DeleteEntity(uid);
        });
        await WaitUntilServer(() => Server.System<WFCrewShipStatusSystem>().IsDisabled(grid), Ticks(30));
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

    /// <summary>Guns torn out leave nothing to power back up: the hub is beaten, no ship hit needed.</summary>
    [Test]
    public async Task HubWithItsGunsTornOutIsBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await TearOutGuns(Lead(stronghold));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewShipStatusSystem>().IsStarved(Lead(stronghold)), Is.False, "Nothing unpowered is left aboard.");
            Server.System<WFReaverCampaignSystem>().CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
        });
    }

    /// <summary>A dark thruster does not explain guns that are gone: the hub is beaten all the same.</summary>
    [Test]
    public async Task HubWithItsGunsTornOutAndAThrusterDarkIsBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        var lead = Lead(stronghold);
        var weapons = await Guns(lead);
        await Server.WaitAssertion(() =>
        {
            var power = Server.System<PowerReceiverSystem>();
            EntityUid? thruster = null;
            var query = SEntMan.EntityQueryEnumerator<ThrusterComponent, ApcPowerReceiverComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out _, out var xform))
            {
                if (xform.GridUid == lead)
                {
                    thruster = uid;
                    break;
                }
            }

            Assert.That(thruster, Is.Not.Null, "The hull has a thruster that takes power.");
            power.TogglePower(thruster!.Value, false);
            foreach (var uid in weapons)
                SEntMan.DeleteEntity(uid);
        });
        var status = Server.System<WFCrewShipStatusSystem>();
        await WaitUntilServer(() => status.IsDisabled(lead), Ticks(30));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            Assert.That(status.IsStarved(lead), Is.False, "The dark thruster does not explain the missing guns.");
            Server.System<WFReaverCampaignSystem>().CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
        });
    }

    /// <summary>A hub still dark once the grace runs out is broken like any other.</summary>
    [Test]
    public async Task HubDarkPastTheGraceIsBroken()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await Starve(Lead(stronghold));
        await RunTicks(Ticks(5));

        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.CheckStrongholds();
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(comp.Starved, Is.True, "Left standing for now.");
            comp.StarvedSince -= WFReaverCampaignSystem.StarvedGrace;
            campaign.CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
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
