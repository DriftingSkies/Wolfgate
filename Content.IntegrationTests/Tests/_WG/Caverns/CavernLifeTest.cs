#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Caverns;
using Content.Server._WG.LivingWorld;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WG.Caverns;
using Content.Shared._WG.LivingWorld;
using Content.Shared._WG.Planets;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WG.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WG.Caverns;

/// <summary>
/// The Rime Galleries', Umbral Deeps' and Gut's cave life: every creature and plant spawns, each stands only on its own
/// hall's floor, the hazards are rare, the plants act on a mob and the harvests yield.
/// </summary>
[TestFixture]
public sealed class CavernLifeTest
{
    /// <summary>Edge of the square of cavern noise read around each gate, taken every other tile.</summary>
    private const int SampleSize = 512;

    /// <summary>The most any hazard may fill of a world's open ground.</summary>
    private const float MaxHazardShare = 0.003f;

    private static readonly string[] Mobs =
    {
        "WGMobCavernRimeBat", "WGMobCavernFrostWeaver", "WGMobCavernRimeSlime",
        "WGMobCavernDeepBat", "WGMobCavernUmbralSpider", "WGMobCavernGeodeCrab",
        "WGMobCavernBileSlug", "WGMobCavernAntibody", "WGMobCavernBloodLeech",
    };

    /// <summary>The air-breathers of worlds with no oxygen to speak of.</summary>
    private static readonly string[] NoBreath =
    {
        "WGMobCavernRimeBat", "WGMobCavernFrostWeaver", "WGMobCavernRimeSlime",
        "WGMobCavernDeepBat", "WGMobCavernUmbralSpider", "WGMobCavernGeodeCrab",
    };

    private static readonly string[] Plants =
    {
        "WGCavernRimeCapPlant", "WGCavernHearthLichenPlant", "WGCavernHangingIcicle", "WGCavernFrostBulb", "WGCavernPlasmaBloom",
        "WGCavernUmbralCapPlant", "WGCavernPaleLichenPlant", "WGCavernGeodeCluster", "WGCavernGeodeBloom", "WGCavernRadiantCrystal", "WGCavernAcidSpitter",
        "WGCavernGastricMossPlant", "WGCavernMarrowCapPlant", "WGCavernNerveWhip", "WGCavernAcidPolyp",
    };

    /// <summary>Each plant that is picked, and what picking it gives.</summary>
    private static readonly (string Plant, string Produce)[] Harvests =
    {
        ("WGCavernRimeCapPlant", "WGRimeCap"),
        ("WGCavernHearthLichenPlant", "WGHearthLichen"),
        ("WGCavernUmbralCapPlant", "WGUmbralCap"),
        ("WGCavernPaleLichenPlant", "WGPaleLichen"),
        ("WGCavernGeodeCluster", "ShardCrystalPink"),
        ("WGCavernGastricMossPlant", "WGGastricMoss"),
        ("WGCavernMarrowCapPlant", "WGMarrowCap"),
    };

    private const string Snowdug = "FloorSnowDug";
    private const string Drift = "WGCavernFloorSnowdrift";
    private static readonly string[] Falls = { "WGCavernFloorRimeFall", "WGCavernFloorRimeBlue", "WGCavernFloorRimePack" };
    private static readonly string[] Bone = { "WGCavernFloorBoneRim", "WGCavernFloorBoneCartilage", "WGCavernFloorBoneSpine" };

