#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Limbs;
using Content.Server.Damage.Systems;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.CCVar;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The Remade variants, and the stuck breaker that sends a Remade through what is in its way.</summary>
[TestFixture]
public sealed class VariantsTest
{
    private static readonly (string Variant, string Base)[] Variants =
    {
        ("WFRemadeSlasherDesiccated", "WFRemadeSlasher"),
        ("WFRemadeSlasherCarrion", "WFRemadeSlasher"),
        ("WFRemadeBruteFlesh", "WFRemadeBrute"),
        ("WFRemadePukerFlayed", "WFRemadePuker"),
        ("WFRemadeLurkerMalo", "WFRemadeLurker"),
        ("WFRemadeHopperFennec", "WFRemadeHopper"),
    };

    [Test]
    public async Task EveryVariantIsBoughtLikeItsBaseAndSharesItsWeights()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var protos = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var (variant, parent) in Variants)
            {
                var mob = protos.EnumeratePrototypes<WFRemadeMobPrototype>().SingleOrDefault(mob => mob.Entity == variant);
                Assert.That(mob, Is.Not.Null, $"{variant} has no wfRemadeMob, so nothing ever sends it.");
                Assert.That(mob!.SpawnWeight, Is.GreaterThan(0f), $"{variant} is never sent.");
                Assert.That(mob.Cost, Is.GreaterThan(0f));

                var baseMob = protos.EnumeratePrototypes<WFRemadeMobPrototype>().Single(other => other.Entity == parent);
                Assert.That(mob.MinStage, Is.GreaterThanOrEqualTo(baseMob.MinStage), $"{variant} comes in before its base.");
                Assert.That(protos.Index<EntityPrototype>(variant).Parents, Does.Contain(parent), $"{variant} isn't parented to {parent}.");
            }

            // DS13 picks a slasher's look at 8 : 2 : 0.1, a brute's at 1 : 1, a puker's at 2 : 1 and a lurker's at 1 : 0.1.
            float Share(string baseId, params string[] others)
            {
                var all = new[] { baseId }.Concat(others).Select(id => protos.EnumeratePrototypes<WFRemadeMobPrototype>().Single(mob => mob.Entity == id).SpawnWeight).ToList();
                return all[0] / all.Sum();
            }

            Assert.That(Share("WFRemadeSlasher", "WFRemadeSlasherDesiccated", "WFRemadeSlasherCarrion"), Is.EqualTo(8f / 10.1f).Within(0.01f));
            Assert.That(Share("WFRemadeBrute", "WFRemadeBruteFlesh"), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(Share("WFRemadePuker", "WFRemadePukerFlayed"), Is.EqualTo(2f / 3f).Within(0.01f));
            Assert.That(Share("WFRemadeLurker", "WFRemadeLurkerMalo"), Is.EqualTo(1f / 1.1f).Within(0.01f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryVariantSpawnsWithLimbsDrawsFromItsSheetAndDiesCleanly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limbSystem = entMan.System<WFRemadeLimbsSystem>();
        var mobs = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            var x = -9;
            foreach (var (variant, _) in Variants)
            {
                mobs[variant] = entMan.SpawnEntity(variant, WFRemadeDirectorTest.At(map, x, 4));
                x += 3;
            }
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                var limbs = entMan.GetComponent<WFRemadeLimbsComponent>(uid);
                Assert.That(limbs.Limbs.Count, Is.GreaterThanOrEqualTo(4), $"{id} has hardly any limbs.");
                Assert.That(limbs.Limbs.Any(limb => !limb.Severable), Is.True, $"{id} has no torso.");
                Assert.That(entMan.HasComponent<Content.Server._WF.Remade.Abilities.WFRemadeAbilitiesComponent>(uid), Is.True, $"{id} has no abilities.");
                Assert.That(entMan.HasComponent<WFRemadeVoiceComponent>(uid), Is.True, $"{id} has no voice.");
                Assert.That(entMan.System<MobStateSystem>().IsAlive(uid), Is.True, $"{id} didn't spawn alive.");
            }
        });

        var sprites = pair.Client.System<SpriteSystem>();
        var cache = pair.Client.ResolveDependency<IResourceCache>();
        await pair.Client.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                var client = pair.ToClientUid(uid);
                var limbs = pair.Client.EntMan.GetComponent<WFRemadeLimbsComponent>(client);
                var ent = new Entity<SpriteComponent?>(client, pair.Client.EntMan.GetComponent<SpriteComponent>(client));
                Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / limbs.Rsi, out var rsi), Is.True, $"{id}: no sprite sheet {limbs.Rsi}.");

                foreach (var limb in limbs.Limbs)
                {
                    foreach (var key in limb.LayerKeys)
                    {
                        Assert.That(sprites.LayerMapTryGet(ent, key, out var index, false), Is.True, $"{id}: no layer '{key}' for the {limb.Id}.");
                        var state = sprites.LayerGetRsiState(ent, index).Name ?? string.Empty;
                        Assert.That(rsi!.RSI.TryGetState(state, out _), Is.True, $"{id}: layer '{key}' uses state '{state}', which the sheet doesn't have.");
                    }

                    if (limb.Lying != null)
                        Assert.That(rsi!.RSI.TryGetState(limb.Lying, out _), Is.True, $"{id}: the lying state '{limb.Lying}' of the {limb.Id} doesn't exist.");
                }

                Assert.That(sprites.LayerMapTryGet(ent, limbs.DeadLayer, out var dead, false), Is.True, $"{id}: no dead layer.");
                var deadState = sprites.LayerGetRsiState(ent, dead).Name ?? string.Empty;
                Assert.That(rsi!.RSI.TryGetState(deadState, out _), Is.True, $"{id}: dead layer uses state '{deadState}', which the sheet doesn't have.");
            }
        });

        // One limb off, then dead: something must still be drawn, and nothing may throw.
        await server.WaitPost(() =>
        {
            foreach (var (_, uid) in mobs)
            {
                var severable = entMan.GetComponent<WFRemadeLimbsComponent>(uid).Limbs.FirstOrDefault(limb => limb.Severable);
                if (severable != null)
                    limbSystem.TrySever(uid, severable.Id);

                entMan.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
            }
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
                Assert.That(entMan.System<MobStateSystem>().IsDead(uid), Is.True, $"{id} didn't die.");
        });

        await pair.Client.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                var client = pair.ToClientUid(uid);
                var limbs = pair.Client.EntMan.GetComponent<WFRemadeLimbsComponent>(client);
                var ent = new Entity<SpriteComponent?>(client, pair.Client.EntMan.GetComponent<SpriteComponent>(client));
                var keys = limbs.Limbs.SelectMany(limb => limb.LayerKeys).Append(limbs.DeadLayer).Distinct();
                var visible = keys.Count(key => sprites.LayerMapTryGet(ent, key, out var index, false) && sprites.TryGetLayer(ent, index, out var layer, false) && layer!.Visible);
                Assert.That(visible, Is.GreaterThan(0), $"{id} is drawn as nothing once dead.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var uid in mobs.Values)
                entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeadCrewmanCanRiseAsAVariantAtItsShare()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid corpse = default;

        await server.WaitPost(() =>
        {
            corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
        });

        await server.WaitAssertion(() =>
        {
            var weights = conversion.Weights(corpse).ToDictionary(pair => pair.Key.Entity.Id, pair => pair.Value);
            Assert.That(weights, Does.ContainKey("WFRemadeSlasherDesiccated"), "a corpse can't rise as a desiccated slasher.");
            Assert.That(weights, Does.ContainKey("WFRemadeSlasherCarrion"), "a corpse can't rise as a carrion slasher.");
            Assert.That(weights["WFRemadeSlasher"] / weights["WFRemadeSlasherDesiccated"], Is.EqualTo(4f).Within(0.2f), "DS13 turns slashers into desiccated ones one time in five.");
            Assert.That(weights.Values.Sum() - weights.Where(pair => pair.Key.StartsWith("WFRemadeSlasher")).Sum(pair => pair.Value), Is.GreaterThanOrEqualTo(0f));
            entMan.DeleteEntity(corpse);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A line of railings across the deck at y = 5, with the crewman south of it and a Remade north of it.</summary>
    private static async Task<List<EntityUid>> RailingLine(TestPair pair, TestMapData map, int from, int to, bool asWall)
    {
        var rails = new List<EntityUid>();
        await pair.Server.WaitPost(() =>
        {
            var tags = pair.Server.EntMan.System<TagSystem>();
            for (var x = from; x <= to; x++)
            {
                var rail = pair.Server.EntMan.SpawnEntity("Railing", WFRemadeDirectorTest.At(map, x, 5));
                if (asWall)
                    tags.AddTag(rail, "Wall");

                rails.Add(rail);
            }
        });

        return rails;
    }

    [Test]
    public async Task ARemadeBehindARailingBreaksItAndReachesTheCrewman()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid remade = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
        });

        // From wall to wall, so there is no way round and the pathfinder, which can't see a railing, sends it straight at one.
        var rails = await RailingLine(pair, map, -22, 22, false);
        await server.WaitPost(() => remade = entMan.SpawnEntity("WFRemadeSlasher", WFRemadeDirectorTest.At(map, 0, 9)));

        await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 1f, 90);

        await server.WaitAssertion(() =>
        {
            Assert.That(rails.Count(rail => entMan.Deleted(rail)), Is.GreaterThan(0), $"it never broke a railing: {WFRemadeDirectorTest.Describe(pair, remade)}");
            Assert.That(LimbsTest.Total(pair, crewman), Is.GreaterThan(1f), $"it broke through but never reached the crewman: {WFRemadeDirectorTest.Describe(pair, remade)}");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyABruteGoesThroughAWallThatIsInItsWay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid slasher = default;
        EntityUid brute = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            var crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            // The slasher comes round through the gap; a dead crewman puts the brute to sleep before it reaches the wall.
            entMan.System<GodmodeSystem>().EnableGodmode(crewman);
        });

        // Railings the pathfinder can't see, tagged as walls: what a wall the navmesh missed would be.
        var rails = await RailingLine(pair, map, -22, 22, true);
        var left = rails.Take(21).ToList();
        var right = rails.Skip(24).ToList();
        await server.WaitPost(() =>
        {
            slasher = entMan.SpawnEntity("WFRemadeSlasher", WFRemadeDirectorTest.At(map, -10, 9));
            brute = entMan.SpawnEntity("WFRemadeBruteFlesh", WFRemadeDirectorTest.At(map, 10, 9));
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => right.Any(rail => entMan.Deleted(rail)), 90);
        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(right.Any(rail => entMan.Deleted(rail)), Is.True, $"the brute never went through the wall: {WFRemadeDirectorTest.Describe(pair, brute)}");
            Assert.That(left.Any(rail => entMan.Deleted(rail)), Is.False, "a slasher broke a wall.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }
}
