#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// A vessel of no faction fighting one side of a battle is that side's enemy from its first hit, but to the other
    /// side it is a friend whose stray rounds are forgiven, until it has hit them more than half as often as their foe.
    /// </summary>
    [Test]
    public async Task StrayRoundsFromAVesselFightingYourFightAreForgiven()
    {
        EntityUid encounter = default, player = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryMelee");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-47000, -47000), MapData.MapId), out encounter), Is.True);
            var ships = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships;

            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            player = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(player, new EntityCoordinates(MapData.MapUid, new Vector2(-46400, -47000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(player, new Vector2(0.5f)));

            var alerts = Server.System<WFCrewAlertSystem>();
            var one = ships["one"];
            var two = ships["two"];
            for (var i = 0; i < 6; i++)
                Hit(two.Grid, player);
            Assert.That(alerts.IsAttacker(two.Grid, player), Is.True, "A stranger's fire is an attack to the side it falls on.");

            Hit(one.Grid, player);
            Hit(one.Grid, player);
            Assert.That(alerts.IsAttacker(one.Grid, player), Is.False, "Two strays from a vessel that has hit their enemy six times are forgiven.");

            Hit(one.Grid, player);
            Assert.That(alerts.IsAttacker(one.Grid, player), Is.False, "Three, at twice as many on the enemy, still are.");

            Hit(one.Grid, player);
            Assert.That(alerts.IsAttacker(one.Grid, player), Is.True, "Four is more than half its fire on the enemy: an attack.");

            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(player);
        });
        await RunTicks(10);
    }

    private void Hit(EntityUid grid, EntityUid attacker)
    {
        var hit = new WFCrewHullHitEvent(grid, attacker);
        SEntMan.EventBus.RaiseLocalEvent(grid, ref hit, true);
    }
}
