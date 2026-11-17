#nullable enable
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestStoryRivalProto => "WGTestStoryRival";

    /// <summary>
    /// An encounter ship's captain drops its deed when he dies; used, it cuts the ship loose from its encounter, which
    /// then never removes it, even when ended.
    /// </summary>
    [Test]
    public async Task CaptainsDeedMakesHisShipPermanent()
    {
        EntityUid encounter = default, ship = default, captain = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryRivalProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-45000, -45000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["rival"].Grid;

            var carriers = 0;
            var query = SEntMan.EntityQueryEnumerator<WGEncounterDeedDropComponent>();
            while (query.MoveNext(out var uid, out var drop))
            {
                if (drop.Ship != ship)
                    continue;
                carriers++;
                captain = uid;
            }

            Assert.That(carriers, Is.EqualTo(1), "One crewman carries the deed.");
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(captain).Role, Is.EqualTo(WGCrewRoles.Captain), "The captain carries it.");
            Server.System<MobStateSystem>().ChangeMobState(captain, MobState.Dead);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            EntityUid? deed = null;
            var query = SEntMan.EntityQueryEnumerator<WGEncounterDeedComponent>();
            while (query.MoveNext(out var uid, out var held))
            {
                if (held.Ship == ship)
                    deed = uid;
            }

            Assert.That(deed, Is.Not.Null, "The captain dropped the deed.");
            var use = new UseInHandEvent(SEntMan.GetEntity(Player));
            SEntMan.EventBus.RaiseLocalEvent(deed!.Value, use);
            Assert.That(use.Handled, Is.True);

            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            Assert.That(comp.Ships.ContainsKey("rival"), Is.False, "The encounter lets the ship go.");
            Assert.That(comp.Resolution, Is.EqualTo(WGEncounterResolution.Completed), "With its only ship gone, the encounter is over.");
            Assert.That(SEntMan.HasComponent<WGEncounterGridComponent>(ship), Is.False, "The ship belongs to no encounter.");
            Assert.That(SEntMan.HasComponent<CleanupImmuneComponent>(ship), Is.True, "No cleanup takes it.");
            var user = SEntMan.GetComponent<ActorComponent>(SEntMan.GetEntity(Player)).PlayerSession.UserId;
            Assert.That(SEntMan.TryGetComponent<WGShipAccessComponent>(ship, out var access) && access.OwnerUsers.Contains(user),
                Is.True, "The claimant owns the ship's access.");

            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.True, "Ending the encounter leaves the ship.");
            SEntMan.DeleteEntity(ship);
        });
    }
}
