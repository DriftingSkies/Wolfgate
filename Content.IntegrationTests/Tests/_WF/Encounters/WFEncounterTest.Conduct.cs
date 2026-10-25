#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>
/// Traders run from attackers; any crew that does not already fight strangers tells a player with a gun out to put it
/// away and turns on him after ten seconds; and a player seen taking the ship's things is fought at once.
/// </summary>
public sealed partial class WFEncounterTest
{
    /// <summary>Starts the test trader far from everything and returns its encounter and ship.</summary>
    private async Task<(EntityUid Encounter, WFEncounterShipState Ship)> StartTestTrader(Vector2 at)
    {
        EntityUid encounter = default;
        WFEncounterShipState ship = default!;
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestTraderUnderWay");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(proto, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["trader"];
        });
        await RunTicks(5);
        return (encounter, ship);
    }

    /// <summary>A living crewman of the ship in the given duty, or any fighter.</summary>
    private EntityUid CrewOf(WFEncounterShipState ship, string? duty = null)
    {
        var query = SEntMan.EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == ship.Grid
                && (duty == null ? WFCrewSystem.IsFighter(member) : member.Duty == duty))
                return uid;
        }

        Assert.Fail($"No crewman with duty {duty ?? "fighter"} aboard.");
        return default;
    }

    private bool Remembers(EntityUid crewman, EntityUid attacker)
    {
        if (!SEntMan.TryGetComponent<NPCRetaliationComponent>(crewman, out var retaliation))
            return false;

        var memories = retaliation.AttackMemories;
        return memories.ContainsKey(attacker);
    }

    [Test]
    public async Task AttackedTraderRunsFromItsAttacker()
    {
        var at = new Vector2(-23000f, 23000f);
        var (encounter, ship) = await StartTestTrader(at);
        EntityUid attacker = default;
        await Server.WaitAssertion(() =>
        {
            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            attacker = grid.Owner;
            Server.System<SharedTransformSystem>().SetCoordinates(attacker, new EntityCoordinates(MapData.MapUid, at + new Vector2(-300f, 0f)));

            var pilot = CrewOf(ship, WFCrewDuties.Pilot);
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilot).FleeAttackers, Is.True, "A trader's pilot runs.");
            Server.System<WFCrewAlertSystem>().ReportShipThreat(ship.Grid, ship.Group, attacker);
        });

        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(CrewOf(ship, WFCrewDuties.Pilot));
            Assert.That(duty.ResumeOrder ?? duty.Orders, Is.EqualTo(WFPilotOrder.GoTo), "It runs instead of circling the attacker.");
            var transform = Server.System<SharedTransformSystem>();
            var goal = transform.ToMapCoordinates(duty.ResumeOrder != null ? duty.ResumeWaypoints.Last() : duty.Waypoints.Last()).Position;
            var here = transform.GetWorldPosition(ship.Grid);
            var away = here - transform.GetWorldPosition(attacker);
            Assert.That(Vector2.Dot(Vector2.Normalize(goal - here), Vector2.Normalize(away)), Is.GreaterThan(0.9f),
                "The run heads directly away from the attacker.");

            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(attacker);
        });
        await RunTicks(10);
    }

    [Test]
    public async Task CrewTurnOnAPlayerWhoKeepsAGunOut()
    {
        var (encounter, ship) = await StartTestTrader(new Vector2(-26000f, 26000f));
        EntityUid gun = default;
        EntityUid guard = default;
        await Server.WaitAssertion(() =>
        {
            guard = CrewOf(ship);
            // A player's body is a mob; the bare test body is not.
            SEntMan.EnsureComponent<MobStateComponent>(SPlayer);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(guard).Coordinates);
            gun = SEntMan.SpawnEntity("WeaponPistolViper", SEntMan.GetComponent<TransformComponent>(guard).Coordinates);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, gun), Is.True);
        });

        var conduct = Server.System<WFCrewConductSystem>();
        await WaitUntilServer(() => conduct.IsWarned(ship.Grid, ship.Group, SPlayer), 120);

        // Put away in time: nothing comes of it.
        await Server.WaitAssertion(() => Server.System<SharedHandsSystem>().TryDrop(SPlayer, gun));
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(conduct.IsWarned(ship.Grid, ship.Group, SPlayer), Is.False);
            Assert.That(Remembers(guard, SPlayer), Is.False);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, gun), Is.True);
        });

        // Kept out past the warning: the crew turn on the player.
        await WaitUntilServer(() => Remembers(guard, SPlayer), (int) (WFCrewConductSystem.BrandishTime.TotalSeconds + 5) * 30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.DeleteEntity(gun);
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    [Test]
    public async Task CrewTurnOnAPlayerSeenTakingTheShipsThings()
    {
        var (encounter, ship) = await StartTestTrader(new Vector2(-29000f, 29000f));
        EntityUid guard = default;
        EntityUid own = default;
        EntityUid item = default;
        await Server.WaitAssertion(() =>
        {
            guard = CrewOf(ship);
            SEntMan.EnsureComponent<MobStateComponent>(SPlayer);
            var coordinates = SEntMan.GetComponent<TransformComponent>(guard).Coordinates;
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, coordinates);
            own = SEntMan.SpawnEntity("WeaponPistolViper", coordinates);

            var containers = Server.System<SharedContainerSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var query = SEntMan.EntityQueryEnumerator<WFCrewPropertyComponent>();
            while (query.MoveNext(out var uid, out var property))
            {
                // the first match must be something a hand can take, not a crew-marked structure or an anchored item
                if (property.Grid != ship.Grid || containers.IsEntityInContainer(uid) || !hands.CanPickupAnyHand(SPlayer, uid))
                    continue;

                item = uid;
                break;
            }

            Assert.That(item, Is.Not.EqualTo(EntityUid.Invalid), "The ship's things are marked as its crew's.");
            Server.System<SharedTransformSystem>().SetCoordinates(item, coordinates);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var hands = Server.System<SharedHandsSystem>();
            // Something of the player's own, dropped aboard, is not the ship's.
            Assert.That(hands.TryPickupAnyHand(SPlayer, own), Is.True);
            Assert.That(Remembers(guard, SPlayer), Is.False);
            SEntMan.DeleteEntity(own);

            Assert.That(hands.TryPickupAnyHand(SPlayer, item), Is.True);
            Assert.That(Remembers(guard, SPlayer), Is.True, "Seen taking it, the player is fought.");
            SEntMan.DeleteEntity(item);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
