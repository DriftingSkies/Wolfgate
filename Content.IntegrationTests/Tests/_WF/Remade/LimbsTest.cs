#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Limbs;
using Content.Server.NPC.HTN;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>Limbs: hits are shared between them, cutting one off costs the mob dearly, and what it gave goes with it.</summary>
[TestFixture]
public sealed class LimbsTest
{
    private const string Slasher = "WFRemadeSlasher";
    private const string Infector = "WFRemadeInfector";

    private const int DeckRadius = 22;

    /// <summary>Lays a square of plating round the test grid's origin.</summary>
    internal static void LayDeck(TestPair pair, TestMapData map)
    {
        var maps = pair.Server.EntMan.System<SharedMapSystem>();
        var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = -DeckRadius; x <= DeckRadius; x++)
        for (var y = -DeckRadius; y <= DeckRadius; y++)
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), plating);
    }

    internal static EntityCoordinates At(TestMapData map, float x, float y)
    {
        return new EntityCoordinates(map.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f));
    }

    /// <summary>A player's body that lives in the test map's vacuum, where a human drops into crit and NPCs sleep without a living player.</summary>
    internal static EntityUid SpawnCrewman(TestPair pair, EntityCoordinates coordinates)
    {
        var crewman = pair.Server.EntMan.SpawnEntity("MobCarp", coordinates);
        pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, crewman);
        return crewman;
    }

    internal static async Task RunUntil(TestPair pair, Func<bool> done, int seconds)
    {
        var ticks = seconds * 60 / 15;
        for (var i = 0; i < ticks; i++)
        {
            await pair.RunTicksSync(15);
            // The pathfinder answers in real time, so a test can't spin faster than it thinks.
            await Task.Delay(25);
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;
        }
    }

    /// <summary>Hits a mob for this much of a damage type, aimed at one body part. Returns whether it was applied.</summary>
    internal static void Hit(TestPair pair, EntityUid target, string type, float amount, TargetBodyPart? part, EntityUid? origin = null)
    {
        var entMan = pair.Server.EntMan;
        var damage = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
        entMan.System<DamageableSystem>().TryChangeDamage(target, damage, origin: origin, targetPart: part);
    }

    internal static float Total(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float();
    }

    [Test]
    public async Task SeveringALimbHidesItsLayerAndDeadMobsLieInPieces()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        EntityUid slasher = default;

        await server.WaitPost(() =>
        {
            LimbsTest.LayDeck(pair, map);
            LimbsTest.SpawnCrewman(pair, LimbsTest.At(map, 0, 0));
            slasher = entMan.SpawnEntity(Slasher, LimbsTest.At(map, 3, 0));
        });
        await pair.RunTicksSync(10);

        var clientSlasher = pair.ToClientUid(slasher);
        var sprite = pair.Client.System<SpriteSystem>();

        (bool Visible, string State) Layer(string key)
        {
            var ent = new Entity<SpriteComponent?>(clientSlasher, pair.Client.EntMan.GetComponent<SpriteComponent>(clientSlasher));
            Assert.That(sprite.LayerMapTryGet(ent, key, out var index, false), Is.True, $"no layer '{key}'.");
            Assert.That(sprite.TryGetLayer(ent, index, out var layer, false), Is.True);
            return (layer!.Visible, sprite.LayerGetRsiState(ent, index).Name ?? string.Empty);
        }

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Layer("l_arm"), Is.EqualTo((true, "slasher_l_arm")));
            Assert.That(Layer("dead").Visible, Is.False);
        });

        await server.WaitPost(() => Assert.That(limbs.TrySever(slasher, "l_arm"), Is.True));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(slasher).Severed, Does.Contain("l_arm"));
            Assert.That(limbs.TrySever(slasher, "l_arm"), Is.False, "a limb came off twice.");
            Assert.That(limbs.TrySever(slasher, "chest"), Is.False, "the torso came off.");
            Assert.That(limbs.Stat(slasher, WFRemadeLimbStat.Melee), Is.EqualTo(0.5f).Within(0.001f), "one blade gone should halve its melee damage.");
            Assert.That(Total(pair, slasher), Is.GreaterThanOrEqualTo(12f), "losing a limb should cost the mob a lump of damage.");
            Assert.That(entMan.EntityQuery<WFSeveredLimbComponent>().Any(), Is.True, "no limb was left on the floor.");
        });
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Layer("l_arm").Visible, Is.False, "the severed blade is still drawn.");
            Assert.That(Layer("r_arm").Visible, Is.True);
        });

        await server.WaitPost(() => entMan.System<MobStateSystem>().ChangeMobState(slasher, MobState.Dead));
        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Layer("chest"), Is.EqualTo((true, "torso_lying")), "a dead slasher should draw its lying torso.");
            Assert.That(Layer("r_arm"), Is.EqualTo((true, "r_arm_lying")));
            Assert.That(Layer("l_arm").Visible, Is.False, "a severed limb came back with the corpse.");
            Assert.That(Layer("dead").Visible, Is.False, "the dead composite is drawn over the lying limbs.");
        });

        await server.WaitPost(() => entMan.DeleteEntity(slasher));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlashSeversWhileTorsoDamageAloneIsMuchSlower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        EntityUid cut = default;
        EntityUid shot = default;
        var cutHits = 0;
        var shotHits = 0;

        await server.WaitPost(() =>
        {
            cut = entMan.SpawnEntity(Slasher, map.GridCoords);
            shot = entMan.SpawnEntity(Slasher, map.GridCoords);
            var parts = new[] { TargetBodyPart.Head, TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg, TargetBodyPart.LeftArm, TargetBodyPart.RightArm };
            foreach (var part in parts)
            {
                if (mobState.IsDead(cut))
                    break;

                Hit(pair, cut, "Slash", 20, part);
                cutHits++;
            }

            Assert.That(mobState.IsDead(cut), Is.True, "cutting limbs off should be a quick kill, total health still decides death.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(cut).Severed, Is.Not.Empty);

            while (!mobState.IsDead(shot) && shotHits < 200)
            {
                Hit(pair, shot, "Slash", 20, TargetBodyPart.Torso);
                shotHits++;
            }

            Assert.That(mobState.IsDead(shot), Is.True);
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(shot).Severed, Is.Empty, "torso hits severed a limb.");
            Assert.That(shotHits, Is.GreaterThanOrEqualTo(cutHits * 4), $"the torso died in {shotHits} hits, cutting limbs in {cutHits}.");
            entMan.DeleteEntity(cut);
            entMan.DeleteEntity(shot);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BluntSeversLessEasilyThanSlash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var slashed = entMan.SpawnEntity(Slasher, map.GridCoords);
            var bashed = entMan.SpawnEntity(Slasher, map.GridCoords);
            Hit(pair, slashed, "Slash", 20, TargetBodyPart.LeftLeg);
            Hit(pair, bashed, "Blunt", 20, TargetBodyPart.LeftLeg);
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(slashed).Severed, Does.Contain("l_leg"), "a slash didn't sever a leg.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(bashed).Severed, Is.Empty, "one blow severed a leg.");
            entMan.DeleteEntity(slashed);
            entMan.DeleteEntity(bashed);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALeglessSlasherIsSlow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        EntityUid slasher = default;
        var whole = 0f;

        await server.WaitPost(() =>
        {
            slasher = entMan.SpawnEntity(Slasher, map.GridCoords);
            whole = entMan.GetComponent<MovementSpeedModifierComponent>(slasher).CurrentSprintSpeed;
            limbs.TrySever(slasher, "l_leg");
        });
        await pair.RunTicksSync(3);

        var hobbling = 0f;
        await server.WaitPost(() =>
        {
            hobbling = entMan.GetComponent<MovementSpeedModifierComponent>(slasher).CurrentSprintSpeed;
            limbs.TrySever(slasher, "r_leg");
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var crawling = entMan.GetComponent<MovementSpeedModifierComponent>(slasher).CurrentSprintSpeed;
            Assert.That(hobbling, Is.LessThanOrEqualTo(whole * 0.55f), "one leg gone should cut its speed hard.");
            Assert.That(crawling, Is.LessThanOrEqualTo(whole * 0.15f), "a legless slasher should crawl.");
            Assert.That(crawling, Is.GreaterThan(0f), "a legless slasher can't move at all.");
            entMan.DeleteEntity(slasher);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHeadlessSlasherLivesButCannotShoutAndSeesLess()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limbs = entMan.System<WFRemadeLimbsSystem>();

        await server.WaitPost(() =>
        {
            var slasher = entMan.SpawnEntity(Slasher, map.GridCoords);
            var htn = entMan.GetComponent<HTNComponent>(slasher);
            var before = htn.Blackboard.GetValueOrDefault<float>("AggroVisionRadius", entMan);
            Assert.That(limbs.Can(slasher, WFRemadeLimbStat.Voice), Is.True);
            Assert.That(limbs.TrySever(slasher, "head"), Is.True);

            Assert.That(entMan.System<MobStateSystem>().IsAlive(slasher), Is.True, "losing its head killed it.");
            Assert.That(limbs.Can(slasher, WFRemadeLimbStat.Voice), Is.False, "it can still shout.");
            Assert.That(htn.Blackboard.GetValueOrDefault<float>("AggroVisionRadius", entMan), Is.LessThan(before * 0.5f), "it sees as far as before.");
            entMan.DeleteEntity(slasher);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnInfectorWithoutAProboscisCannotTurnACorpse()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        var limbs = entMan.System<WFRemadeLimbsSystem>();

        await server.WaitPost(() =>
        {
            var whole = entMan.SpawnEntity(Infector, map.GridCoords);
            var maimed = entMan.SpawnEntity(Infector, map.GridCoords);
            var corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);

            Assert.That(limbs.TrySever(maimed, "proboscis"), Is.True);
            Assert.That(entMan.System<MobStateSystem>().IsAlive(maimed), Is.True, "losing the proboscis killed it.");
            Assert.That(conversion.TryStartConversion(corpse, null, maimed), Is.False, "an Infector with no proboscis turned a corpse.");
            Assert.That(entMan.HasComponent<WFRemadeConvertingComponent>(corpse), Is.False);
            Assert.That(conversion.TryStartConversion(corpse, null, whole), Is.True, "a whole Infector couldn't turn a corpse.");
            entMan.DeleteEntity(whole);
            entMan.DeleteEntity(maimed);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnInfectorWithoutWingsIsSlowAndKeepsItsSpeedFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limbs = entMan.System<WFRemadeLimbsSystem>();

        await server.WaitPost(() =>
        {
            var infector = entMan.SpawnEntity(Infector, map.GridCoords);
            Assert.That(limbs.Stat(infector, WFRemadeLimbStat.Speed), Is.EqualTo(1f));
            limbs.TrySever(infector, "l_wing");
            limbs.TrySever(infector, "r_wing");
            Assert.That(limbs.Stat(infector, WFRemadeLimbStat.Speed), Is.EqualTo(0.5f).Within(0.001f), "losing both wings should halve its speed.");
            entMan.DeleteEntity(infector);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APlayersAimPicksTheLimbHit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var crewman = LimbsTest.SpawnCrewman(pair, map.GridCoords);
            var targeting = entMan.EnsureComponent<TargetingComponent>(crewman);
            targeting.Target = TargetBodyPart.RightLeg;
            var slasher = entMan.SpawnEntity(Slasher, map.GridCoords.Offset(new Vector2(2f, 0f)));
            entMan.GetComponent<WFRemadeLimbsComponent>(slasher).AimedChance = 1f;

            Hit(pair, slasher, "Slash", 20, null, crewman);
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(slasher).Severed, Is.EquivalentTo(new[] { "r_leg" }), "the hit missed the limb the player aimed at.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entMan.DeleteEntity(slasher);
        });
        await pair.CleanReturnAsync();
    }
}
