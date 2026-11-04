#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Limbs;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Access.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tiles;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The open items: how the Undying ends, the elite and roster gaps, the sanctuary radius.</summary>
[TestFixture]
public sealed class OpenItemsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFOpenItemsBullet
  parent: BaseBullet

- type: entity
  id: WFOpenItemsCartridge
  components:
  - type: CartridgeAmmo
    proto: WFOpenItemsBullet
    deleteOnSpawn: true

- type: entity
  id: WFOpenItemsGun
  components:
  - type: Gun
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFOpenItemsCartridge
    capacity: 5
";

    private static void Pound(TestPair pair, EntityUid uid, string type, float amount)
    {
        var damage = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
        pair.Server.EntMan.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }

    [Test]
    public async Task TheUndyingEndsWhenItsMonolithGoesWhenBurntOrHeldOnAStasisBed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, lost = default, burnt = default, held = default, bed = default;

        EntityUid Spawn(float x)
        {
            var undying = entMan.SpawnEntity("WFRemadeUndying", At(map, x, 0));
            entMan.GetComponent<WFRisesComponent>(undying).RiseDelay = 4f;
            return undying;
        }

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 10));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            lost = Spawn(-10);
            burnt = Spawn(-5);
            held = Spawn(5);
            directors.Enlist(lost, scion);
            directors.Enlist(burnt, scion);
            bed = entMan.SpawnEntity("StasisBed", At(map, 5, 0));
            entMan.GetComponent<ApcPowerReceiverComponent>(bed).NeedsPower = false;
            foreach (var undying in new[] { lost, burnt, held })
                Pound(pair, undying, "Blunt", 5000);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            Assert.That(mobState.IsDead(lost) && mobState.IsDead(burnt) && mobState.IsDead(held), Is.True, "lethal damage didn't put them all down.");

            // Fire while it lies there, and its Monolith going.
            Pound(pair, burnt, "Heat", 400);
            entMan.DeleteEntity(scion);
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRisesComponent>(lost).Final, Is.True, "it will get up with its Monolith gone.");
            Assert.That(mobState.IsDead(lost), Is.True, "it got up with its Monolith gone.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(burnt).Final, Is.True, "it will get up after burning.");
            Assert.That(mobState.IsDead(burnt), Is.True, "it got up after burning.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(held).Held, Is.True, "the stasis bed didn't hold it.");
            Assert.That(mobState.IsDead(held), Is.True, "it got up off a powered stasis bed.");

            // Cut the bed's power and it rises.
            entMan.GetComponent<ApcPowerReceiverComponent>(bed).NeedsPower = true;
            entMan.GetComponent<ApcPowerReceiverComponent>(bed).PowerDisabled = true;
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(held), Is.True, "it stayed down after the bed lost its power.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStarvingUndyingWastesAwayInAboutTwoMinutes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, undying = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 10));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            undying = entMan.SpawnEntity("WFRemadeUndying", At(map, 5, 0));
            directors.Enlist(undying, scion);
            entMan.DeleteEntity(scion);
        });

        await pair.RunSeconds(150f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<MobStateSystem>().IsDead(undying), Is.True, "the Undying outlived its Monolith by over two minutes.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(undying).Final, Is.True, "it will get up again.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATwitcherSlipsBeamsAsWellAsBullets()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            var shooter = entMan.SpawnEntity("MobCarp", At(map, 8, 0));
            var twitcher = entMan.SpawnEntity("WFRemadeTwitcher", At(map, 0, 0));
            var pool = entMan.GetComponent<WFDodgePoolComponent>(twitcher);

            void Fire()
            {
                var beam = entMan.SpawnEntity("RedLaser", At(map, 8, 0));
                var ev = new HitscanTraceEvent
                {
                    FromCoordinates = At(map, 8, 0),
                    ShotDirection = new Vector2(-1f, 0f),
                    Gun = shooter,
                    Shooter = shooter,
                    Target = twitcher,
                };
                entMan.EventBus.RaiseLocalEvent(beam, ref ev);
            }

            var before = LimbsTest.Total(pair, twitcher);
            Fire();
            Assert.That(pool.Dodged, Is.EqualTo(1), "a full pool didn't slip a beam.");
            Assert.That(LimbsTest.Total(pair, twitcher), Is.EqualTo(before), "a slipped beam still hurt.");

            pool.Pool = 0f;
            entMan.System<SharedTransformSystem>().SetCoordinates(twitcher, At(map, 0, 0));
            before = LimbsTest.Total(pair, twitcher);
            Fire();
            Assert.That(pool.Dodged, Is.EqualTo(1), "an empty pool slipped a beam.");
            Assert.That(LimbsTest.Total(pair, twitcher), Is.GreaterThan(before), "the beam didn't hurt it.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASlasherSidestepsAShotThatGoesPastItAndAHurtHunterShowsALimbGrowing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        EntityUid slasher = default, gun = default, hunter = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, -12, 5));
            gun = entMan.SpawnEntity("WFOpenItemsGun", At(map, -15, 0));
            slasher = entMan.SpawnEntity("WFRemadeSlasher", At(map, 0, 1));
            hunter = entMan.SpawnEntity("WFRemadeHunter", At(map, 8, 8));
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            // Fired along the row y = 0.5; it stands at y = 1.5, a metre to the side.
            var before = transforms.GetWorldPosition(slasher);
            entMan.System<SharedGunSystem>().AttemptShoot(gun, gun, entMan.GetComponent<GunComponent>(gun), At(map, 20, 0));
            Assert.That(Math.Abs(transforms.GetWorldPosition(slasher).Y - before.Y) + Math.Abs(transforms.GetWorldPosition(slasher).X - before.X),
                Is.GreaterThan(0.5f), "it didn't step aside from a shot going past.");

            Assert.That(limbs.TrySever(hunter, "l_arm"), Is.True);
        });

        // The limb shows growing before it is whole.
        await pair.RunSeconds(14f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFRemadeLimbsComponent>(hunter);
            Assert.That(comp.Severed, Does.Contain("l_arm"));
            Assert.That(comp.Growing, Does.Contain("l_arm"), "the hunter's limb wasn't growing back yet.");
            Assert.That(comp.Limbs.Find(limb => limb.Id == "l_arm")!.Regen, Is.EqualTo("l_arm_regen"));
        });

        await pair.RunSeconds(10f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFRemadeLimbsComponent>(hunter);
            Assert.That(comp.Severed, Is.Empty, "the limb never grew back.");
            Assert.That(comp.Growing, Is.Empty, "it is still showing as growing.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABruteFlingsWhoeverItHitsAndALurkerDiesWithoutItsTentacles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        var mobState = entMan.System<MobStateSystem>();
        EntityUid brute = default, victim = default, lurker = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            brute = entMan.SpawnEntity("WFRemadeBrute", At(map, 0, 0));
            victim = entMan.SpawnEntity("MobCarp", At(map, 1, 0));
            lurker = entMan.SpawnEntity("WFRemadeLurker", At(map, 10, 10));
            entMan.EventBus.RaiseLocalEvent(brute, new MeleeHitEvent(new List<EntityUid> { victim }, brute, brute, new DamageSpecifier(), null), false);
        });
        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            Assert.That((transforms.GetWorldPosition(victim) - transforms.GetWorldPosition(brute)).Length(), Is.GreaterThan(2.5f), "the brute's blow didn't throw its target.");

            Assert.That(limbs.TrySever(lurker, "tentacle_1"), Is.True);
            Assert.That(limbs.TrySever(lurker, "tentacle_2"), Is.True);
            Assert.That(mobState.IsAlive(lurker), Is.True, "it died with a tentacle left.");
            Assert.That(limbs.TrySever(lurker, "tentacle_3"), Is.True);
            Assert.That(mobState.IsDead(lurker), Is.True, "it lived on without any tentacle.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADividerHeadThatChokesSomeoneToDeathTurnsTheBodyAndIsGone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var abilities = entMan.System<WFAbilitySystem>();
        EntityUid head = default, human = default, scion = default;

        await server.WaitPost(() =>
        {
            // The head holds on by its latch in play; with no mind running here it just has to stay put.
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCEnabled, false);
            LayDeck(pair, map);
            // Far off, so the head has nobody in sight to leave the body for.
            SpawnCrewman(pair, At(map, -20, -20));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            human = entMan.SpawnEntity("MobHuman", At(map, 0, 0));
            head = entMan.SpawnEntity("WFRemadeDividerHead", At(map, 0, 0));
            // They sit on each other; keep them from shoving one another out of the head's reach.
            entMan.System<WFEliteSystem>().SetSolid(head, false);
            entMan.System<WFEliteSystem>().SetSolid(human, false);

            // Dead already, with the head holding on: it turns the body.
            mobState.ChangeMobState(human, MobState.Dead);
            Assert.That(abilities.Turn(human, head), Is.True, "the head couldn't start turning a dead body.");
        });

        await RunUntil(pair, () => entMan.Deleted(human) || !entMan.HasComponent<WFRemadeConvertingComponent>(human), 40);
        await server.WaitAssertion(() =>
        {
            WFRemadeConvertingComponent? throes = null;
            if (!entMan.Deleted(human))
                entMan.TryGetComponent(human, out throes);

            var diag = $"converting {throes != null} ends {throes?.EndsAt} now {server.ResolveDependency<IGameTiming>().CurTime}, head at {(entMan.Deleted(head) ? "gone" : entMan.System<SharedTransformSystem>().GetWorldPosition(head).ToString())}, body at {(entMan.Deleted(human) ? "gone" : entMan.System<SharedTransformSystem>().GetWorldPosition(human).ToString())}";
            Assert.That(entMan.Deleted(human) || entMan.GetComponent<MetaDataComponent>(human).EntityLifeStage >= EntityLifeStage.Terminating, Is.True, "the body never burst: " + diag);
            Assert.That(entMan.Deleted(head) || entMan.GetComponent<MetaDataComponent>(head).EntityLifeStage >= EntityLifeStage.Terminating, Is.True, "the head didn't go into the body.");
            Assert.That(entMan.AllEntities<WFRemadeComponent>().Count(ent => ent.Owner != head && !entMan.Deleted(ent) && entMan.GetComponent<MetaDataComponent>(ent).EntityLifeStage < EntityLifeStage.Terminating),
                Is.GreaterThan(0), "nothing stood up from the body.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADividerHeadWithNobodyToFightGoesForABodyAndTurnsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid head = default, human = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            // Out of sight, but within the 32 m sleep range of the whole walk.
            SpawnCrewman(pair, At(map, 4, -24));
            human = entMan.SpawnEntity("MobHuman", At(map, 8, 0));
            entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Dead);
            head = entMan.SpawnEntity("WFRemadeDividerHead", At(map, 0, 0));
        });

        await RunUntil(pair, () => entMan.Deleted(human) || entMan.HasComponent<WFRemadeConvertingComponent>(human), 60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(human) || entMan.HasComponent<WFRemadeConvertingComponent>(human), Is.True,
                $"the head never started on the body: {Describe(pair, head)}.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACorpseOnlyTurnsIntoWhatItsSexArmourPoisonAndJobAllow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var inventory = entMan.System<InventorySystem>();

        HashSet<string> Can(EntityUid corpse)
        {
            return conversion.Weights(corpse).Keys.Select(mob => mob.ID).ToHashSet();
        }

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            EntityUid Corpse()
            {
                var corpse = entMan.SpawnEntity("MobHuman", At(map, 0, 0));
                mobState.ChangeMobState(corpse, MobState.Dead);
                return corpse;
            }

            // A man: exploders and a leaper, never a spitter, a puker or (no job) more than a long shot of a divider.
            var man = Corpse();
            entMan.GetComponent<HumanoidAppearanceComponent>(man).Sex = Sex.Male;
            var can = Can(man);
            Assert.That(can, Does.Contain("WFRemadeExploder").And.Contain("WFRemadeLeaper").And.Contain("WFRemadeSlasher"));
            Assert.That(can, Does.Not.Contain("WFRemadeSpitter"), "a man turned into a spitter.");
            Assert.That(can, Does.Not.Contain("WFRemadePuker"), "an unpoisoned body turned into a puker.");
            var divider = conversion.Weights(man).First(pair => pair.Key.ID == "WFRemadeDivider").Value;
            Assert.That(divider, Is.LessThan(0.5f), "a body with no job should rarely be a divider.");

            // A woman: a spitter, not an exploder.
            var woman = Corpse();
            entMan.GetComponent<HumanoidAppearanceComponent>(woman).Sex = Sex.Female;
            can = Can(woman);
            Assert.That(can, Does.Contain("WFRemadeSpitter"));
            Assert.That(can, Does.Not.Contain("WFRemadeExploder").And.Not.Contain("WFRemadeExploderEnhanced"));

            // No sex: either.
            var none = Corpse();
            entMan.GetComponent<HumanoidAppearanceComponent>(none).Sex = Sex.Unsexed;
            can = Can(none);
            Assert.That(can, Does.Contain("WFRemadeSpitter").And.Contain("WFRemadeExploder"));

            // Heavy armour: no leaper.
            var armoured = Corpse();
            var vest = entMan.SpawnEntity("ClothingOuterArmorBulletproof", At(map, 0, 0));
            Assert.That(inventory.TryEquip(armoured, vest, "outerClothing", force: true), Is.True, "the test vest wouldn't go on.");
            Assert.That(Can(armoured), Does.Not.Contain("WFRemadeLeaper"), "a body in a bulletproof vest turned into a leaper.");

            // Poison: a puker.
            var poisoned = Corpse();
            Pound(pair, poisoned, "Poison", 150);
            Assert.That(Can(poisoned), Does.Contain("WFRemadePuker"), "a poisoned body can't turn into a puker.");

            // A job from the ID: a divider at the full weight, from the right department.
            var engineer = Corpse();
            var card = entMan.SpawnEntity("PassengerIDCard", At(map, 0, 0));
            entMan.GetComponent<IdCardComponent>(card).JobDepartments = new List<Robust.Shared.Prototypes.ProtoId<Content.Shared.Roles.DepartmentPrototype>> { "Engineering" };
            Assert.That(inventory.TryEquip(engineer, card, "id", force: true), Is.True, "the test card wouldn't go on.");
            var weight = conversion.Weights(engineer).First(pair => pair.Key.ID == "WFRemadeDivider").Value;
            Assert.That(weight, Is.GreaterThan(2f), "an engineer's body should be a divider at the full weight.");

            var janitor = Corpse();
            var civilian = entMan.SpawnEntity("PassengerIDCard", At(map, 0, 0));
            entMan.GetComponent<IdCardComponent>(civilian).JobDepartments = new List<Robust.Shared.Prototypes.ProtoId<Content.Shared.Roles.DepartmentPrototype>> { "Civilian" };
            inventory.TryEquip(janitor, civilian, "id", force: true);
            Assert.That(Can(janitor), Does.Not.Contain("WFRemadeDivider"), "a civilian turned into a divider.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NothingGrowsNearAMainStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var corruption = entMan.System<WFCorruptionSystem>();

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            entMan.EnsureComponent<Content.Server._WF.Remade.Corruption.WFRemadeSanctuaryComponent>(map.Grid.Owner);

            var near = maps.CreateGridEntity(map.MapId);
            maps.SetTile(near.Owner, near.Comp, Vector2i.Zero, new Tile(1));
            transforms.SetWorldPosition(near.Owner, new Vector2(300f, 0f));
            var far = maps.CreateGridEntity(map.MapId);
            maps.SetTile(far.Owner, far.Comp, Vector2i.Zero, new Tile(1));
            transforms.SetWorldPosition(far.Owner, new Vector2(900f, 0f));

            Assert.That(corruption.IsSanctuary(map.Grid.Owner), Is.True, "the station itself isn't a sanctuary.");
            Assert.That(corruption.IsSanctuary(near.Owner), Is.True, "a grid 300 m off a main station wasn't a sanctuary.");
            Assert.That(corruption.IsSanctuary(far.Owner), Is.False, "a grid 900 m off was.");
            Assert.That(corruption.IsSanctuary(null), Is.False);

            var adrift = entMan.SpawnEntity("MobCarp", new EntityCoordinates(map.MapUid, new Vector2(300f, 100f)));
            var distant = entMan.SpawnEntity("MobCarp", new EntityCoordinates(map.MapUid, new Vector2(900f, 100f)));
            Assert.That(corruption.IsSanctuaryAt(adrift), Is.True, "a Monolith adrift 300 m off wasn't barred.");
            Assert.That(corruption.IsSanctuaryAt(distant), Is.False);

            server.CfgMan.SetCVar(WFRemadeSanctuaryCVars.Radius, 100f);
            Assert.That(corruption.IsSanctuary(near.Owner), Is.False, "the radius cvar changed nothing.");
            Assert.That(corruption.IsSanctuary(map.Grid.Owner), Is.True, "the station stopped being one.");
        });
        await pair.CleanReturnAsync();
    }
}
