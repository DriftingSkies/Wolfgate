#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Atmos.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Atmos.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>
    /// A hull with no air is still crewed: the helm gets its pilot, and a crewman whose loadout has no suit is put in
    /// one, with a tank that fills itself again.
    /// </summary>
    [Test]
    public async Task AirlessHullIsCrewedInVoidKits()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        EntityUid pilot = default;
        EntityUid tank = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f, 5.5f)));
            var post = Server.System<WFCrewPlannerSystem>().Plan(deck, 1).FirstOrDefault(post => post.Kind == WFCrewPostKind.Helm);
            Assert.That(post.Role.Id, Is.EqualTo(WFCrewRoles.Pilot.Id), "An airless hull should still plan a pilot beside its helm.");

            pilot = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Pilot, post.Coordinates, "void", "WFCrewHaulerGuardSidearm")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            Assert.That(SEntMan.HasComponent<WFCrewVoidKitComponent>(pilot), Is.True);
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(pilot, "outerClothing", out var suit)
                && SEntMan.GetComponent<PressureProtectionComponent>(suit.Value).LowPressureMultiplier >= 100, Is.True,
                "The loadout's armour should have been swapped for a pressure suit.");
            Assert.That(inventory.TryGetSlotEntity(pilot, "suitstorage", out var worn), Is.True);
            tank = worn!.Value;
            Assert.That(SEntMan.GetComponent<GasTankComponent>(tank).IsConnected, Is.True, "He should spawn on internals.");
#pragma warning disable RA0002 // Drain the tank the way an hour of breathing would.
            SEntMan.GetComponent<GasTankComponent>(tank).Air.Clear();
#pragma warning restore RA0002
        });
        await WaitUntil(() => SEntMan.GetComponent<GasTankComponent>(tank).Air.Pressure > 800f, 120,
            () => "A void kit's tank should be topped up while its crewman wears it.");
        await RunTicks(600);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MobStateSystem>().IsAlive(pilot), Is.True, "A crewman in a void kit should live in vacuum.");
            Assert.That(SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(pilot).TotalDamage.Float(), Is.LessThan(5f));
        });
    }

    /// <summary>
    /// A hull whose guns can only fire past one side presents that side: the heading the pilot holds turns it along
    /// the line to the target. Guns that bear all round ask for no stance.
    /// </summary>
    [Test]
    public async Task AttackingShipTurnsItsGunSideToTheTarget()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var open = await CreateDeck(new Vector2(40, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            // A wall right across the deck north of the guns: they fire south, east and west only.
            for (var x = 0; x < 7; x++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(x + 0.5f, 4.5f)));
            SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)));
            SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(4.5f, 3.5f)));
            SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(open, new Vector2(3.5f, 3.5f)));
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var pilots = Server.System<WFPilotDutySystem>();
            Assert.That(pilots.GunBearings(open), Is.Empty, "A gun with nothing in its way bears equally all round.");
            Assert.That(pilots.GunHeading(open, new Vector2(100f, 0f)), Is.Null);

            var bearings = pilots.GunBearings(deck);
            Assert.That(bearings, Is.Not.Empty);
            foreach (var bearing in bearings)
                Assert.That((bearing + new Angle(Math.PI)).ToWorldVec().Y, Is.LessThan(-0.3f), "The walled-off side is not the gun side.");

            // Turned to the heading, as the steering turns a hull, the grid's open southern arc should look at the target.
            foreach (var toTarget in new[] { new Vector2(100f, 0f), new Vector2(0f, 100f), new Vector2(-70f, -70f) })
            {
                var heading = pilots.GunHeading(deck, toTarget)!.Value;
                var rotation = heading - new Angle(Math.PI);
                var gunSide = rotation.RotateVec(new Vector2(0f, -1f));
                Assert.That(Vector2.Dot(gunSide, Vector2.Normalize(toTarget)), Is.GreaterThan(0.3f),
                    $"The gun side should face a target at {toTarget}.");
            }
        });
    }
}