    /// <summary>What each world's life may stand on, and whether it is a hazard that must stay rare.</summary>
    private static readonly Dictionary<string, Placement[]> Placements = new()
    {
        {
            "WGSurfaceThrascias", new[]
            {
                new Placement("WGCavernFaunaThrasciasHollows", Drift),
                new Placement("WGCavernFaunaThrasciasFalls", Falls),
                new Placement("WGCavernFaunaThrasciasLake", "WGCavernFloorRimeDark"),
                new Placement("WGCavernFaunaThrascias", Snowdug),
                new Placement("WGCavernRimeCapPlant", Falls.Append(Snowdug).Append(Drift).ToArray()),
                new Placement("WGCavernHearthLichenPlant", Snowdug, Drift),
                new Placement("WGCavernHangingIcicle", Falls.Append(Snowdug).ToArray(), true),
                new Placement("WGCavernFrostBulb", Falls, true),
                new Placement("WGCavernPlasmaBloom", new[] { "WGCavernFloorRimeDark" }, true),
            }
        },
        {
            "WGSurfaceAerumna", new[]
            {
                new Placement("WGCavernFaunaAerumnaGrove", "WGCavernFloorUmbralShroud"),
                new Placement("WGCavernFaunaAerumnaGeode", "WGCavernFloorUmbralDusk"),
                new Placement("WGCavernFaunaAerumnaNest", "WGCavernFloorNest"),
                new Placement("WGCavernFaunaAerumna", "WGCavernFloorChromite", "WGCavernFloorBedrock"),
                new Placement("WGXenoEggWild", "WGCavernFloorNest"),
                new Placement("WGXenoWeedsWild", "WGCavernFloorNest"),
                new Placement("WGCavernUmbralCapPlant", "WGCavernFloorChromite", "WGCavernFloorBedrock", "WGCavernFloorUmbralShroud"),
                new Placement("WGCavernPaleLichenPlant", "WGCavernFloorChromite", "WGCavernFloorBedrock", "WGCavernFloorUmbralDusk"),
                new Placement("WGCavernGeodeCluster", "WGCavernFloorUmbralDusk"),
                new Placement("WGCavernGeodeBloom", new[] { "WGCavernFloorUmbralDusk" }, true),
                new Placement("WGCavernRadiantCrystal", new[] { "WGCavernFloorUmbralDusk" }, true),
                new Placement("WGCavernAcidSpitter", new[] { "WGCavernFloorChromite", "WGCavernFloorUmbralShroud" }, true),
            }
        },
        {
            "WGSurfaceCarcinoma", new[]
            {
                new Placement("WGCavernFaunaCarcinomaStomach", "WGCavernFloorGut"),
                new Placement("WGCavernFaunaCarcinomaNerve", "WGCavernFloorGut", "WGCavernFloorNerve"),
                new Placement("WGCavernFaunaCarcinomaOssuary", Bone),
                new Placement("WGCavernFaunaCarcinomaBlood", "WGCavernFloorClot"),
                new Placement("WGCavernFaunaCarcinoma", "WGCavernFloorFlesh"),
                new Placement("WGCavernGastricMossPlant", "WGCavernFloorGut"),
                new Placement("WGCavernMarrowCapPlant", Bone),
                new Placement("WGCarcinomaBonePile", Bone),
                new Placement("WGCavernNerveWhip", new[] { "WGCavernFloorGut", "WGCavernFloorNerve" }, true),
                new Placement("WGCavernAcidPolyp", new[] { "WGCavernFloorGut" }, true),
            }
        },
    };

    /// <summary>Every creature, plant and hall marker spawns, and the air-breathers of airless worlds don't need air.</summary>
    [Test]
    public async Task EverythingSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var spawned = new List<EntityUid>();
        var kept = new List<EntityUid>();
        var markers = Placements.Values.SelectMany(list => list).Select(placement => placement.Entity).Where(id => id.StartsWith("WGCavernFauna"));

