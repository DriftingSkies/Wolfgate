#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.BountyContracts;
using Content.Server._NF.SectorServices;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Warheads;
using Content.Server.Body.Systems;
using Content.Server.Destructible;
using Content.Shared._NF.Bank.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>What the crew has against an outbreak: scorched ground, necrotoxin, the cutter, and the end of a Monolith.</summary>
[TestFixture]
public sealed class CounterplayTest
{
    private const string Scion = "WFMonolithScion";
    private const string Slasher = "WFRemadeSlasher";

    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    private static void Hit(IEntityManager entMan, IPrototypeManager protos, EntityUid target, string type, float amount, EntityUid? origin = null)
    {
        var damage = new DamageSpecifier(protos.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
        entMan.System<DamageableSystem>().TryChangeDamage(target, damage, origin: origin);
    }

    private static float Total(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float();
    }

    [Test]
    public async Task BurnedCorruptionScorchesItsTileAndScorchBlocksRegrowth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        var scorch = entMan.System<WFScorchSystem>();
        var grid = map.Grid.Owner;
        EntityUid burned = default;
        EntityUid cut = default;
        var blocked = new Vector2i(1, 0);

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            burned = entMan.SpawnEntity("WFCorruption", At(map, 10, 10));
            cut = entMan.SpawnEntity("WFCorruption", At(map, 12, 10));
            Hit(entMan, server.ProtoMan, burned, "Heat", 200f);
            Hit(entMan, server.ProtoMan, cut, "Slash", 200f);

            // Block a tile beside the Scion before anything has grown.
            scorch.Scorch(grid, blocked);
            var scion = entMan.SpawnEntity(Scion, At(map, 0, 0));
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });

        // Growth spreads on a 10 % roll a second, so the first few tiles can take a while: give it up to two minutes.
        await pair.RunSeconds(40f);
        for (var i = 0; i < 16 && entMan.EntityQuery<WFCorruptionComponent>().Count() <= 4; i++)
            await pair.RunSeconds(5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(burned), Is.True, "the burned corruption was never destroyed.");
            Assert.That(entMan.Deleted(cut), Is.True, "the cut corruption was never destroyed.");
            Assert.That(scorch.IsScorched(grid, new Vector2i(10, 10)), Is.True, "burned corruption didn't scorch its tile.");
            Assert.That(scorch.IsScorched(grid, new Vector2i(12, 10)), Is.False, "corruption that was only cut scorched its tile.");

            var grown = entMan.EntityQuery<WFCorruptionComponent>().Count();
            Assert.That(grown, Is.GreaterThan(4), "no corruption spread, so a block on it proves nothing.");
            Assert.That(corruption.IsCorrupted(map.Grid, blocked), Is.False, "corruption grew onto a scorched tile.");
        });

        await server.WaitPost(() => scorch.ScorchFor = TimeSpan.Zero);
        await server.WaitPost(() => scorch.Scorch(grid, new Vector2i(-1, 0)));
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(scorch.IsScorched(grid, new Vector2i(-1, 0)), Is.False, "a scorch with no time left still blocks.");
            scorch.ScorchFor = TimeSpan.FromMinutes(10);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NecrotoxinPoisonsTheRemadeAndAKeepsADosedCorpseDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var bloodstream = entMan.System<BloodstreamSystem>();
        var conversion = entMan.System<WFRemadeConversionSystem>();
        var necrotoxin = entMan.System<WFNecrotoxinSystem>();
        EntityUid slasher = default;
        EntityUid control = default;
        EntityUid dosed = default;
        EntityUid clean = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            slasher = entMan.SpawnEntity(Slasher, At(map, 0, 0));
            control = entMan.SpawnEntity(Slasher, At(map, 6, 0));
            Assert.That(bloodstream.TryAddToChemicals(slasher, new Solution(WFNecrotoxinSystem.Reagent, FixedPoint2.New(15))), Is.True,
                "a slasher has no blood to poison.");

            dosed = entMan.SpawnEntity("MobHuman", At(map, 0, 6));
            clean = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
            foreach (var corpse in new[] { dosed, clean })
            {
                entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            }

            Assert.That(bloodstream.TryAddToChemicals(dosed, new Solution(WFNecrotoxinSystem.Reagent, FixedPoint2.New(5))), Is.True);
        });

        await pair.RunSeconds(10f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Total(entMan, slasher), Is.GreaterThan(20f), "necrotoxin did hardly anything to a slasher.");
            Assert.That(Total(entMan, control), Is.Zero, "a slasher with none in it was hurt.");
            Assert.That(necrotoxin.Dose(slasher), Is.LessThan(15f), "the Remade's blood never cleared any.");
            Assert.That(entMan.GetComponent<DamageableComponent>(slasher).Damage.DamageDict["Poison"].Float(), Is.GreaterThan(20f),
                "the damage wasn't poison.");

            Assert.That(entMan.HasComponent<WFNecrotoxinProtectedComponent>(dosed), Is.True, "a dosed corpse wasn't marked.");
            Assert.That(entMan.HasComponent<WFNecrotoxinProtectedComponent>(clean), Is.False, "a clean corpse was marked.");
            Assert.That(conversion.CanConvert(dosed), Is.False, "a dosed corpse can still turn.");
            Assert.That(conversion.CanConvert(clean), Is.True, "a clean corpse can't turn.");
            Assert.That(conversion.TryStartConversion(dosed), Is.False, "a dosed corpse started turning.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCutterTakesLimbsOffAndItsGunFires()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var gunSystem = entMan.System<SharedGunSystem>();
        EntityUid arc = default;
        EntityUid slasher = default;
        EntityUid shot = default;
        EntityUid cutter = default;
        EntityUid crewman = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
            arc = entMan.SpawnEntity("WFRemadeCutterArc", map.GridCoords);
            slasher = entMan.SpawnEntity(Slasher, At(map, 10, 10));
            shot = entMan.SpawnEntity(Slasher, At(map, 4, 0));
            cutter = entMan.SpawnEntity("WFRemadeCutter", At(map, 0, 0));
        });
        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<HitscanBasicDamageComponent>(arc).Damage;
            var limbs = entMan.GetComponent<WFRemadeLimbsComponent>(slasher);
            var damageable = entMan.System<DamageableSystem>();

            for (var i = 0; i < 3 && !limbs.Severed.Contains("l_arm"); i++)
            {
                damageable.TryChangeDamage(slasher, new DamageSpecifier(damage), targetPart: TargetBodyPart.LeftArm);
            }

            Assert.That(limbs.Severed, Does.Contain("l_arm"), "three cutter hits on an arm didn't take it off.");
            Assert.That(limbs.Severed, Does.Not.Contain("chest"));
            Assert.That(Total(entMan, slasher), Is.LessThan(100f), "taking an arm killed it outright.");

            var gun = entMan.GetComponent<GunComponent>(cutter);
            var battery = entMan.GetComponent<BatteryComponent>(cutter);
            var before = battery.CurrentCharge;
            gunSystem.AttemptShoot(crewman, cutter, gun, At(map, 4, 0));
            Assert.That(battery.CurrentCharge, Is.LessThan(before), "the cutter used no charge.");
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Total(entMan, shot), Is.GreaterThan(0f), "the cutter's arc hurt nothing in front of it.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyingAScionKillsItsShardsStopsItsDirectorAndLeavesItsRemadeFeral()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var director = entMan.System<WFRemadeDirectorSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        EntityUid shard = default;
        EntityUid slasher = default;
        EntityUid tile = default;
        var hurt = 0f;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = entMan.SpawnEntity(Scion, At(map, 3, 0));
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 60f;
            shard = entMan.SpawnEntity(WFShardSystem.ShardProto, At(map, 8, 8));
            entMan.GetComponent<WFMonolithShardComponent>(shard).Parent = scion;
            entMan.GetComponent<WFMonolithComponent>(scion).Shards.Add(shard);
            slasher = entMan.SpawnEntity(Slasher, At(map, -8, 8));
            director.Enlist(slasher, scion);
        });
        await pair.RunSeconds(4f);

        await server.WaitPost(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            Assert.That(monolith.Corruption, Is.Not.Empty, "no corruption had grown to be cleaned up.");
            tile = monolith.Corruption.First();
            Assert.That(entMan.HasComponent<WFFeralComponent>(slasher), Is.False);
            entMan.System<DestructibleSystem>().DestroyEntity(scion);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(scion), Is.True);
            Assert.That(entMan.Deleted(shard), Is.True, "the shard outlived its Scion.");
            // Decay comes at a random time within a minute, so the tile may already be gone.
            Assert.That(entMan.Deleted(tile) || entMan.GetComponent<WFCorruptionComponent>(tile).DecayAt != null, Is.True,
                "its corruption didn't start to die.");
            Assert.That(entMan.EntityQuery<WFRemadeDirectorComponent>().Any(), Is.False, "a Director outlived its Monolith.");

            var husks = entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFMonolithHusk");
            Assert.That(husks, Is.EqualTo(1), "the stone left no husk.");

            Assert.That(entMan.GetComponent<WFRemadeComponent>(slasher).Source, Is.Null, "the Remade still answers to a dead stone.");
            var feral = entMan.GetComponent<WFFeralComponent>(slasher);
            Assert.That(entMan.System<MobStateSystem>().IsDead(slasher), Is.False);
            hurt = Total(entMan, slasher);
            Assert.That(hurt, Is.GreaterThan(25f), "a feral Remade wasn't hurt.");
            Assert.That(feral.WithersAt, Is.GreaterThan(timing.CurTime), "it wastes away straight off.");
        });

        // The old starvation is gone: nothing wears it down until its time is up.
        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Total(entMan, slasher), Is.EqualTo(hurt).Within(0.5f), "a feral Remade took damage before its time.");
            entMan.GetComponent<WFFeralComponent>(slasher).WithersAt = timing.CurTime;
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Total(entMan, slasher), Is.GreaterThan(hurt + 2f), "a feral Remade never wasted away.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APrimePostsABountyWearsArmourAndPaysWhoeverBringsItDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var board = entMan.System<BountyContractSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        EntityUid crewman = default;

        await server.WaitPost(() =>
        {
            entMan.AddComponent<StationSectorServiceHostComponent>(entMan.Spawn());
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
            entMan.AddComponent<BankAccountComponent>(crewman);
            scion = entMan.SpawnEntity(Scion, At(map, 3, 0));
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 100f;
        });
        await pair.RunSeconds(3f);

        uint contractId = 0;
        var paid = 0;
        var before = 0;
        await server.WaitAssertion(() =>
        {
            var prime = entMan.GetComponent<WFPrimeComponent>(scion);
            Assert.That(entMan.GetComponent<WFMonolithComponent>(scion).Stage, Is.EqualTo(WFMonolithStage.Crescendo));
            Assert.That(entMan.GetComponent<DamageableComponent>(scion).DamageModifierSetId, Is.EqualTo(prime.Armour.Id), "the Prime wears no armour.");
            Assert.That(prime.Reward, Is.EqualTo(prime.BaseReward));
            Assert.That(prime.Contract, Is.Not.Null, "no bounty was posted.");
            contractId = prime.Contract!.Value;
            Assert.That(board.TryGetContract(contractId, out var contract), Is.True, "the bounty isn't on the board.");
            Assert.That(contract!.Reward, Is.EqualTo(prime.Reward));
            Assert.That(contract.Name, Is.Not.Empty);

            // Nothing short of a nuke hurts it: not blades, bullets or blasts.
            Hit(entMan, server.ProtoMan, scion, "Slash", 8f, crewman);
            Hit(entMan, server.ProtoMan, scion, "Piercing", 15f, crewman);
            Hit(entMan, server.ProtoMan, scion, "Structural", 100000f, crewman);
            Hit(entMan, server.ProtoMan, scion, "Heat", 100000f, crewman);
            Assert.That(Total(entMan, scion), Is.Zero, "something short of a nuke hurt the Prime.");

            // The bounty grows while it stands, and the board follows.
            prime.RootedAt = timing.CurTime - TimeSpan.FromMinutes(10);
            prime.LastHit = TimeSpan.Zero;
            prime.MendAfter = TimeSpan.FromSeconds(1);
            prime.NextVerse = timing.CurTime;
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).Biomass = 0f;
        });

        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var prime = entMan.GetComponent<WFPrimeComponent>(scion);
            Assert.That(prime.Reward, Is.EqualTo(prime.BaseReward + 10 * prime.RewardPerMinute).Within(prime.RewardPerMinute), "the bounty didn't grow.");
            Assert.That(board.TryGetContract(contractId, out var contract), Is.True);
            Assert.That(contract!.Reward, Is.EqualTo(prime.Reward), "the board didn't follow.");
            Assert.That(prime.Verses, Is.GreaterThanOrEqualTo(1), "the Prime never sang again.");
            Assert.That(entMan.GetComponent<WFRemadeDirectorComponent>(scion).Biomass, Is.GreaterThanOrEqualTo(prime.VerseBiomass - 1f),
                "a verse gave the Director nothing.");

            // A nuclear blast close by ends it, and whoever set it off is paid.
            paid = prime.Reward;
            before = entMan.System<BankSystem>().TryGetBalance(crewman, out var start) ? start : 0;
            var far = new WFNuclearDetonatedEvent(new MapCoordinates(new Vector2(5000f, 0f), map.MapId), 60f, crewman);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref far);
            Assert.That(entMan.Deleted(scion), Is.False, "a nuke far away killed the Prime.");
            var blast = new WFNuclearDetonatedEvent(entMan.System<SharedTransformSystem>().GetMapCoordinates(scion), 60f, crewman);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref blast);
            Assert.That(board.TryGetContract(contractId, out _), Is.False, "the bounty stayed up after the Prime fell.");
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<BankSystem>().TryGetBalance(crewman, out var balance) ? balance - before : 0, Is.EqualTo(paid),
                "the player who brought the Prime down wasn't paid its bounty.");
            Assert.That(entMan.Deleted(scion), Is.True);
            var husks = entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFMonolithPrimeHusk");
            Assert.That(husks, Is.EqualTo(1), "the Prime left no husk.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public void ABountySplitsByDamageAndLosesNothing()
    {
        var a = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
        var b = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
        var shares = WFPrimeSystem.Split(1_000_001, new Dictionary<Robust.Shared.Network.NetUserId, float> { [a] = 300f, [b] = 100f });

        Assert.That(shares.Values.Sum(), Is.EqualTo(1_000_001), "the split lost or made up spesos.");
        Assert.That(shares[a], Is.GreaterThan(shares[b] * 2), "the player who dealt three times as much didn't get about three times as much.");
        Assert.That(WFPrimeSystem.Split(1000, new Dictionary<Robust.Shared.Network.NetUserId, float>()), Is.Empty);
    }
}
