#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.KillTracking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>A resolved ship due to jump out stays for the grace period after the last player steps off it.</summary>
    [Test]
    public async Task ResolvedShipWaitsOutTheGraceAfterPlayersLeave()
    {
        EntityUid encounter = default, ship = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitAssertion(() =>
        {
            config.SetCVar(EncountersCVars.CleanupGrace, 12f);
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-47000, 47000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            ship = comp.Ships["lead"].Grid;
            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Completed);
            comp.JumpAt = Server.ResolveDependency<IGameTiming>().CurTime;
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(ship, Vector2.Zero));
        });
        // A poll sees the player aboard.
        await RunSeconds(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.True, "The ship waits while a player is aboard.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
        });
        await RunSeconds(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship) && !SEntMan.IsQueuedForDeletion(ship), Is.True, "The ship is not removed the moment the player steps off.");
        });
        await RunSeconds(16);
        await Server.WaitAssertion(() =>
        {
            Assert.That(!SEntMan.EntityExists(ship) || SEntMan.IsQueuedForDeletion(ship), Is.True, "Past the grace it jumps out.");
            if (SEntMan.EntityExists(encounter))
                Server.System<WFEncounterSystem>().End(encounter);
            config.SetCVar(EncountersCVars.CleanupGrace, EncountersCVars.CleanupGrace.DefaultValue);
        });
        await RunTicks(10);
    }

    /// <summary>A player who downs a side's crewman in person shares that side's defeat like one whose ship hit it.</summary>
    [Test]
    public async Task DowningACrewmanCountsTowardTheReward()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterSkirmish");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-49000, 49000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var foe = comp.Ships["foe"];
            var friend = comp.Ships["friend"];
            var crewman = SEntMan.EntityQuery<WFCrewComponent>(true).First(member => member.Group == foe.Group).Owner;
            Assert.That(SEntMan.HasComponent<KillTrackerComponent>(crewman), Is.True, "Crews report who downs them.");

            var rewards = Server.System<WFEncounterRewardSystem>();
            Assert.That(rewards.GetHelpers(comp, new HashSet<string> { "bad" }), Is.Empty, "Nobody has helped yet.");

            var ev = new KillReportedEvent(crewman, new KillPlayerSource(ServerSession.UserId), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(comp.Kills[ServerSession.UserId][foe.Side], Is.EqualTo(1));
            Assert.That(rewards.GetHelpers(comp, new HashSet<string> { foe.Side }).Select(helper => helper.Session),
                Does.Contain(ServerSession), "Downing one of theirs is help against them.");

            var other = SEntMan.EntityQuery<WFCrewComponent>(true).First(member => member.Group == friend.Group).Owner;
            var turn = new KillReportedEvent(other, new KillPlayerSource(ServerSession.UserId), null, false);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref turn);
            Assert.That(rewards.GetHelpers(comp, new HashSet<string> { foe.Side }), Is.Empty, "Downing the other side's crew too forfeits the share.");

            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
