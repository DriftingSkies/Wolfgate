#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Lathe;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Events;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The Gifts: Matter, Flesh, Reach and shards, with the dials adding up and a sanctuary barring all of them.</summary>
[TestFixture]
public sealed class GiftsTest
{
    private const string Scion = "WFMonolithScion";

    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    /// <summary>Breathable air over the whole map: a human goes critical in the test vacuum even in godmode.</summary>
    private static void SetAir(RobustIntegrationTest.ServerIntegrationInstance server, TestMapData map)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
    }

    private static void Power(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid uid)
    {
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
        server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
    }

    /// <summary>Spawns a Scion with its Director removed, so a test sees only the Gifts.</summary>
    private static Entity<WFMonolithComponent> SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var uid = entMan.SpawnEntity(Scion, at);
        entMan.RemoveComponent<WFRemadeDirectorComponent>(uid);
        var comp = entMan.GetComponent<WFMonolithComponent>(uid);
        comp.Resonance = resonance;
        return (uid, comp);
    }

    private static void Kill(IEntityManager entMan, EntityUid body)
    {
        var brain = entMan.System<WolfmedLifeSystem>().GetBrainOrgan(body);
        Assert.That(brain, Is.Not.Null);
        entMan.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
    }

    private static void Hurt(IEntityManager entMan, EntityUid body, float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(amount);
        entMan.System<DamageableSystem>().TryChangeDamage(body, damage, true);
    }

    private static float Blunt(IEntityManager entMan, EntityUid body)
    {
        return entMan.GetComponent<DamageableComponent>(body).Damage.DamageDict.TryGetValue("Blunt", out var blunt) ? blunt.Float() : 0f;
    }

    private static float TotalDamage(IEntityManager entMan, EntityUid body)
    {
        return entMan.GetComponent<DamageableComponent>(body).Damage.GetTotal().Float();
    }

    private static EntityUid? Find(IEntityManager entMan, string prototype, EntityUid? except = null)
    {
        foreach (var ent in entMan.AllEntities<MetaDataComponent>())
        {
            if (ent.Owner != except && ent.Comp.EntityPrototype?.ID == prototype)
                return ent.Owner;
        }

        return null;
    }

    /// <summary>Prints a recipe on a lathe from start to finish and returns what it made.</summary>
    private static EntityUid Print(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid lathe, string recipeId, string product,
        EntityUid? except = null)
    {
        var lathes = server.System<LatheSystem>();
        var recipe = server.ProtoMan.Index<Content.Shared.Research.Prototypes.LatheRecipePrototype>(recipeId);
        var comp = server.EntMan.GetComponent<Content.Shared.Lathe.LatheComponent>(lathe);
        Assert.That(lathes.TryAddToQueue(lathe, recipe, 1, comp), Is.True, "the lathe wouldn't queue the recipe.");
        Assert.That(lathes.TryStartProducing(lathe, comp), Is.True, "the lathe wouldn't start printing.");
        lathes.FinishProducing(lathe);
        var made = Find(server.EntMan, product, except);
        Assert.That(made, Is.Not.Null, $"the lathe made no {product}.");
        return made!.Value;
    }

    [Test]
    public async Task MatterFillsASiloAndNothingFromItSells()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var pricing = entMan.System<PricingSystem>();
        var storage = entMan.System<SharedMaterialStorageSystem>();
        var monoliths = entMan.System<WFMonolithSystem>();
        EntityUid silo = default, lathe = default, cleanSilo = default, cleanLathe = default;
        Entity<WFMonolithComponent> scion = default;
        double baseline = 0;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            silo = entMan.SpawnEntity("MachineMaterialSilo", At(map, 3, 0));
            lathe = entMan.SpawnEntity("Autolathe", At(map, 3, 3));
            cleanSilo = entMan.SpawnEntity("MachineMaterialSilo", At(map, -12, 0));
            cleanLathe = entMan.SpawnEntity("Autolathe", At(map, -12, 3));
            foreach (var uid in new[] { silo, lathe, cleanSilo, cleanLathe })
            {
                Power(server, uid);
            }

            entMan.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(entMan.GetNetEntity(lathe)));
            entMan.EventBus.RaiseLocalEvent(cleanSilo, new ToggleOreSiloClientMessage(entMan.GetNetEntity(cleanLathe)));
            storage.TryChangeMaterialAmount(cleanSilo, "Steel", 2000);
            storage.TryChangeMaterialAmount(cleanSilo, "Plastic", 500);
            baseline = pricing.GetPrice(silo);
            monoliths.SetDial(scion, WFGift.Matter, 1f);
        });

        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var steel = storage.GetMaterialAmount(silo, "Steel");
            Assert.That(steel, Is.GreaterThan(0), "Matter put no steel in the silo.");
            Assert.That(storage.GetMaterialAmount(silo, "Glass"), Is.GreaterThan(0));
            Assert.That(storage.GetMaterialAmount(silo, "Plastic"), Is.GreaterThan(0));
            Assert.That(entMan.HasComponent<WFMatterStockComponent>(cleanSilo), Is.False, "Matter fed the far silo, not the nearest one.");
            Assert.That(pricing.GetPrice(silo), Is.EqualTo(baseline).Within(0.01), "the silo sells for more with the stone's matter in it.");

            // The stone's matter won't come out as sheets, but clean material in the same silo still does.
            var eject = new WFMatterEjectAttemptEvent(silo, silo, "Steel", steel);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref eject);
            Assert.That(eject.Cancelled, Is.True, "all of the silo's steel is the stone's, and it was allowed out as sheets.");
            storage.TryChangeMaterialAmount(silo, "Steel", 300);
            var clean = new WFMatterEjectAttemptEvent(silo, silo, "Steel", 100);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref clean);
            Assert.That(clean.Cancelled, Is.False, "clean steel put in beside the stone's was refused.");
        });

        // The eject flag lapses on the next update.
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            // A print from the clean silo is an ordinary item; a print from the stone's silo sells for nothing.
            var control = Print(server, cleanLathe, "CrowbarGreen", "CrowbarGreen");
            Assert.That(entMan.HasComponent<WFNoResaleComponent>(control), Is.False);
            Assert.That(pricing.GetPrice(control), Is.GreaterThan(0d), "a clean print has no price, so this test proves nothing.");

            var made = Print(server, lathe, "CrowbarGreen", "CrowbarGreen", control);
            Assert.That(entMan.HasComponent<WFNoResaleComponent>(made), Is.True, "a print from the stone's matter wasn't marked.");
            Assert.That(pricing.GetPrice(made), Is.Zero, "a print from the stone's matter still sells.");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshHealsRaisesTheDeadAndMarksThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        EntityUid ally = default, corpse = default, headless = default;
        Entity<WFMonolithComponent> scion = default;
        var before = 0f;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            SetAir(server, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 50f);
            ally = entMan.SpawnEntity("MobHuman", At(map, 3, 0));
            corpse = entMan.SpawnEntity("MobHuman", At(map, -3, 0));
            headless = entMan.SpawnEntity("MobHuman", At(map, 0, 4));
            Hurt(entMan, ally, 30f);
            before = Blunt(entMan, ally);
            Assert.That(before, Is.GreaterThan(10f), "the ally took no damage, so healing can't be seen.");
            Kill(entMan, corpse);
            foreach (var (head, _) in entMan.System<Content.Server.Body.Systems.BodySystem>()
                         .GetBodyChildrenOfType(headless, Content.Shared.Body.Part.BodyPartType.Head).ToList())
            {
                entMan.DeleteEntity(head);
            }

            entMan.System<WFMonolithSystem>().SetDial(scion, WFGift.Flesh, 1f);
        });

        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(corpse), Is.True, "the corpse didn't die, so reviving it proves nothing.");
            Assert.That(mobState.IsDead(headless), Is.True, "the headless body didn't die.");
            // Only the blunt it was hurt with: a fresh body takes a little heat and asphyxiation while it settles.
            Assert.That(Blunt(entMan, ally), Is.LessThan(before),
                $"Flesh healed nobody in reach: blunt {before} before, now {string.Join(", ", entMan.GetComponent<DamageableComponent>(ally).Damage.DamageDict.Where(kv => kv.Value > 0))}.");
        });

        // About 27 s to raise a body at this resonance.
        await WFRemadeDirectorTest.RunUntil(pair, () => !mobState.IsDead(corpse), 60);

        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(corpse), Is.False, "Flesh never raised the corpse.");
            Assert.That(entMan.HasComponent<WFFleshMarkedComponent>(corpse), Is.True, "a raised body wasn't marked.");
            Assert.That(entMan.GetComponent<WFFleshMarkedComponent>(corpse).Source, Is.EqualTo(scion.Owner));

            var text = entMan.System<ExamineSystemShared>().GetExamineText(corpse, ally).ToMarkup();
            Assert.That(text, Does.Contain("eyes"), "a marked body doesn't look wrong on examine.");
        });

        // Stable, not a body that drops straight back dead.
        await pair.RunSeconds(10f);

        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(corpse), Is.False, "the raised body died again with nothing wrong with it.");
            Assert.That(mobState.IsDead(headless), Is.True, "a headless body came back.");
            Assert.That(entMan.HasComponent<WFFleshMarkedComponent>(headless), Is.False);
            Kill(entMan, corpse);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(corpse), 10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "a marked body that died again didn't turn.");
            Assert.That(entMan.EntityQuery<WFRemadeComponent>().Any(), Is.True, "no Remade rose from the marked body.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReachWidensSensorsShortensJumpsAndEveryJumpAddsResonance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var gridUid = map.Grid.Owner;
        var timing = server.ResolveDependency<IGameTiming>();
        var shuttles = entMan.System<Content.Server.Shuttles.Systems.ShuttleSystem>();
        EntityUid radar = default;
        Entity<WFMonolithComponent> scion = default;
        var baseRange = 0f;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 50f);
            radar = entMan.SpawnEntity("ComputerRadar", At(map, 4, 0));
            baseRange = entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange;
        });

        // A jump with the dial down still adds resonance, and takes its full time.
        float TravelLeft()
        {
            return (float) (entMan.GetComponent<FTLComponent>(gridUid).StateTime.End - timing.CurTime).TotalSeconds;
        }

        var jumped = 0f;
        var plain = 0f;
        await server.WaitAssertion(() =>
        {
            Jump(entMan, timing, shuttles, gridUid, map);
            plain = TravelLeft();
            jumped = scion.Comp!.Resonance;
            entMan.RemoveComponent<FTLComponent>(gridUid);
            Assert.That(jumped, Is.GreaterThan(50f), "a jump with the dial down added no resonance.");
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange, Is.EqualTo(baseRange));
        });

        await server.WaitPost(() => entMan.System<WFMonolithSystem>().SetDial(scion, WFGift.Reach, 1f));
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange, Is.GreaterThan(baseRange), "Reach left the radar range alone.");

            var dialUp = scion.Comp!.Resonance;
            Jump(entMan, timing, shuttles, gridUid, map);
            var quick = TravelLeft();
            entMan.RemoveComponent<FTLComponent>(gridUid);
            Assert.That(quick, Is.LessThan(plain - 5f), "a jump with the dial up wasn't any shorter.");
            Assert.That(scion.Comp!.Resonance - dialUp, Is.GreaterThan(jumped - 50f), "a jump with the dial up added no more resonance.");
        });

        // Put the dial down and the radar goes back.
        await server.WaitPost(() => entMan.System<WFMonolithSystem>().SetDial(scion, WFGift.Reach, 0f));
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange, Is.EqualTo(baseRange), "the radar kept its extra range.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Raises the event a ship in FTL space raises when its jump begins, with a 60 s travel clock set.</summary>
    private static void Jump(IEntityManager entMan, IGameTiming timing,
        Content.Server.Shuttles.Systems.ShuttleSystem shuttles, EntityUid grid, TestMapData map)
    {
        var ftl = entMan.EnsureComponent<FTLComponent>(grid);
        ftl.State = FTLState.Travelling;
        ftl.TravelTime = 60f;
        ftl.StateTime = StartEndTime.FromCurTime(timing, 60f - shuttles.DefaultArrivalTime);
        var ev = new FTLStartedEvent(grid, new EntityCoordinates(map.MapUid, Vector2.Zero), null, System.Numerics.Matrix3x2.Identity, Angle.Zero);
        entMan.EventBus.RaiseLocalEvent(grid, ref ev, true);
    }

    [Test]
    public async Task ChippingAScionYieldsAShardAndAddsResonance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid user = default, knife = default;
        Entity<WFMonolithComponent> scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            SetAir(server, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 0f);
            user = entMan.SpawnEntity("MobHuman", At(map, 1, 0));
            knife = entMan.SpawnEntity("KitchenKnife", At(map, 1, 1));
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, knife), Is.True);

            // A loose Scion gives nothing: the knife slips.
            server.System<SharedInteractionSystem>().InteractUsing(user, knife, scion, entMan.GetComponent<TransformComponent>(scion).Coordinates,
                checkCanInteract: false, checkCanUse: false);
        });

        await pair.RunSeconds(10f);
        await server.WaitAssertion(() =>
        {
            Assert.That(scion.Comp!.Shards, Is.Empty, "a Scion that wasn't wrenched down gave up a shard.");
            Assert.That(entMan.System<SharedTransformSystem>().AnchorEntity(scion.Owner), Is.True);
            server.System<SharedInteractionSystem>().InteractUsing(user, knife, scion, entMan.GetComponent<TransformComponent>(scion).Coordinates,
                checkCanInteract: false, checkCanUse: false);
        });

        await pair.RunSeconds(12f);
        await server.WaitAssertion(() =>
        {
            Assert.That(scion.Comp!.Shards, Has.Count.EqualTo(1), "chipping an anchored Scion yielded no shard.");
            Assert.That(scion.Comp!.Resonance, Is.EqualTo(WFMonolithMath.ChipResonance).Within(0.2f), "a chip didn't add resonance.");
            var shard = scion.Comp!.Shards.Single();
            Assert.That(entMan.GetComponent<WFMonolithShardComponent>(shard).Parent, Is.EqualTo(scion.Owner));
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AShardsCellNeverRunsDownAndSellsOnlyWhileItsParentIsQuiet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var battery = entMan.System<BatterySystem>();
        var pricing = entMan.System<PricingSystem>();
        EntityUid shard = default;
        Entity<WFMonolithComponent> scion = default;

        await server.WaitPost(() =>
        {
            scion = SpawnScion(entMan, map.GridCoords, 0f);
            shard = entMan.SpawnEntity(WFShardSystem.ShardProto, map.GridCoords);
            entMan.GetComponent<WFMonolithShardComponent>(shard).Parent = scion;
            scion.Comp!.Shards.Add(shard);
            Assert.That(entMan.HasComponent<PowerCellComponent>(shard), Is.True, "a shard doesn't fit a power cell slot.");
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var cell = entMan.GetComponent<BatteryComponent>(shard);
            battery.SetCharge(shard, 0f, cell);
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var cell = entMan.GetComponent<BatteryComponent>(shard);
            Assert.That(cell.CurrentCharge, Is.EqualTo(cell.MaxCharge).Within(1f), "a drained shard didn't refill.");
            Assert.That(entMan.HasComponent<CargoSellBlacklistComponent>(shard), Is.False);
            Assert.That(pricing.GetPrice(shard), Is.GreaterThanOrEqualTo(400_000d), "a shard of a quiet Scion doesn't sell high.");
            scion.Comp!.Resonance = 20f;
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<CargoSellBlacklistComponent>(shard), Is.True, "a shard of a Scion that has woken still sells.");
            entMan.DeleteEntity(scion);
        });
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(shard), Is.True, "a shard outlived its Scion.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStillShardGrowsCorruptionAtGrowthAndNotBefore()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var farMap = await pair.CreateTestMap();
        EntityUid awake = default, asleep = default, stray = default, user = default;
        Entity<WFMonolithComponent> woken = default, quiet = default, strayScion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            // Far apart, or the Scion's own corruption would reach the shard.
            woken = SpawnScion(entMan, At(map, 15, -15), 40f);
            quiet = SpawnScion(entMan, At(map, -19, -19), 20f);
            awake = entMan.SpawnEntity(WFShardSystem.ShardProto, At(map, -8, 10));
            asleep = entMan.SpawnEntity(WFShardSystem.ShardProto, At(map, -15, -15));
            user = entMan.SpawnEntity("MobCarp", At(map, -8, 8));
            foreach (var (shard, parent) in new[] { (awake, woken), (asleep, quiet) })
            {
                entMan.GetComponent<WFMonolithShardComponent>(shard).Parent = parent;
                parent.Comp.Shards.Add(shard);
            }

            // A shard on another map still follows its Scion.
            strayScion = SpawnScion(entMan, At(map, 8, 8), 0f);
            stray = entMan.SpawnEntity(WFShardSystem.ShardProto, farMap.GridCoords);
            entMan.GetComponent<WFMonolithShardComponent>(stray).Parent = strayScion;
            strayScion.Comp!.Shards.Add(stray);
        });

        // Two minutes still, and a little over.
        await pair.RunSeconds(100f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFMonolithShardComponent>(awake).Deployed, Is.False, "a shard rooted before its two minutes were up.");
        });

        await pair.RunSeconds(35f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFMonolithShardComponent>(awake).Deployed, Is.True, "a still shard never rooted at Growth.");
            var node = entMan.GetComponent<WFCorruptionNodeComponent>(awake);
            Assert.That(node.Tiles, Is.Not.Empty, "a rooted shard grew no corruption.");
            Assert.That(node.Tiles.Count, Is.LessThanOrEqualTo(node.Capacity));
            Assert.That(entMan.GetComponent<WFMonolithShardComponent>(asleep).Deployed, Is.False, "a shard of a Scion below Growth rooted.");
            Assert.That(entMan.GetComponent<WFCorruptionNodeComponent>(asleep).Tiles, Is.Empty);

            // Rooted, it can't be lifted until it is cut free.
            var pickup = new GettingPickedUpAttemptEvent(user, awake);
            entMan.EventBus.RaiseLocalEvent(awake, pickup);
            Assert.That(pickup.Cancelled, Is.True, "a rooted shard could be picked up.");
        });

        // It stops at its small cap however long it is left.
        await pair.RunSeconds(60f);
        await server.WaitAssertion(() =>
        {
            var node = entMan.GetComponent<WFCorruptionNodeComponent>(awake);
            Assert.That(node.Tiles.Count, Is.LessThanOrEqualTo(node.Capacity), "a shard's corruption outgrew its cap.");

            entMan.DeleteEntity(strayScion);
        });
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(stray), Is.True, "a shard on another map outlived its Scion.");
            entMan.DeleteEntity(woken);
            entMan.DeleteEntity(quiet);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryDialUpClimbsFasterThanOne()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> one = default, all = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            one = SpawnScion(entMan, At(map, -15, 0), 0f);
            all = SpawnScion(entMan, At(map, 15, 0), 0f);
            monoliths.SetDial(one, WFGift.Power, 0.5f);
            foreach (var gift in new[] { WFGift.Power, WFGift.Matter, WFGift.Flesh, WFGift.Reach })
            {
                monoliths.SetDial(all, gift, 0.5f);
            }
        });

        await pair.RunSeconds(60f);

        await server.WaitAssertion(() =>
        {
            Assert.That(one.Comp!.Resonance, Is.GreaterThan(0f));
            Assert.That(all.Comp!.Resonance, Is.GreaterThan(one.Comp!.Resonance * 2f),
                $"four dials at half climbed {all.Comp!.Resonance} against {one.Comp!.Resonance} for one.");
            entMan.DeleteEntity(one);
            entMan.DeleteEntity(all);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARootedStoneHoldsItsGiftsAtFull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> scion = default;

        await server.WaitPost(() =>
        {
            scion = SpawnScion(entMan, map.GridCoords, 100f);
            monoliths.SetDial(scion, WFGift.Matter, 0f);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(scion.Comp!.Stage, Is.EqualTo(WFMonolithStage.Crescendo));
            Assert.That(monoliths.GetDial(scion, WFGift.Matter), Is.EqualTo(1f), "a rooted stone let a dial stay down.");
            monoliths.SetDial(scion, WFGift.Flesh, 0f);
            Assert.That(monoliths.GetDial(scion, WFGift.Flesh), Is.EqualTo(1f), "a rooted stone let a dial be turned down.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheTapWindowHasARowPerGiftAndFitsTheScreen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        Content.Client._WF.Remade.WFMonolithTapWindow window = default!;

        var states = new[]
        {
            new WFMonolithTapState(null, WFMonolithStage.Quiet, new float[5], 0f, 0f, false, false, 0),
            new WFMonolithTapState(55f, WFMonolithStage.Harvest, new[] { 1f, 0.5f, 0.25f, 1f, 0.75f }, 3_000_000f, 4.2f, false, true, 3),
            new WFMonolithTapState(100f, WFMonolithStage.Crescendo, new[] { 0.5f, 1f, 1f, 1f, 1f }, 3_000_000f, 0f, false, false, 1),
            new WFMonolithTapState(40f, WFMonolithStage.Growth, new float[5], 0f, 0f, true, false, 0),
        };

        // The state is already in when the window first lays out, as it is from a real Tap.
        foreach (var state in states)
        {
            await client.WaitPost(() =>
            {
                window = new Content.Client._WF.Remade.WFMonolithTapWindow();
                window.UpdateState(state);
                window.OpenCentered();
            });
            await pair.RunTicksSync(5);
            await client.WaitAssertion(() =>
            {
                var screen = window.Parent!.Size;
                var report = $"screen {screen}, window at {window.Position} size {window.Size} for {state.Stage}";
                Assert.That(window.Size.Y, Is.GreaterThan(200f), $"the Tap window collapsed. {report}");
                Assert.That(window.Size.X, Is.LessThanOrEqualTo(Content.Client._WF.Remade.WFMonolithTapWindow.TextWidth + 100f),
                    $"the Tap window stretched to fit unwrapped text. {report}");
                Assert.That(window.Position.X, Is.GreaterThanOrEqualTo(0f), report);
                Assert.That(window.Position.Y, Is.GreaterThanOrEqualTo(0f), report);
                Assert.That(window.Position.X + window.Size.X, Is.LessThanOrEqualTo(screen.X + 0.5f), report);
                Assert.That(window.Position.Y + window.Size.Y, Is.LessThanOrEqualTo(screen.Y + 0.5f), report);
                window.Close();
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASanctuaryBarsEveryGift()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        var battery = entMan.System<BatterySystem>();
        var storage = entMan.System<SharedMaterialStorageSystem>();
        var shuttles = entMan.System<Content.Server.Shuttles.Systems.ShuttleSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid silo = default, ally = default, radar = default, shard = default, user = default, knife = default;
        Entity<WFMonolithComponent> scion = default;
        var before = 0f;
        var baseRange = 0f;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            SetAir(server, map);
            entMan.EnsureComponent<Content.Server._WF.Remade.Corruption.WFRemadeSanctuaryComponent>(map.Grid.Owner);
            scion = SpawnScion(entMan, At(map, 0, 0), 50f);
            silo = entMan.SpawnEntity("MachineMaterialSilo", At(map, 4, 0));
            radar = entMan.SpawnEntity("ComputerRadar", At(map, 4, 4));
            ally = entMan.SpawnEntity("MobHuman", At(map, -4, 0));
            user = entMan.SpawnEntity("MobHuman", At(map, 1, 0));
            knife = entMan.SpawnEntity("KitchenKnife", At(map, 1, 1));
            shard = entMan.SpawnEntity(WFShardSystem.ShardProto, At(map, -4, 4));
            entMan.GetComponent<WFMonolithShardComponent>(shard).Parent = scion;
            scion.Comp!.Shards.Add(shard);
            entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, knife);
            entMan.System<SharedTransformSystem>().AnchorEntity(scion.Owner);
            Power(server, silo);
            Hurt(entMan, ally, 30f);
            before = TotalDamage(entMan, ally);
            baseRange = entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange;

            foreach (var gift in new[] { WFGift.Power, WFGift.Matter, WFGift.Flesh, WFGift.Reach, WFGift.Ward })
            {
                monoliths.SetDial(scion, gift, 1f);
                Assert.That(monoliths.GetDial(scion, gift), Is.Zero, $"the {gift} dial turned on a sanctuary grid.");
            }

            // Even a dial forced up in the data does nothing there.
            scion.Comp!.MatterDraw = 1f;
            scion.Comp!.FleshDraw = 1f;
            scion.Comp!.ReachDraw = 1f;
            Assert.That(monoliths.GetDial(scion, WFGift.Matter), Is.Zero);
            server.System<SharedInteractionSystem>().InteractUsing(user, knife, scion, entMan.GetComponent<TransformComponent>(scion).Coordinates,
                checkCanInteract: false, checkCanUse: false);
        });

        await pair.RunSeconds(12f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFMatterStockComponent>(silo), Is.False, "Matter filled a silo on a sanctuary grid.");
            Assert.That(storage.GetTotalMaterialAmount(silo), Is.Zero);
            Assert.That(TotalDamage(entMan, ally), Is.GreaterThanOrEqualTo(before), "Flesh healed someone on a sanctuary grid.");
            Assert.That(scion.Comp!.Shards, Has.Count.EqualTo(1), "a Scion on a sanctuary grid gave up a shard.");
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(radar).MaxRange, Is.EqualTo(baseRange), "Reach widened a radar on a sanctuary grid.");

            var resonance = scion.Comp!.Resonance;
            Jump(entMan, timing, shuttles, map.Grid.Owner, map);
            Assert.That(scion.Comp!.Resonance, Is.EqualTo(resonance), "a jump on a sanctuary grid added resonance.");
            entMan.RemoveComponent<FTLComponent>(map.Grid.Owner);

            battery.SetCharge(shard, 0f);
        });

        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BatteryComponent>(shard).CurrentCharge, Is.Zero, "a shard's cell refilled on a sanctuary grid.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }
}
