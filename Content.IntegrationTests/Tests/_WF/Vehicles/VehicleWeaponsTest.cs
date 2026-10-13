#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Timing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using ClientGunSystem = Content.Client.Weapons.Ranged.Systems.GunSystem;
using ServerEntityStorage = Content.Server.Storage.Components.EntityStorageComponent;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Weapon seats on a vehicle with a turret and a cannon: who is the operator, who holds which hardpoint, and that the
/// turret turns to the gunner's aim and only lets the shot go once it is lined up, also on a turned grid.
/// </summary>
public sealed class VehicleWeaponsTest : InteractionTest
{
    private const string Vehicle = "WFWeaponsTestVehicle";
    private const string Seat = "WFWeaponsTestSeat";
    private const string SeatSecondary = "WFWeaponsTestSeatSecondary";
    private const string SeatWideView = "WFWeaponsTestSeatWideView";
    private const string VehicleCargo = "WFWeaponsTestVehicleCargo";
    private const string SelectAction = "ActionVehicleSelectHardpoint";

    // The chassis sits at this spot of the grid; its doorways are a metre to its left and right.
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);
    private static readonly Vector2[] DoorwayOffsets = { new(1f, 0f), new(-1f, 0f) };

    /// <summary>Where the cannon's muzzle sits along its barrel; the barrel points south at rest.</summary>
    private static readonly Vector2 MuzzleOffset = new(0f, -0.8f);

    /// <summary>The two barrels the cannon alternates between, either side of the muzzle, in the barrel's frame.</summary>
    private static readonly Vector2[] BarrelOffsets = { new(-0.3f, 0f), new(0.3f, 0f) };

    private const float TurretSpeedDegrees = 90f;

    protected override string PlayerPrototype => "MobHuman";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFWeaponsTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFWeaponsTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: WFWeaponsTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFWeaponsTestCannon
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeCannon
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeCannon
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: VehicleTurretAttachment
  - type: VehicleTurret
    useBarrelDirectionForShots: true
  - type: GunUseGunOrigin
  - type: GunIgnoreContainerOwnerCollision
  - type: GunMuzzleOffset
    offset: 0,-0.8
    useContainerOwner: false
  - type: VehicleTurretMuzzle
    useDirectionalOffsets: false
    offsetLeft: -0.3,0
    offsetRight: 0.3,0
  - type: Gun
    fireRate: 4
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFWeaponsTestCartridge
    capacity: 20

- type: entity
  id: WFWeaponsTestTurret
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeTurret
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeTurret
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: VehicleTurret
    rotateToCursor: true
    stabilizedRotation: false
    rotationSpeed: 90
  - type: ItemSlots
    slots:
      turret-cannon:
        startingItem: WFWeaponsTestCannon
        disableEject: true
        whitelist:
          components:
          - HardpointItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: turret-cannon
      hardpointType: HardpointTypeCannon
      slotType: HardpointSlotTypeCannon
      required: false

- type: entity
  parent: WFVehicleTestChassis
  id: WFWeaponsTestVehicle
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
  - type: Appearance
  - type: HardpointIntegrity
    maxIntegrity: 1000
  - type: VehicleEnter
    interiorPath: /Maps/_WF/Vehicles/Interiors/wf_test_interior.yml
    maxPassengers: 3
    enterDoAfter: 0.1
    exitDoAfter: 0.1
    entryPoints:
    - offset: 1,0
      radius: 0.6
      interiorCoords: 1.5,1.5
    - offset: -1,0
      radius: 0.6
      interiorCoords: 3.5,1.5
  - type: ItemSlots
    slots:
      turret:
        startingItem: WFWeaponsTestTurret
        disableEject: true
        whitelist:
          components:
          - HardpointItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: turret
      hardpointType: HardpointTypeTurret
      slotType: HardpointSlotTypeTurret
      required: false

- type: entity
  parent: SeatBase
  id: WFWeaponsTestSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Static
  - type: VehicleWeaponsSeat
  - type: UserInterface
    interfaces:
      enum.VehicleWeaponsUiKey.Key:
        type: VehicleWeaponsBoundUserInterface

- type: entity
  parent: WFWeaponsTestSeat
  id: WFWeaponsTestSeatSecondary
  categories: [ HideSpawnMenu ]
  components:
  - type: VehicleWeaponsSeat
    isPrimaryOperatorSeat: false

- type: entity
  parent: WFWeaponsTestSeat
  id: WFWeaponsTestSeatWideView
  categories: [ HideSpawnMenu ]
  components:
  - type: VehicleWeaponsSeat
    baseViewPvsScale: 0.35
    baseViewCursorMaxOffset: 4
    baseViewCursorPvsIncrease: 0.5

# A gun that is a turret part but has no attachment component, and a turret to carry one.
- type: entity
  id: WFWeaponsTestPlainGun
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeCannon
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeCannon
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: VehicleTurret
    rotateToCursor: true

- type: entity
  id: WFWeaponsTestTurretBare
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeTurret
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeTurret
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: VehicleTurret
    rotateToCursor: true
    rotationSpeed: 90
  - type: ItemSlots
    slots:
      turret-cannon:
        disableEject: true
        whitelist:
          components:
          - HardpointItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: turret-cannon
      hardpointType: HardpointTypeCannon
      slotType: HardpointSlotTypeCannon
      required: false

# The weapons vehicle with a bare turret mount and a slot that is not a hardpoint, for things that are only carried on it.
- type: entity
  parent: WFWeaponsTestVehicle
  id: WFWeaponsTestVehicleCargo
  components:
  - type: ItemSlots
    slots:
      turret:
        startingItem: WFWeaponsTestTurret
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      turret-bare:
        startingItem: WFWeaponsTestTurretBare
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      cargo:
        name: Cargo
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: turret
      hardpointType: HardpointTypeTurret
      slotType: HardpointSlotTypeTurret
      required: false
    - id: turret-bare
      hardpointType: HardpointTypeTurret
      slotType: HardpointSlotTypeTurret
      required: false
