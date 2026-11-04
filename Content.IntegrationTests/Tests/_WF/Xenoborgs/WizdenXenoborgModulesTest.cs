#nullable enable
using Content.Server.Silicons.Borgs;
using Content.Shared._Mono.Emp;
using Content.Shared._WF.Xenoborgs;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.Movement.Components;
using Content.Server._WF.Xenoborgs;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Projectiles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenoborgs;

/// <summary>
/// The xenoborg modules ported from wizden: weapon slot blacklists, component-granting modules, self-refilling ammo,
/// the nanite crystal's per-target damage, the chassis lending its EMP resistance to its cell, and the crystal extractor.
/// </summary>
[TestFixture]
[TestOf(typeof(BorgModuleExtrasSystem))]
public sealed class WizdenXenoborgModulesTest
{
    private const string Heavy = "XenoborgChassisHeavy";
    private const string Engineer = "XenoborgChassisEngineer";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFXenoborgRefillTestGun
  components:
  - type: Gun
  - type: BallisticAmmoProvider
    capacity: 2
    proto: null
  - type: BallisticAmmoSelfRefiller
    autoRefillRate: 1s
    ammoProto: CartridgeNanite
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container
        ents: []
";

    /// <summary>
    /// A heavy holds one primary weapon, one secondary weapon and one overlay module; Mono's default defense module counts as none.
    /// </summary>
    [Test]
    public async Task WeaponSlotsBlockSecondModule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var borg = entMan.System<BorgSystem>();
        var container = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chassis = entMan.SpawnEntity(Heavy, map.GridCoords);
            var chassisComp = entMan.GetComponent<BorgChassisComponent>(chassis);

            bool TryInsert(string proto)
            {
                var module = entMan.SpawnEntity(proto, map.GridCoords);
                if (!borg.CanInsertModule(chassis, module, chassisComp))
                    return false;

                container.Insert(module, chassisComp.ModuleContainer);
                return true;
            }

            Assert.Multiple(() =>
            {
                Assert.That(TryInsert("XenoborgModuleLaser"), Is.True, "A primary weapon fits next to the default defense module.");
                Assert.That(TryInsert("XenoborgModuleBurstFireLaser"), Is.False, "A second primary weapon is blocked.");
                Assert.That(TryInsert("XenoborgModuleRocket"), Is.True, "A secondary weapon fits next to the primary.");
                Assert.That(TryInsert("XenoborgModuleGuidedMissileLauncher"), Is.False, "A second secondary weapon is blocked.");
                Assert.That(TryInsert("XenoborgModuleHeavyRadioactive"), Is.False, "A second sprite overlay module is blocked.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Installing the zap and jump modules grants their components, and the jump action; uninstalling takes them back.
    /// </summary>
    [Test]
    public async Task ComponentModulesGrantAndRemove()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var borg = entMan.System<BorgSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chassis = entMan.SpawnEntity(Engineer, map.GridCoords);
            var zap = entMan.SpawnEntity("XenoborgModuleZap", map.GridCoords);
            var jump = entMan.SpawnEntity("XenoborgModuleJump", map.GridCoords);
            Assert.That(entMan.HasComponent<ElectrifiedComponent>(chassis), Is.False);

            borg.InstallModule(chassis, zap, null);
            borg.InstallModule(chassis, jump, null);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.TryGetComponent<ElectrifiedComponent>(chassis, out var electrified), Is.True);
                Assert.That(electrified?.ShockDelay, Is.EqualTo(TimeSpan.FromSeconds(6)));
                Assert.That(entMan.GetComponent<JumpAbilityComponent>(chassis).ActionEntity, Is.Not.Null, "The jump module grants its action.");
            });