        await server.WaitPost(() =>
        {
            foreach (var id in Mobs.Concat(Plants).Concat(markers))
            {
                Assert.That(server.ProtoMan.HasIndex<EntityPrototype>(id), Is.True, $"{id} is not a prototype.");
                var uid = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                spawned.Add(uid);
                if (!id.StartsWith("WGCavernFauna"))
                    kept.Add(uid);
            }
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in kept.Where(uid => entMan.Deleted(uid)))
                    Assert.Fail($"{uid} is gone.");

                foreach (var id in NoBreath)
                {
                    var proto = server.ProtoMan.Index<EntityPrototype>(id);
                    Assert.That(proto.HasComponent<BreathingImmunityComponent>(server.ResolveDependency<IComponentFactory>()), Is.True, $"{id} needs air.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// In 512² of each world's cavern noise around the gate every placement turns up, and only on its own hall's floor;
    /// hazards fill a sliver of the open ground.
    /// </summary>
    [Test]
    public async Task LifeStandsOnItsOwnHall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WGCavernSampler>();
        await EnableCaverns(pair);

        foreach (var (surfaceId, placements) in Placements)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var spec = CavernOf(pair, surfaceId).Mouths;
                await server.WaitPost(() =>
                {
                    var biome = server.EntMan.GetComponent<BiomeComponent>(world.Cavern);
                    var planet = server.EntMan.GetComponent<WGCavernGroundComponent>(world.Ground).Centre;
                    var centre = new Vector2i((int) MathF.Floor(planet.X), (int) MathF.Floor(planet.Y)) + spec.GateOffset;
                    var sample = sampler.Sample(biome, centre, SampleSize, 2);
                    var open = Enumerable.Range(0, sample.Count).Count(sample.IsOpen);

                    using (Assert.EnterMultipleScope())
                    {
                        foreach (var placement in placements)
                        {
                            var found = Enumerable.Range(0, sample.Count).Where(i => sample.Entities[i] == placement.Entity).ToList();
                            TestContext.Out.WriteLine($"{surfaceId}: {found.Count} samples of {placement.Entity}.");
                            Assert.That(found, Is.Not.Empty, $"{surfaceId}: no {placement.Entity} in {SampleSize}² around the gate.");

                            // A wild slime grotto (_WG/WildSlimes) dresses its own floor with the world's decor.
                            foreach (var i in found.Where(i => !CavernBiomeTest.WildSlimeGrottoTiles.Contains(sample.Tiles[i] ?? string.Empty)))
                            {
                                Assert.That(placement.Tiles, Does.Contain(sample.Tiles[i]),
                                    $"{surfaceId}: {placement.Entity} at {sample.IndexOf(i)} stands on {sample.Tiles[i]}.");
                            }

                            if (placement.Hazard)
                            {
                                Assert.That((float) found.Count / open, Is.LessThan(MaxHazardShare),
                                    $"{surfaceId}: {placement.Entity} fills {(float) found.Count / open:P2} of the open ground.");
                            }
                        }
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Picking each harvestable plant gives its produce, leaves the plant standing, and the plant grows it back.</summary>
    [Test]
    public async Task HarvestsYield()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var harvest = server.System<WGRegrowingHarvestSystem>();
        var plants = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            foreach (var (plant, _) in Harvests)
                plants.Add(entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));
        });

        int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                for (var i = 0; i < Harvests.Length; i++)
                {
                    var (plant, produce) = Harvests[i];
                    var before = Count(produce);
                    harvest.Harvest((plants[i], entMan.GetComponent<WGRegrowingHarvestComponent>(plants[i])), null);

                    Assert.That(Count(produce), Is.GreaterThan(before), $"Picking {plant} gave no {produce}.");
                    Assert.That(entMan.Deleted(plants[i]), Is.False, $"Picking {plant} took the plant.");
                    Assert.That(entMan.GetComponent<WGRegrowingHarvestComponent>(plants[i]).Harvested, Is.True, $"{plant} isn't bare after picking.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hazard acts on a mob beside it, and the Gut's plants spare its own flesh.</summary>
    [TestCase("WGCavernHangingIcicle", "Piercing")]
    [TestCase("WGCavernGeodeBloom", "Piercing")]
    [TestCase("WGCavernRadiantCrystal", "Radiation")]
    [TestCase("WGCavernAcidSpitter", "Caustic")]
    [TestCase("WGCavernAcidPolyp", "Caustic")]
    [TestCase("WGCavernNerveWhip", "Shock")]
    public async Task HazardHitsAMob(string plant, string damageType)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, mobs) = await Rig(pair, plant, 1f, 3f, -1.2f);
        var (near, sibling, flesh) = (mobs[0], mobs[1], mobs[2]);

        await server.WaitPost(() => server.System<NpcFactionSystem>().AddFaction(flesh, "Chimera"));
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entMan, near, damageType) + Damage(entMan, sibling, damageType), Is.GreaterThan(FixedPoint2.Zero),
                $"{plant} did no {damageType} to anyone in reach.");

            if (plant is "WGCavernNerveWhip" or "WGCavernAcidPolyp")
                Assert.That(Damage(entMan, flesh, damageType), Is.EqualTo(FixedPoint2.Zero), $"{plant} hurt the flesh.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The mist and plasma bulbs release a cloud on a mob beside them.</summary>
    [TestCase("WGCavernFrostBulb")]
    [TestCase("WGCavernPlasmaBloom")]
    public async Task BulbReleasesACloud(string plant)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await Rig(pair, plant, 0.8f);
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() => Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.True, $"{plant} released no cloud."));
        await pair.CleanReturnAsync();
    }

    /// <summary>Each of these caverns names a soundscape of its own, and every sound in it exists.</summary>
    [Test]
    public async Task CavernsPlayTheirOwnAmbience()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var surface in Placements.Keys)
                {
                    var cavern = server.ProtoMan.EnumeratePrototypes<WGCavernPrototype>().Single(c => c.Surface == surface);
                    Assert.That(cavern.Ambience, Is.Not.Null, $"{cavern.ID} plays only the surface's ambience.");

                    if (cavern.Ambience is not { } id)
                        continue;

                    var profile = server.ProtoMan.Index(id);
                    var sounds = profile.Loops.Concat(profile.OneShots).OfType<SoundPathSpecifier>().ToList();
                    Assert.That(profile.Loops, Is.Not.Empty, $"{id} has no bed.");
                    Assert.That(profile.OneShots, Is.Not.Empty, $"{id} has no accents.");

                    foreach (var sound in sounds)
                        Assert.That(resources.ContentFileExists(sound.Path), Is.True, $"{id} names the missing {sound.Path}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A plating floor with the plant at the centre and a human at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];

        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            plantUid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < distances.Length; i++)
                mobs[i] = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distances[i], 0.5f));
        });

        return (plantUid, mobs);
    }

    private static FixedPoint2 Damage(IEntityManager entMan, EntityUid uid, string type)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    /// <summary>A kind of life, the floors it may stand on, and whether it is a hazard that must stay rare.</summary>
    private sealed record Placement(string Entity, string[] Tiles, bool Hazard = false)
    {
        public Placement(string entity, params string[] tiles) : this(entity, tiles, false)
        {
        }
    }
}