";

    /// <summary>Refuses every weapons seat while <see cref="RefuseSeats"/> is set.</summary>
    public sealed class WeaponSeatProbeSystem : EntitySystem
    {
        public bool RefuseSeats;

        public override void Initialize()
        {
            SubscribeLocalEvent<VehicleWeaponsSeatComponent, VehicleSeatAttemptEvent>(OnSeatAttempt);
        }

        private void OnSeatAttempt(Entity<VehicleWeaponsSeatComponent> ent, ref VehicleSeatAttemptEvent args)
        {
            if (RefuseSeats)
                args.Cancelled = true;
        }
    }

    private sealed class Rig
    {
        public EntityUid Vehicle;
        public EntityUid Turret;
        public EntityUid Gun;
    }

    /// <summary>A block of plating with the vehicle in the middle of it.</summary>
    private async Task<Rig> BuildRig(string prototype = Vehicle)
    {
        var rig = new Rig();
        await Server.WaitPost(() =>
        {
            var plating = TileMan["Plating"].TileId;
            for (var x = -4; x < 16; x++)
            {
                for (var y = -4; y < 16; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Vehicle = SEntMan.SpawnEntity(prototype, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot));
            Transform.SetLocalRotation(rig.Vehicle, Angle.Zero);
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(rig.Vehicle, "turret", out var turretSlot), Is.True, "The vehicle has no turret slot.");
            rig.Turret = turretSlot!.Item ?? EntityUid.Invalid;
            Assert.That(rig.Turret, Is.Not.EqualTo(EntityUid.Invalid), "The turret was not fitted.");

            Assert.That(slots.TryGetSlot(rig.Turret, "turret-cannon", out var gunSlot), Is.True, "The turret has no cannon slot.");
            rig.Gun = gunSlot!.Item ?? EntityUid.Invalid;
            Assert.That(rig.Gun, Is.Not.EqualTo(EntityUid.Invalid), "The cannon was not fitted.");
        });

        return rig;
    }

    private static Vector2 Doorway(int index) => VehicleSpot + DoorwayOffsets[index];

    /// <summary>Puts <paramref name="user"/> at a doorway, activates the vehicle and waits out the do-after.</summary>
    private async Task Enter(Rig rig, EntityUid user, int doorway)
    {
        await Server.WaitPost(() => Transform.SetCoordinates(user, new EntityCoordinates(MapData.Grid.Owner, Doorway(doorway))));
        await RunTicks(2);

        await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().InteractionActivate(user, rig.Vehicle));
        await RunTicks(40);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.True, "The user did not get inside the vehicle.");
        });
    }

    /// <summary>Puts a new seat on the interior grid and straps <paramref name="user"/> into it.</summary>
    private async Task<EntityUid> SeatUp(Rig rig, EntityUid user, string seatPrototype, Vector2 seatSpot)
    {
        var seat = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
            seat = SEntMan.SpawnEntity(seatPrototype, new EntityCoordinates(grid, seatSpot));
            Transform.SetCoordinates(user, new EntityCoordinates(grid, seatSpot));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.True, "The user could not strap into the weapons seat.");
        });
        await RunTicks(10);

        return seat;
    }

    private List<EntityUid> SelectActions(EntityUid user)
    {
        var found = new List<EntityUid>();
        if (!SEntMan.TryGetComponent(user, out ActionsComponent? actions))
            return found;

        foreach (var action in actions.Actions)
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == SelectAction)
                found.Add(action);
        }

        return found;
    }

    private void PerformAction(EntityUid user, EntityUid action)
    {
        var actions = SEntMan.System<SharedActionsSystem>();
        Assert.That(actions.TryGetActionData(action, out var data), Is.True);
        actions.PerformAction(user, null, action, data!, data!.BaseEvent, STiming.CurTime, predicted: false);
    }

    private List<EntityUid> Projectiles()
    {
        var found = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            found.Add(uid);
        }

        return found;
    }

    /// <summary>Fires the rig's cannon as <paramref name="user"/> at a point on the vehicle's map.</summary>
    private void Shoot(Rig rig, EntityUid user, Vector2 worldTarget)
    {
        var guns = SEntMan.System<SharedGunSystem>();
        var coordinates = Transform.ToCoordinates(new MapCoordinates(worldTarget, MapId));
        guns.AttemptShoot(user, rig.Gun, SEntMan.GetComponent<GunComponent>(rig.Gun), coordinates);
    }

    /// <summary>Buckling in makes the user an operator with a select action per weapon; selecting and unbuckling undo each other.</summary>
    [Test]
    public async Task BucklingIntoAWeaponsSeatMakesAnOperatorWhoSelectsAHardpoint()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var seat = await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));
        var netGun = SEntMan.GetNetEntity(rig.Gun);

        await Server.WaitAssertion(() =>
        {
            var op = SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer);
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);
            var actions = SelectActions(SPlayer);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(op.Vehicle, Is.EqualTo(rig.Vehicle), "The operator is not tied to the vehicle.");
                Assert.That(op.SelectedWeapon, Is.Null, "Strapping in should not pick a weapon by itself.");
                Assert.That(weapons.Operator, Is.EqualTo(SPlayer), "The primary seat's user should be the primary operator.");
                Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.EqualTo(rig.Vehicle), "The gunner's eye is not on the vehicle.");
                Assert.That(SEntMan.GetComponent<VehicleWatchingComponent>(SPlayer).Watching, Is.EqualTo(rig.Vehicle));
                Assert.That(actions, Has.Count.EqualTo(1), "The gunner should get one select action for the cannon.");
                Assert.That(SEntMan.GetComponent<VehicleHardpointActionComponent>(actions[0]).MountedWeapon, Is.EqualTo(rig.Gun));
            }

            var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints
                .Single(e => e.InstalledEntity == netGun);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entry.Selectable, Is.True);
                Assert.That(entry.Selected, Is.False);
            }
        });

        // Pick the cannon the way the window does.
        Target = SEntMan.GetNetEntity(seat);
        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));

        await Server.WaitAssertion(() =>
        {
            var op = SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer);
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(op.SelectedWeapon, Is.EqualTo(rig.Gun), "The window's pick did not select the cannon.");
                Assert.That(weapons.SelectedWeapon, Is.EqualTo(rig.Gun));
                Assert.That(weapons.HardpointOperators[rig.Gun], Is.EqualTo(SPlayer));
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var gun, out _), Is.True, "The gun lookup does not answer for the gunner.");
                Assert.That(gun, Is.EqualTo(rig.Gun));
                Assert.That(SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun).Selected, Is.True);
            }
        });

        // The hotbar action toggles it off, and on again.
        await Server.WaitAssertion(() =>
        {
            var action = SelectActions(SPlayer).Single();
            PerformAction(SPlayer, action);
            Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.Null, "The action did not deselect the cannon.");
            Assert.That(SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle).HardpointOperators, Is.Empty);
            Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False);

            PerformAction(SPlayer, action);
            Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.EqualTo(rig.Gun), "The action did not select the cannon.");
        });

        // Damage reaches the window: the refresh event the hardpoint code raises redraws the seat's state.
        await Server.WaitAssertion(() =>
        {
            SEntMan.GetComponent<HardpointIntegrityComponent>(rig.Gun).Integrity = 40f;
            var refresh = new VehicleHardpointsRefreshEvent(rig.Vehicle);
            SEntMan.EventBus.RaiseLocalEvent(rig.Vehicle, ref refresh);

            var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun);
            Assert.That(entry.Integrity, Is.EqualTo(40f), "The weapons state did not follow the hardpoint's integrity.");
            SEntMan.GetComponent<HardpointIntegrityComponent>(rig.Gun).Integrity = 100f;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer, true), Is.True, "The user could not unstrap.");
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleWeaponsOperatorComponent>(SPlayer), Is.False, "Unbuckling left the operator component.");
                Assert.That(SEntMan.HasComponent<VehicleWatchingComponent>(SPlayer), Is.False);
                Assert.That(SelectActions(SPlayer), Is.Empty, "Unbuckling left the select actions.");
                Assert.That(weapons.Operator, Is.Null);
                Assert.That(weapons.SelectedWeapon, Is.Null);
                Assert.That(weapons.HardpointOperators, Is.Empty);
                Assert.That(weapons.OperatorSelections, Is.Empty);
                Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.Null, "The gunner's eye is still on the vehicle.");
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False);
            }
        });
    }

    /// <summary>
    /// Deleting a mob that is still strapped into a weapons seat takes its weapons state with it and logs no error (the
    /// pair fails the test on one): nothing grants the dying mob an action, and the vehicle forgets the operator. With
    /// <paramref name="alsoLooksOutOfAViewport"/> the gunner holds a second source of the view action, as someone does who
    /// looked out of a viewport before sitting down; taking the seat's source away then leaves the action to be kept.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task DeletingAGunnerStrappedIntoAWeaponsSeatLogsNoError(bool alsoLooksOutOfAViewport)
    {
        var rig = await BuildRig();
        var gunner = await SpawnAtDoorway(0);
        await Enter(rig, gunner, 0);
        await SeatUp(rig, gunner, Seat, new Vector2(1.5f, 2.5f));

        if (alsoLooksOutOfAViewport)
            await AddViewSource(rig, gunner);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleWeaponsOperatorComponent>(gunner), Is.True, "The gunner is not an operator.");
                Assert.That(SEntMan.HasComponent<VehicleViewToggleComponent>(gunner), Is.True, "The gunner has no view toggle.");
                Assert.That(SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle).Operator, Is.EqualTo(gunner));
            }
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(gunner));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(gunner), Is.True);
                Assert.That(weapons.Operator, Is.Null, "The vehicle still has the deleted gunner as its operator.");
                Assert.That(weapons.OperatorSelections, Is.Empty);
                Assert.That(weapons.HardpointOperators, Is.Empty);
            }
        });
    }

    /// <summary>Deleting the driver while still strapped into the driver seat logs no error and frees the wheel.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task DeletingADriverStrappedIntoTheDriverSeatLogsNoError(bool alsoLooksOutOfAViewport)
    {
        var rig = await BuildRig();
        var driver = await SpawnAtDoorway(0);
        await Enter(rig, driver, 0);

        var seat = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var interior = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle);
            var query = SEntMan.EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == interior.MapId)
                    seat = uid;
            }

            Transform.SetCoordinates(driver, new EntityCoordinates(interior.Grid, 3.5f, 1.5f));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(seat, Is.Not.EqualTo(EntityUid.Invalid), "The interior has no driver seat.");
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(driver, driver, seat), Is.True, "The driver could not strap in.");
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(driver), "The driver is not the operator.");
                Assert.That(SEntMan.HasComponent<VehicleViewToggleComponent>(driver), Is.True, "The driver has no view toggle.");
                Assert.That(SEntMan.HasComponent<VehicleLockActionComponent>(driver), Is.True, "The driver has no lock action.");
            }
        });

        if (alsoLooksOutOfAViewport)
            await AddViewSource(rig, driver);

        await Server.WaitPost(() => SEntMan.DeleteEntity(driver));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(driver), Is.True);
                Assert.That(SEntMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.Null, "The vehicle still has the deleted driver as its operator.");
            }
        });
    }

    /// <summary>Gives <paramref name="user"/> a second source of the view action, as looking out of a viewport does.</summary>
    private async Task AddViewSource(Rig rig, EntityUid user)
    {
        await Server.WaitPost(() =>
        {
            var viewport = SEntMan.SpawnEntity("VehicleViewport", new EntityCoordinates(SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid, 2.5f, 2.5f));
            SEntMan.System<VehicleViewToggleSystem>().EnableViewToggle(user, rig.Vehicle, viewport, insideTarget: null, isOutside: true);
        });
        await RunTicks(3);
    }

    /// <summary>A new human standing at a doorway of the rig's vehicle.</summary>
    private async Task<EntityUid> SpawnAtDoorway(int doorway)
    {
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() => mob = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(MapData.Grid.Owner, Doorway(doorway))));
        await RunTicks(3);
        return mob;
    }

    /// <summary>
    /// A turret with an overlay gets a drawn copy on the client, in the state its integrity picks, turned as the turret is
    /// on a grid that is itself turned.
    /// </summary>
    [Test]
    public async Task TheTurretOverlayIsDrawnInTheTurretsPose()
    {
        var rig = await BuildRig();
        var netTurret = SEntMan.GetNetEntity(rig.Turret);

        await Server.WaitPost(() =>
        {
            var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
#pragma warning disable RA0002
            turret.ShowOverlay = true;
            turret.OverlayRsi = "_WF/Vehicles/humvee.rsi";
            turret.OverlayState = "humveeturret_0";
            turret.OverlayDamagedState = "humveeturret_1";
#pragma warning restore RA0002
            SEntMan.Dirty(rig.Turret, turret);

            Transform.SetLocalRotation(MapData.Grid.Owner, Angle.FromDegrees(45));
            Assert.That(SEntMan.System<VehicleTurretSystem>().TrySetTargetRotationWorld(rig.Gun, Angle.FromDegrees(120)), Is.True);
        });
        await RunSeconds(2f);

        var visual = EntityUid.Invalid;
        var serverRotation = Angle.Zero;
        await Server.WaitAssertion(() =>
        {
            visual = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret).VisualEntity ?? EntityUid.Invalid;
            Assert.That(visual, Is.Not.EqualTo(EntityUid.Invalid), "The server made no overlay entity for the turret.");
            serverRotation = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret).WorldRotation;
        });

        var sprites = CEntMan.System<SpriteSystem>();
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(SEntMan.GetNetEntity(visual), out var found), Is.True, "The client never received the overlay entity.");
            var cVisual = found!.Value;
            var sprite = CEntMan.GetComponent<SpriteComponent>(cVisual);
            var xforms = CEntMan.System<SharedTransformSystem>();
            var cVehicle = CEntMan.GetEntity(SEntMan.GetNetEntity(rig.Vehicle));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(CEntMan.GetComponent<VehicleTurretVisualComponent>(cVisual).Turret, Is.EqualTo(netTurret));
                Assert.That(sprites.LayerGetRsiState((cVisual, sprite), 0).Name, Is.EqualTo("humveeturret_0"), "The overlay shows the wrong state.");
                Assert.That(sprite.AllLayers.First().Visible, Is.True, "The overlay is hidden.");

                var expected = xforms.GetWorldRotation(cVehicle) + serverRotation;
                Assert.That(Math.Abs(Angle.ShortestDistance(xforms.GetWorldRotation(cVisual), expected).Degrees), Is.LessThan(0.5), "The overlay is not turned as the turret is.");
                Assert.That(Math.Abs(Angle.ShortestDistance(expected, Angle.FromDegrees(120)).Degrees), Is.LessThan(0.5), "The turret did not come round to its aim.");
            }

            var system = CEntMan.System<VehicleTurretVisualSystem>();
            Assert.That(system.TryGetRenderedPose(CEntMan.GetEntity(netTurret), out _, out var renderedRotation), Is.True);
            Assert.That(Math.Abs(Angle.ShortestDistance(renderedRotation, xforms.GetWorldRotation(cVehicle) + serverRotation).Degrees), Is.LessThan(0.5),
                "The rendered pose is not the turret's.");
        });

        // A destroyed turret is drawn in its damaged state.
        await Server.WaitPost(() =>
        {
            var integrity = SEntMan.GetComponent<HardpointIntegrityComponent>(rig.Turret);
            integrity.Integrity = 0f;
            SEntMan.Dirty(rig.Turret, integrity);
        });
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(SEntMan.GetNetEntity(visual), out var found), Is.True);
            var sprite = CEntMan.GetComponent<SpriteComponent>(found!.Value);
            Assert.That(sprites.LayerGetRsiState((found.Value, sprite), 0).Name, Is.EqualTo("humveeturret_1"), "A destroyed turret is not drawn damaged.");
        });
    }

    /// <summary>
    /// The turret sits a metre off the vehicle's centre in the way the vehicle faces, and that follows the ship it drives
    /// on: facing grid south the offset is south of the vehicle, and on a grid turned a quarter turn it turns with it.
    /// </summary>
    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task TheTurretAnchorFollowsTheVehiclesFacingOnATurnedGrid(int gridDegrees)
    {
        var rig = await BuildRig();

        await Server.WaitPost(() =>
        {
            var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
#pragma warning disable RA0002
            turret.UseDirectionalOffsets = true;
            turret.PixelOffsetSouth = new Vector2(0f, -32f);
            turret.PixelOffsetEast = new Vector2(32f, 0f);
            turret.PixelOffsetNorth = new Vector2(0f, 32f);
            turret.PixelOffsetWest = new Vector2(-32f, 0f);
#pragma warning restore RA0002

            // Facing the grid's south, as the mover keeps it; the grid then turns under it.
            SEntMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle).CurrentDirection = new Vector2i(0, -1);
            Transform.SetLocalRotation(MapData.Grid.Owner, Angle.FromDegrees(gridDegrees));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<VehicleTurretSystem>().TryGetTurretPose(rig.Turret, out var origin, out _), Is.True);

            var vehiclePos = Transform.GetWorldPosition(rig.Vehicle);
            var expected = vehiclePos + Angle.FromDegrees(gridDegrees).ToWorldVec();
            Assert.That(Vector2.Distance(Transform.ToMapCoordinates(origin).Position, expected), Is.LessThan(0.02f),
                "The turret is not a metre ahead of the vehicle in the way it faces.");
        });
    }

    /// <summary>The seat attempt event gates a weapons seat as it does the driver's: a refused seat makes no operator.</summary>
    [Test]
    public async Task ARefusedSeatMakesNoOperator()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);

        var seat = EntityUid.Invalid;
        var probe = SEntMan.System<WeaponSeatProbeSystem>();
        try
        {
            await Server.WaitPost(() =>
            {
                var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
                seat = SEntMan.SpawnEntity(Seat, new EntityCoordinates(grid, 1.5f, 2.5f));
                Transform.SetCoordinates(SPlayer, new EntityCoordinates(grid, 1.5f, 2.5f));
            });
            await RunTicks(3);

            await Server.WaitAssertion(() =>
            {
                probe.RefuseSeats = true;
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat), Is.False, "A refused seat still took the user.");
            });
            await RunTicks(5);

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.HasComponent<VehicleWeaponsOperatorComponent>(SPlayer), Is.False, "A refused seat still made an operator.");
                Assert.That(SEntMan.HasComponent<VehicleWeaponsComponent>(rig.Vehicle), Is.False);

                probe.RefuseSeats = false;
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat), Is.True, "The seat stayed closed once the refusal stopped.");
            });
            await RunTicks(5);

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.HasComponent<VehicleWeaponsOperatorComponent>(SPlayer), Is.True);
            });
        }
        finally
        {
            probe.RefuseSeats = false;
        }
    }

    /// <summary>A second gunner is not offered a hardpoint the first one holds, cannot take it, and can once it is let go.</summary>
    [Test]
    public async Task ASecondGunnerCannotTakeAHardpointAnotherOperatorHolds()
    {
        var rig = await BuildRig();
        var first = EntityUid.Invalid;
        await Server.WaitPost(() => first = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(MapData.Grid.Owner, Doorway(0))));
        await RunTicks(3);

        await Enter(rig, first, 0);
        var firstSeat = await SeatUp(rig, first, Seat, new Vector2(1.5f, 2.5f));

        await Enter(rig, SPlayer, 1);
        var secondSeat = await SeatUp(rig, SPlayer, SeatSecondary, new Vector2(2.5f, 2.5f));
        var netGun = SEntMan.GetNetEntity(rig.Gun);

        await Server.WaitAssertion(() =>
        {
            var action = SelectActions(first).Single();
            PerformAction(first, action);

            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);
            Assert.That(weapons.HardpointOperators[rig.Gun], Is.EqualTo(first), "The first gunner did not take the cannon.");
        });

        Target = SEntMan.GetNetEntity(secondSeat);
        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));

        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.Null, "The second gunner took a held hardpoint.");
                Assert.That(weapons.HardpointOperators[rig.Gun], Is.EqualTo(first), "The first gunner lost the cannon.");
                Assert.That(weapons.Operator, Is.EqualTo(first));
                Assert.That(weapons.SelectedWeapon, Is.EqualTo(rig.Gun));
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False, "The second gunner can fire the held cannon.");
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(first, out var gun, out _), Is.True);
                Assert.That(gun, Is.EqualTo(rig.Gun));

                var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(secondSeat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun);
                Assert.That(entry.Selectable, Is.False, "The held cannon is offered to the second gunner.");
                Assert.That(entry.OperatorIsSelf, Is.False);
            }
        });

        // Once the first gunner lets go, the second can have it.
        await Server.WaitAssertion(() => PerformAction(first, SelectActions(first).Single()));
        await RunTicks(3);

        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));

        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.GetComponent<VehicleWeaponsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.EqualTo(rig.Gun), "The second gunner could not take the released cannon.");
                Assert.That(weapons.HardpointOperators[rig.Gun], Is.EqualTo(SPlayer));
                Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(first).SelectedWeapon, Is.Null);
                Assert.That(weapons.SelectedWeapon, Is.Null, "The secondary seat's pick should not become the primary weapon.");
            }
        });
    }

    /// <summary>
    /// The gunner aims through the client; the turret turns at its own speed, the shot is refused while it is off aim, and
    /// once it is lined up exactly one projectile leaves the muzzle on the vehicle's map, along the barrel.
    /// </summary>
    [TestCase(0)]
    [TestCase(45)]
    public async Task TheTurretStepsToTheAimAndTheShotIsBlockedUntilItIsAligned(int gridDegrees)
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() => PerformAction(SPlayer, SelectActions(SPlayer).Single()));
        await RunTicks(3);

        await Server.WaitPost(() => Transform.SetLocalRotation(MapData.Grid.Owner, Angle.FromDegrees(gridDegrees)));
        await RunTicks(3);

        var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
        var vehiclePos = Vector2.Zero;
        var aimPoint = Vector2.Zero;
        var startWorld = Angle.Zero;
        var tickPeriod = TickPeriod;

        await Server.WaitAssertion(() =>
        {
            vehiclePos = Transform.GetWorldPosition(rig.Vehicle);
            startWorld = Transform.GetWorldRotation(rig.Vehicle) + turret.WorldRotation;

            // A point due east of the vehicle in the world, whichever way the grid is turned.
            aimPoint = vehiclePos + new Vector2(6f, 0f);
            Shoot(rig, SPlayer, aimPoint);
            Assert.That(Projectiles(), Is.Empty, "The turret shot before it was aimed.");
        });

        // Aim through the client, as the gunner's cursor does.
        var netAim = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(aimPoint, MapId)));
        await Client.WaitPost(() =>
        {
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new VehicleTurretRotateEvent
            {
                Turret = SEntMan.GetNetEntity(rig.Gun),
                Coordinates = netAim,
            });
        });
        await RunTicks(2);

        var desired = (aimPoint - vehiclePos).ToWorldAngle();
        var totalDegrees = Math.Abs(Angle.ShortestDistance(startWorld, desired).Degrees);
        Assert.That(totalDegrees, Is.GreaterThan(30), "The test needs the turret to start well off its aim.");

        var rotations = new List<double>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(Math.Abs(Angle.ShortestDistance(Transform.GetWorldRotation(rig.Vehicle) + turret.WorldRotation, desired).Degrees),
                Is.GreaterThan(5), "The turret was already on aim.");

            // Off aim, the shot is refused and leaves no cooldown.
            var gun = SEntMan.GetComponent<GunComponent>(rig.Gun);
            Shoot(rig, SPlayer, aimPoint);
            Assert.That(Projectiles(), Is.Empty, "A shot got out while the turret was off its aim.");
            Assert.That(gun.NextFire, Is.LessThanOrEqualTo(STiming.CurTime), "A refused shot left a cooldown.");
        });

        var aligned = false;
        var ticks = 2;
        while (ticks < 200 && !aligned)
        {
            await RunTicks(1);
            ticks++;
            await Server.WaitAssertion(() =>
            {
                rotations.Add(turret.WorldRotation.Degrees);
                var remaining = Angle.ShortestDistance(Transform.GetWorldRotation(rig.Vehicle) + turret.WorldRotation, desired);
                aligned = Math.Abs(remaining.Degrees) < 0.5;
            });
        }

        Assert.That(aligned, Is.True, "The turret never lined up with the aim.");

        // It turned in steps no bigger than its speed allows, and took about as long as the angle needs.
        var maxStep = TurretSpeedDegrees * tickPeriod + 0.01;
        var steps = rotations.Zip(rotations.Skip(1), (a, b) => Math.Abs(Angle.ShortestDistance(Angle.FromDegrees(a), Angle.FromDegrees(b)).Degrees)).ToList();
        var expectedTicks = totalDegrees / (TurretSpeedDegrees * tickPeriod);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(steps.Max(), Is.LessThanOrEqualTo(maxStep), "The turret jumped instead of stepping.");
            Assert.That(ticks, Is.GreaterThan(expectedTicks - 6), "The turret got there too quickly for its speed.");
            Assert.That(ticks, Is.LessThan(expectedTicks + 20), "The turret took much longer than its speed allows.");
        }

        // Two shots in turn: each leaves from its own barrel, perpendicular to the aim whichever way the grid is turned.
        for (var round = 0; round < 2; round++)
        {
            var barrelIndex = round;
            await Server.WaitAssertion(() =>
            {
                var barrel = Transform.GetWorldRotation(rig.Vehicle) + turret.WorldRotation;
                var expectedMuzzle = vehiclePos + barrel.RotateVec(MuzzleOffset + BarrelOffsets[barrelIndex]);

                Shoot(rig, SPlayer, aimPoint);

                var shots = Projectiles();
                Assert.That(shots, Has.Count.EqualTo(1), "An aligned turret should fire exactly one projectile.");

                var shot = shots[0];
                var velocity = SEntMan.GetComponent<PhysicsComponent>(shot).LinearVelocity;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(Transform.GetMapId(shot), Is.EqualTo(MapId), "The projectile is not on the vehicle's map.");
                    Assert.That(Vector2.Distance(Transform.GetWorldPosition(shot), expectedMuzzle), Is.LessThan(0.02f), $"Shot {barrelIndex} did not start at its barrel's muzzle.");
                    Assert.That(SEntMan.GetComponent<ProjectileComponent>(shot).Shooter, Is.EqualTo(SPlayer));
                    Assert.That(SEntMan.GetComponent<ProjectileComponent>(shot).Weapon, Is.EqualTo(rig.Gun));
                    Assert.That(velocity.Length(), Is.EqualTo(30f).Within(0.1f));
                    Assert.That(Math.Abs(Angle.ShortestDistance(velocity.ToWorldAngle(), barrel).Degrees), Is.LessThan(0.5), "The projectile does not fly along the barrel.");
                    Assert.That(Math.Abs(Angle.ShortestDistance(velocity.ToWorldAngle(), (aimPoint - vehiclePos).ToWorldAngle()).Degrees), Is.LessThan(1), "The projectile is not flying at the target.");
                }

                SEntMan.DeleteEntity(shot);
            });

            // Let the gun's cooldown pass before the next shot.
            await RunSeconds(0.5f);
        }
    }

    /// <summary>
    /// The gunner's client fires its own copy of the shot. With the turret on aim it must start at the muzzle the server's
    /// shot starts at, whichever way the grid is turned, be the one copy, and be linked to the server's projectile.
    /// </summary>
    [TestCase(0)]
    [TestCase(45)]
    public async Task APredictedTurretShotStartsWhereTheServersShotDoes(int gridDegrees)
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() =>
        {
            PerformAction(SPlayer, SelectActions(SPlayer).Single());
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
        });
        await Server.WaitPost(() => Transform.SetLocalRotation(MapData.Grid.Owner, Angle.FromDegrees(gridDegrees)));
        await RunTicks(5);

        var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var vehiclePos = Vector2.Zero;
        var aimPoint = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            vehiclePos = Transform.GetWorldPosition(rig.Vehicle);
            aimPoint = vehiclePos + new Vector2(6f, 0f);
        });

        var netAim = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(aimPoint, MapId)));
        await Client.WaitPost(() =>
        {
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new VehicleTurretRotateEvent
            {
                Turret = netGun,
                Coordinates = netAim,
            });
        });

        // Wait for the turret to settle on the aim, on both sides.
        await RunSeconds(2f);

        var cVehicle = EntityUid.Invalid;
        var cTurret = EntityUid.Invalid;
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True, "The gunner's client never received the cannon.");
            Assert.That(CEntMan.TryGetEntity(SEntMan.GetNetEntity(rig.Vehicle), out var foundVehicle), Is.True, "The gunner's client never received the vehicle.");
            Assert.That(CEntMan.TryGetEntity(SEntMan.GetNetEntity(rig.Turret), out var foundTurret), Is.True, "The gunner's client never received the turret.");
            cVehicle = foundVehicle!.Value;
            cTurret = foundTurret!.Value;

            var serverRotation = Angle.ShortestDistance(CEntMan.GetComponent<VehicleTurretComponent>(cTurret).WorldRotation, turret.WorldRotation);
            Assert.That(Math.Abs(serverRotation.Degrees), Is.LessThan(0.5), "The client's turret is not where the server's is.");
        });

        var expectedMuzzle = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            var barrel = Transform.GetWorldRotation(rig.Vehicle) + turret.WorldRotation;
            expectedMuzzle = vehiclePos + barrel.RotateVec(MuzzleOffset + BarrelOffsets[0]);
            var desired = (aimPoint - vehiclePos).ToWorldAngle();
            Assert.That(Math.Abs(Angle.ShortestDistance(barrel, desired).Degrees), Is.LessThan(0.5), "The turret is not on aim.");
        });

        List<int>? slots = null;
        var copyPos = Vector2.Zero;
        var copyMap = MapId.Nullspace;
        var clientMap = MapId.Nullspace;
        await Client.WaitPost(() =>
        {
            var guns = CEntMan.System<ClientGunSystem>();
            slots = guns.ShootRequested(netGun, netAim, null, null, ClientSession, true);
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent
            {
                Gun = netGun,
                Coordinates = netAim,
                Shot = slots,
                Predicted = guns.DrewHitscan,
            });

            if (slots is not { Count: 1 })
                return;

            var xforms = CEntMan.System<SharedTransformSystem>();
            var copy = new EntityUid(slots[0]);
            copyPos = xforms.GetWorldPosition(copy);
            copyMap = xforms.GetMapId(copy);
            clientMap = xforms.GetMapId(cVehicle);
        });

        Assert.That(slots, Is.Not.Null, "The client predicted nothing.");
        Assert.That(slots, Has.Count.EqualTo(1), "The client should predict exactly one projectile.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(copyMap, Is.EqualTo(clientMap), "The predicted copy is not on the vehicle's map.");
            Assert.That(Vector2.Distance(copyPos, expectedMuzzle), Is.LessThan(0.05f), "The predicted copy did not start at the muzzle.");
        }

        await RunTicks(10);

        var serverShot = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1), "The server should have fired exactly one projectile.");
            serverShot = shots[0];

            var linked = SEntMan.GetComponent<PredictedProjectileServerComponent>(serverShot);
            Assert.That(linked.ClientId, Is.EqualTo(slots![0]), "The server's projectile is not linked to the client's copy.");
        });

        // Deleting the server's copy drops the predicted one, so teardown is not left with projectiles in flight.
        await Server.WaitPost(() => SEntMan.DeleteEntity(serverShot));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(new EntityUid(slots![0])), Is.False, "The orphaned predicted copy was not removed."));
    }

    /// <summary>The eye numbers a gunner's view moves: the PVS scale the widened view changes, and the zoom, which it must not.</summary>
    private (float PvsScale, Vector2 TargetZoom, Vector2 Zoom) ReadEye(EntityUid user)
    {
        var eye = SEntMan.GetComponent<EyeComponent>(user);
        var contentEye = SEntMan.GetComponent<ContentEyeComponent>(user);
        return (eye.PvsScale, contentEye.TargetZoom, eye.Zoom);
    }

    /// <summary>
    /// A seat that widens the view gives the gunner a bigger PVS box while they sit in it, and nothing of it is left when they
    /// stop being a gunner: unstrapped, the seat deleted from under them, or the vehicle deleted under them. The zoom is theirs
    /// throughout.
    /// </summary>
    [TestCase("unstrapped")]
    [TestCase("seat deleted")]
    [TestCase("vehicle deleted")]
    public async Task AGunnersWiderViewIsGoneWhenTheyLeaveTheSeat(string how)
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);

        var before = default((float PvsScale, Vector2 TargetZoom, Vector2 Zoom));
        await Server.WaitPost(() => before = ReadEye(SPlayer));

        var seat = await SeatUp(rig, SPlayer, SeatWideView, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() =>
        {
            var seated = ReadEye(SPlayer);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleGunnerViewUserComponent>(SPlayer), Is.True, "The seat gave no wider view.");
                Assert.That(seated.PvsScale, Is.EqualTo(before.PvsScale + 0.85f).Within(0.001f), "The gunner's PVS box was not widened by the seat's scale and cursor reach.");
                Assert.That(seated.TargetZoom, Is.EqualTo(before.TargetZoom), "Strapping in changed the gunner's zoom.");
                Assert.That(seated.Zoom, Is.EqualTo(before.Zoom));
            }
        });

        await Server.WaitPost(() =>
        {
            switch (how)
            {
                case "unstrapped":
                    Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer, true), Is.True, "The user could not unstrap.");
                    break;
                case "seat deleted":
                    SEntMan.DeleteEntity(seat);
                    break;
                default:
                    SEntMan.DeleteEntity(rig.Vehicle);
                    break;
            }
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(SPlayer), Is.False, "The gunner did not survive leaving the seat.");

            var after = ReadEye(SPlayer);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleGunnerViewUserComponent>(SPlayer), Is.False, "The gunner kept the wider view.");
                Assert.That(after.PvsScale, Is.EqualTo(before.PvsScale).Within(0.001f), "The gunner's PVS box stayed wide after they left the seat.");
                Assert.That(after.TargetZoom, Is.EqualTo(before.TargetZoom), "Leaving the seat changed the gunner's zoom.");
                Assert.That(after.Zoom, Is.EqualTo(before.Zoom));
            }
        });
    }

    /// <summary>
    /// A turret or gun that is only carried on the vehicle, in a crate loaded on it, is left in the crate; so is a turret gun
    /// with no attachment component that sits in a turret's slot. Only mounted parts are re-posed, and only under the parent
    /// that holds them.
    /// </summary>
    [Test]
    public async Task ATurretPartThatIsNotMountedIsLeftWhereItIs()
    {
        var rig = await BuildRig(VehicleCargo);
        EntityUid crate = default, carried = default, plain = default, bareTurret = default;

        await Server.WaitPost(() =>
        {
            var spot = new EntityCoordinates(MapData.Grid.Owner, VehicleSpot + new Vector2(0f, 3f));
            var slots = SEntMan.System<ItemSlotsSystem>();
            var containers = SEntMan.System<SharedContainerSystem>();

            crate = SEntMan.SpawnEntity("CrateGeneric", spot);
            carried = SEntMan.SpawnEntity("WFWeaponsTestTurret", spot);
            Assert.That(containers.Insert(carried, SEntMan.GetComponent<ServerEntityStorage>(crate).Contents), Is.True, "The turret did not go in the crate.");
            Assert.That(slots.TryInsert(rig.Vehicle, "cargo", crate, null), Is.True, "The crate did not load on the vehicle.");

            bareTurret = slots.GetItemOrNull(rig.Vehicle, "turret-bare") ?? EntityUid.Invalid;
            Assert.That(bareTurret, Is.Not.EqualTo(EntityUid.Invalid), "The bare turret was not fitted.");

            plain = SEntMan.SpawnEntity("WFWeaponsTestPlainGun", spot);
            Assert.That(slots.TryInsert(bareTurret, "turret-cannon", plain, null), Is.True, "The plain gun did not go in the turret.");
        });
        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var turrets = SEntMan.System<VehicleTurretSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(containers.TryGetContainingContainer((carried, null), out var holder) ? holder.Owner : EntityUid.Invalid, Is.EqualTo(crate),
                    "The carried turret was pulled out of its crate.");
                Assert.That(Transform.GetParentUid(carried), Is.EqualTo(crate));
                Assert.That(slots.GetItemOrNull(bareTurret, "turret-cannon"), Is.EqualTo(plain), "The plain gun was pulled out of the turret's slot.");
                Assert.That(Transform.GetParentUid(plain), Is.EqualTo(bareTurret));
                Assert.That(slots.GetItemOrNull(rig.Vehicle, "turret"), Is.EqualTo(rig.Turret), "The mounted turret left its slot.");
                Assert.That(turrets.TryGetVehicle(carried, out _), Is.False, "A carried turret counts as mounted.");
                Assert.That(turrets.TryGetVehicle(rig.Gun, out var vehicle) && vehicle == rig.Vehicle, Is.True, "A mounted gun no longer finds its vehicle.");
            }
        });
    }

    /// <summary>
    /// A client does not trust the aim point it is sent: one that names no entity, one that is not a number and one on another
    /// map are dropped without a log line or a turn, and a good one still turns the turret.
    /// </summary>
    [Test]
    public async Task ABadAimPointNeitherLogsNorTurnsTheTurret()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));
        await Server.WaitAssertion(() => PerformAction(SPlayer, SelectActions(SPlayer).Single()));
        await RunTicks(3);

        var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var interiorGrid = SEntMan.GetNetEntity(SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid);
        var vehiclePos = Vector2.Zero;
        await Server.WaitPost(() => vehiclePos = Transform.GetWorldPosition(rig.Vehicle));

        async Task Aim(NetCoordinates coordinates)
        {
            await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(new VehicleTurretRotateEvent
            {
                Turret = netGun,
                Coordinates = coordinates,
            }));
            await RunTicks(3);
        }

        NetCoordinates OnGrid(Vector2 worldPoint) => SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(worldPoint, MapId)));

        // A good aim east turns the turret, so the later checks mean something.
        await Aim(OnGrid(vehiclePos + new Vector2(6f, 0f)));
        var aimed = Angle.Zero;
        await Server.WaitAssertion(() =>
        {
            aimed = turret.TargetRotation;
            Assert.That(aimed, Is.Not.EqualTo(Angle.Zero), "The control aim did not turn the turret.");
        });

        await Aim(new NetCoordinates(NetEntity.Invalid, new Vector2(3f, 3f)));
        await Aim(new NetCoordinates(SEntMan.GetNetEntity(MapData.Grid.Owner), new Vector2(float.NaN, float.NaN)));
        await Aim(new NetCoordinates(SEntMan.GetNetEntity(MapData.Grid.Owner), new Vector2(float.PositiveInfinity, 2f)));
        await Aim(new NetCoordinates(interiorGrid, new Vector2(2.5f, 2.5f)));

        await Server.WaitAssertion(() =>
        {
            Assert.That(turret.TargetRotation, Is.EqualTo(aimed), "A bad aim point turned the turret.");
        });

        // And the handler is still alive for a good one.
        await Aim(OnGrid(vehiclePos + new Vector2(0f, 6f)));
        await Server.WaitAssertion(() =>
        {
            Assert.That(turret.TargetRotation, Is.Not.EqualTo(aimed), "The turret stopped taking aim points.");
        });
    }

    /// <summary>
    /// A request to fire that the first pass refused (the turret is off aim) is refused when the client replays it, as it does
    /// each time a server state arrives: no round is spent and no cooldown is left in the predicted gun.
    /// </summary>
    [Test]
    public async Task AReplayedShotIsRefusedAsItsFirstPassWas()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() =>
        {
            PerformAction(SPlayer, SelectActions(SPlayer).Single());
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
        });
        await RunTicks(5);

        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var vehiclePos = Vector2.Zero;
        await Server.WaitPost(() => vehiclePos = Transform.GetWorldPosition(rig.Vehicle));

        // East of a turret that rests facing south: well off aim, and the server has been told nothing.
        var netAim = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(vehiclePos + new Vector2(6f, 0f), MapId)));
        var timing = Client.ResolveDependency<IClientGameTiming>();

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True, "The gunner's client never received the cannon.");
            var cGun = foundGun!.Value;
            var guns = CEntMan.System<ClientGunSystem>();
            var gun = CEntMan.GetComponent<GunComponent>(cGun);

            int Rounds()
            {
                var ammo = new GetAmmoCountEvent();
                CEntMan.EventBus.RaiseLocalEvent(cGun, ref ammo);
                return ammo.Count;
            }

            var roundsBefore = Rounds();
            var nextFireBefore = gun.NextFire;

            Assert.That(guns.ShootRequested(netGun, netAim, null, null, ClientSession, true), Is.Null, "The first pass of an off-aim request fired.");
            Assert.That(Rounds(), Is.EqualTo(roundsBefore), "The first pass spent a round.");

            // The same request again at the same tick, as a replay of it after a server state.
            using (timing.StartPastPredictionArea())
            {
                Assert.That(guns.ShootRequested(netGun, netAim, null, null, ClientSession, true), Is.Null, "The replay of an off-aim request fired.");
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Rounds(), Is.EqualTo(roundsBefore), "The replay spent a round the first pass had refused.");
                Assert.That(gun.NextFire, Is.EqualTo(nextFireBefore).Or.LessThanOrEqualTo(CTiming.CurTime), "The replay left the predicted gun on cooldown.");
            }
        });
    }

    /// <summary>
    /// The aim point a gunner sends rides on the grid under the gun, not on the gun, the turret or the vehicle: those move
    /// with a pose the gunner's client does not predict, and the server would rebuild the point from a newer one. A hand-held
    /// gun still aims relative to its user.
    /// </summary>
    [Test]
    public async Task AMountedGunsAimPointDoesNotRideOnTheVehicle()
    {
        var rig = await BuildRig();
        var netVehicle = SEntMan.GetNetEntity(rig.Vehicle);
        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var mouse = Vector2.Zero;
        await Server.WaitPost(() => mouse = Transform.GetWorldPosition(rig.Vehicle) + new Vector2(5f, -2f));

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netVehicle, out var foundVehicle), Is.True, "The client never received the vehicle.");
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True, "The client never received the cannon.");
            var cVehicle = foundVehicle!.Value;
            var cGun = foundGun!.Value;
            var guns = CEntMan.System<ClientGunSystem>();
            var xforms = CEntMan.System<SharedTransformSystem>();
            var grid = CEntMan.GetComponent<TransformComponent>(cVehicle).GridUid!.Value;
            var point = new MapCoordinates(mouse, xforms.GetMapId(cVehicle));

            var aim = guns.GetAimCoordinates(cGun, CPlayer, point);
            Assert.That(aim.EntityId, Is.EqualTo(grid), "A mounted gun's aim point is not measured from the grid under it.");
            Assert.That(Vector2.Distance(xforms.ToMapCoordinates(aim).Position, mouse), Is.LessThan(0.001f));

            // Where the client believes the vehicle is changes nothing about the point it sends for the same cursor.
            xforms.SetLocalPosition(cVehicle, CEntMan.GetComponent<TransformComponent>(cVehicle).LocalPosition + new Vector2(3f, 1f));
            xforms.SetLocalRotation(cVehicle, Angle.FromDegrees(90));
            var moved = guns.GetAimCoordinates(cGun, CPlayer, point);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(moved.EntityId, Is.EqualTo(grid));
                Assert.That(moved.Position, Is.EqualTo(aim.Position), "The aim point rode on the vehicle's pose.");
            }

            // A gun without the component aims from its user.
            var held = CEntMan.SpawnEntity(null, new EntityCoordinates(grid, Vector2.Zero));
            Assert.That(guns.GetAimCoordinates(held, CPlayer, point).EntityId, Is.EqualTo(CPlayer), "A hand-held gun no longer aims from its user.");
        });
    }

    /// <summary>
    /// A turret's overlay entity belongs to the turret: taking the cannon out of the turret leaves it alone (it used to be
    /// deleted and drawn again), and taking the turret out of its slot takes it away.
    /// </summary>
    [Test]
    public async Task TakingTheCannonOutOfATurretKeepsItsOverlayButTheTurretLeavingDropsIt()
    {
        var rig = await BuildRig();

        await Server.WaitPost(() =>
        {
            var turret = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret);
#pragma warning disable RA0002
            turret.ShowOverlay = true;
            turret.OverlayRsi = "_WF/Vehicles/humvee.rsi";
            turret.OverlayState = "humveeturret_0";
            turret.OverlayDamagedState = "humveeturret_1";
#pragma warning restore RA0002
            SEntMan.Dirty(rig.Turret, turret);
        });
        await RunTicks(5);

        var visual = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            visual = SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret).VisualEntity ?? EntityUid.Invalid;
            Assert.That(visual, Is.Not.EqualTo(EntityUid.Invalid), "The server made no overlay entity for the turret.");
        });

        await Server.WaitPost(() => Assert.That(RemoveFromHolder(rig.Gun), Is.True, "The cannon would not come out of the turret."));
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(visual), Is.False, "Taking the cannon out deleted the turret's overlay.");
                Assert.That(SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret).VisualEntity, Is.EqualTo(visual));
            }
        });

        await Server.WaitPost(() => Assert.That(RemoveFromHolder(rig.Turret), Is.True, "The turret would not come out of its slot."));
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(visual), Is.True, "The overlay outlived the turret's mount.");
                Assert.That(SEntMan.GetComponent<VehicleTurretComponent>(rig.Turret).VisualEntity, Is.Null);
            }
        });
    }

    /// <summary>Takes an entity out of whatever container holds it, as an admin or a wreck would.</summary>
    private bool RemoveFromHolder(EntityUid entity)
    {
        var containers = SEntMan.System<SharedContainerSystem>();
        return containers.TryGetContainingContainer((entity, null), out var holder) && containers.Remove(entity, holder);
    }

    /// <summary>
    /// A gunner in a vehicle whose frame is destroyed is refused on their own client as well: no round is spent and no
    /// cooldown left in the predicted gun, instead of a phantom shot the server then cancels. With the frame mended the same
    /// request fires.
    /// </summary>
    [Test]
    public async Task ADestroyedFrameRefusesTheShotOnTheGunnersClientToo()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, Seat, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() =>
        {
            PerformAction(SPlayer, SelectActions(SPlayer).Single());
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
        });
        await RunTicks(5);

        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var vehiclePos = Vector2.Zero;
        await Server.WaitPost(() => vehiclePos = Transform.GetWorldPosition(rig.Vehicle));

        // South of a turret that rests facing south: on aim.
        var netAim = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(vehiclePos + new Vector2(0f, -6f), MapId)));

        async Task SetFrame(float integrity)
        {
            await Server.WaitPost(() =>
            {
                var frame = SEntMan.GetComponent<HardpointIntegrityComponent>(rig.Vehicle);
                frame.Integrity = integrity;
                SEntMan.Dirty(rig.Vehicle, frame);
            });
            await RunTicks(5);
        }

        int Rounds(EntityUid cGun)
        {
            var ammo = new GetAmmoCountEvent();
            CEntMan.EventBus.RaiseLocalEvent(cGun, ref ammo);
            return ammo.Count;
        }

        await SetFrame(0f);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True, "The gunner's client never received the cannon.");
            var cGun = foundGun!.Value;
            var gun = CEntMan.GetComponent<GunComponent>(cGun);
            var rounds = Rounds(cGun);

            CEntMan.System<ClientGunSystem>().ShootRequested(netGun, netAim, null, null, ClientSession, true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Rounds(cGun), Is.EqualTo(rounds), "The client spent a round on a shot from a destroyed vehicle.");
                Assert.That(gun.NextFire, Is.LessThanOrEqualTo(CTiming.CurTime), "The client left the gun on cooldown after a refused shot.");
            }
        });

        await SetFrame(1000f);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True);
            var cGun = foundGun!.Value;
            var rounds = Rounds(cGun);

            var copies = CEntMan.System<ClientGunSystem>().ShootRequested(netGun, netAim, null, null, ClientSession, true);

            Assert.That(Rounds(cGun), Is.EqualTo(rounds - 1), "The mended vehicle's shot did not fire on the client.");

            // The server never heard of this shot, so its predicted copy is dropped rather than left to fly.
            foreach (var id in copies ?? new List<int>())
            {
                if (id != 0)
                    CEntMan.DeleteEntity(new EntityUid(id));
            }
        });

        // Let the dropped copy's deletion settle on both sides before the map goes.
        await RunTicks(20);
    }
}
