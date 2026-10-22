#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Emberfall.Weapons.Ranged;
using Content.Shared._WF.Fighters;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.CombatMode;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using ClientGunSystem = Content.Client.Weapons.Ranged.Systems.GunSystem;

namespace Content.IntegrationTests.Tests._WF.Fighters;

/// <summary>
/// The pilot's client fires the fighter's nose gun as a predicted shot, and the round it draws carries a tracer.
/// </summary>
public sealed class FighterGunTest : InteractionTest
{
    protected override string PlayerPrototype => "WFFighterTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFFighterTestMob
  parent: InteractionTestMob
  components:
  - type: CombatMode

- type: entity
  id: WFFighterTestBroadsword
  parent: WFFighterBroadsword
  components:
  - type: Fighter
    canFireOnGrid: true
";

    [Test]
    public async Task PilotFiresPredictedTracerRounds()
    {
        EntityUid fighter = default;
        await Server.WaitPost(() =>
        {
            fighter = SEntMan.SpawnEntity("WFFighterTestBroadsword", SEntMan.GetCoordinates(PlayerCoords));
            var comp = SEntMan.GetComponent<FighterComponent>(fighter);
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(SPlayer, comp.PilotSlot));
        });
        await RunTicks(30);

        var gun = SEntMan.GetNetEntity(fighter);
        // 45 degrees off the nose, which points north: the round leaves along the edge of the cone instead.
        var aim = new NetCoordinates(Player, new Vector2(5f, 5f));
        List<int>? slots = null;
        var inCombat = false;
        await Client.WaitPost(() =>
        {
            inCombat = CEntMan.System<SharedCombatModeSystem>().IsInCombatMode(CPlayer);
            var gunSystem = CEntMan.System<ClientGunSystem>();
            slots = gunSystem.ShootRequested(gun, aim, null, null, ClientSession, true);
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent
            {
                Gun = gun,
                Coordinates = aim,
                Shot = slots,
            });
        });

        Assert.That(inCombat, "boarding did not put the pilot in combat mode");
        Assert.That(slots, Is.Not.Null.And.Not.Empty, "the pilot's client predicted no shot");

        var round = new EntityUid(slots![0]);
        var tracer = false;
        var visible = false;
        var onFighter = true;
        var offNose = 180.0;
        await Client.WaitPost(() =>
        {
            tracer = CEntMan.HasComponent<TracerComponent>(round);
            onFighter = CEntMan.GetComponent<TransformComponent>(round).ParentUid == ToClient(fighter);
            var heading = CEntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(round).LinearVelocity.ToAngle();
            offNose = Math.Abs(Angle.ShortestDistance(Angle.FromDegrees(90), heading).Degrees);
            visible = CEntMan.TryGetComponent<SpriteComponent>(round, out var sprite) && sprite.Visible;
        });
        Assert.Multiple(() =>
        {
            Assert.That(tracer, "the predicted round has no tracer");
            Assert.That(visible, "the predicted round is hidden");
            Assert.That(onFighter, Is.False, "the round was fired from inside the fighter and rides along with it");
            Assert.That(offNose, Is.LessThanOrEqualTo(8.5).And.GreaterThan(5), "the round did not leave along the cone's edge");
        });

        await RunTicks(3);
        var alive = false;
        await Client.WaitPost(() => alive = CEntMan.EntityExists(round));
        Assert.That(alive, "the predicted round vanished at once");

        // Losing the fighter hands the pilot's HUD back cleanly.
        await Server.WaitPost(() => SEntMan.DeleteEntity(fighter));
        await RunTicks(10);
    }

    [Test]
    public async Task DrainedGunWaitsForAFullCell()
    {
        EntityUid fighter = default;
        await Server.WaitPost(() =>
        {
            fighter = SEntMan.SpawnEntity("WFFighterTestBroadsword", SEntMan.GetCoordinates(PlayerCoords));
            var comp = SEntMan.GetComponent<FighterComponent>(fighter);
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(SPlayer, comp.PilotSlot));
            // One round left, and no recharging behind the test's back.
            SEntMan.RemoveComponent<BatterySelfRechargerComponent>(fighter);
            SEntMan.System<BatterySystem>().SetCharge(fighter, 5);
        });
        await RunTicks(30);

        var gun = SEntMan.GetNetEntity(fighter);
        var aim = new NetCoordinates(Player, new Vector2(0f, 5f));
        Assert.That(await Shoot(gun, aim), Is.Not.Null.And.Not.Empty, "the last round did not fire");
        await RunTicks(10);

        var recharging = false;
        await Server.WaitPost(() => recharging = SEntMan.GetComponent<FighterComponent>(fighter).GunRecharging);
        Assert.That(recharging, "emptying the cell did not lock the gun");

        // Half a cell is not enough.
        await Server.WaitPost(() => SEntMan.System<BatterySystem>().SetCharge(fighter, 70));
        await RunTicks(10);
        Assert.That(await Shoot(gun, aim), Is.Null.Or.Empty, "the gun fired before its cell was full");

        await Server.WaitPost(() => SEntMan.System<BatterySystem>().SetCharge(fighter, 140));
        await RunTicks(10);
        await Server.WaitPost(() => recharging = SEntMan.GetComponent<FighterComponent>(fighter).GunRecharging);
        Assert.That(recharging, Is.False, "a full cell did not unlock the gun");
        Assert.That(await Shoot(gun, aim), Is.Not.Null.And.Not.Empty, "the unlocked gun did not fire");
    }

    private async Task<List<int>?> Shoot(NetEntity gun, NetCoordinates aim)
    {
        List<int>? slots = null;
        await Client.WaitPost(() =>
        {
            var gunSystem = CEntMan.System<ClientGunSystem>();
            slots = gunSystem.ShootRequested(gun, aim, null, null, ClientSession, true);
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent { Gun = gun, Coordinates = aim, Shot = slots });
        });
        return slots;
    }
}