            borg.UninstallModule(chassis, zap, null);
            borg.UninstallModule(chassis, jump, null);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ElectrifiedComponent>(chassis), Is.False);
                Assert.That(entMan.HasComponent<JumpAbilityComponent>(chassis), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An empty self-refilling gun gains a round per refill period and stops at its capacity.
    /// </summary>
    [Test]
    public async Task SelfRefillerFillsToCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default;
        await server.WaitPost(() => gun = entMan.SpawnEntity("WFXenoborgRefillTestGun", map.GridCoords));

        int Count()
        {
            var ev = new GetAmmoCountEvent();
            entMan.EventBus.RaiseLocalEvent(gun, ref ev);
            return ev.Count;
        }

        await server.WaitAssertion(() => Assert.That(Count(), Is.Zero));
        await pair.RunSeconds(4);
        await server.WaitAssertion(() => Assert.That(Count(), Is.EqualTo(2), "It refills up to its capacity of 2 and no further."));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A nanite crystal repairs a borg it hits and keeps its normal damage for anything else.
    /// </summary>
    [Test]
    public async Task NaniteCrystalRepairsBorgs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bullet = entMan.SpawnEntity("BulletNanite", map.GridCoords);
            var chassis = entMan.SpawnEntity(Heavy, map.GridCoords);
            var crate = entMan.SpawnEntity("CrateGenericSteel", map.GridCoords);
            var damage = entMan.GetComponent<ProjectileComponent>(bullet).Damage;

            var borgHit = new ProjectileHitEvent(new DamageSpecifier(damage), chassis);
            entMan.EventBus.RaiseLocalEvent(bullet, ref borgHit);
            var crateHit = new ProjectileHitEvent(new DamageSpecifier(damage), crate);
            entMan.EventBus.RaiseLocalEvent(bullet, ref crateHit);

            Assert.Multiple(() =>
            {
                Assert.That(borgHit.Damage.GetTotal().Float(), Is.LessThan(0f), "A borg is repaired.");
                Assert.That(crateHit.Damage.GetTotal().Float(), Is.EqualTo(damage.GetTotal().Float()), "Anything else takes the normal damage.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A xenoborg's cell borrows the chassis's EMP resistance while slotted, and gives it back when taken out.
    /// </summary>
    [Test]
    public async Task ChassisLendsEmpResistanceToCell()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var cells = entMan.System<PowerCellSystem>();
        var container = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chassis = entMan.SpawnEntity(Heavy, map.GridCoords);
            Assert.That(cells.TryGetBatteryFromSlot(chassis, out var cell, out _), Is.True);
            var resistance = entMan.GetComponent<EmpResistanceComponent>(cell!.Value);
            Assert.That(resistance.Coefficient, Is.Zero, "The slotted cell takes the chassis's full resistance.");

            var slot = entMan.GetComponent<PowerCellSlotComponent>(chassis);
            var cellContainer = container.GetContainer(chassis, slot.CellSlotId);
            container.Remove(cell.Value, cellContainer);
            Assert.That(resistance.Coefficient, Is.GreaterThan(0f), "The cell goes back to its own resistance.");

            var bareCell = entMan.SpawnEntity("PowerCellMedium", map.GridCoords);
            Assert.That(entMan.HasComponent<EmpResistanceComponent>(bareCell), Is.False);
            container.Insert(bareCell, cellContainer);
            Assert.That(entMan.GetComponent<EmpResistanceComponent>(bareCell).Coefficient, Is.Zero, "A cell without resistance borrows it.");
            container.Remove(bareCell, cellContainer);
            Assert.That(entMan.HasComponent<EmpResistanceComponent>(bareCell), Is.False, "And gives it back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The extractor only anchors on a station's grid, and turns a full battery into a crystal.
    /// </summary>
    [Test]
    public async Task ExtractorAnchorsOnStationsAndGrowsCrystals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var battery = entMan.System<BatterySystem>();
        var extractorSystem = entMan.System<XenoborgExtractorSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var extractor = entMan.SpawnEntity("XenoborgExtractor", map.GridCoords);
            var user = entMan.SpawnEntity(null, map.GridCoords);

            var offStation = new AnchorAttemptEvent(user, user);
            entMan.EventBus.RaiseLocalEvent(extractor, offStation);
            Assert.That(offStation.Cancelled, Is.True, "A grid without a station refuses the extractor.");

            entMan.AddComponent<StationMemberComponent>(map.Grid).Station = entMan.Spawn();
            var onStation = new AnchorAttemptEvent(user, user);
            entMan.EventBus.RaiseLocalEvent(extractor, onStation);
            Assert.That(onStation.Cancelled, Is.False, "A station's grid takes it.");

            entMan.System<SharedTransformSystem>().AnchorEntity(extractor);
            var batteryComp = entMan.GetComponent<BatteryComponent>(extractor);
            battery.SetCharge(extractor, batteryComp.MaxCharge - 1, batteryComp);
            entMan.GetComponent<PowerConsumerComponent>(extractor).NetworkLoad.ReceivingPower = 1000;
            extractorSystem.Update(1f);

            Assert.That(batteryComp.CurrentCharge, Is.Zero, "Filling the battery spends it.");
            var crystals = 0;
            var query = entMan.EntityQueryEnumerator<StackComponent>();
            while (query.MoveNext(out _, out var stack))
            {
                if (stack.StackTypeId == "XenoborgCrystal")
                    crystals += stack.Count;
            }
            Assert.That(crystals, Is.EqualTo(1), "A full battery grows one crystal.");
        });

        await pair.CleanReturnAsync();
    }
}
