using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.Cloning;
using Content.Server._WG.EmoteVoices;
using Content.Server.Cloning.Components;
using Content.Server.Materials;
using Content.Server.Medical.Components;
using Content.Server.Power.Components;
using Content.Shared._Mono.Cloning;
using Content.Shared._WG.Genitals.Components;
using Content.Shared._WG.LegStyle;
using Content.Shared._WG.Voice;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Physics;
using Content.Shared.Speech;
using Content.Shared.Zombies;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WG.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WG.Cloning;

/// <summary>A genetic record has to rebuild the whole character, even after the body is gone.</summary>
[TestFixture]
[TestOf(typeof(GeneticCloningSystem))]
public sealed class GeneticCloningTest
{
    /// <summary>The one cloner file saved as a whole map; the rest are grids.</summary>
    private const string StationMap = "/Maps/_Mono/Outpost/caelestinus_central.yml";

    /// <summary>Every map with a pod, a scanner and a cloning console. All of it has to work without a mapper's help.</summary>
    private static readonly string[] ClonerMaps =
    {
        "/Maps/Shuttles/dart.yml",
        "/Maps/Shuttles/emergency_raven.yml",
        StationMap,
        "/Maps/_Mono/ShuttleEvent/asakim_medium.yml",
        "/Maps/_Mono/ShuttleEvent/asakim_small.yml",
        "/Maps/centcomm.yml",
        "/SharedMaps/_WG/OldVessels/BlackMarket/ganymede.yml",
        "/SharedMaps/_WG/OldVessels/dervi.yml",
        "/SharedMaps/_WG/OldVessels/tzipora.yml",
        "/SharedMaps/_WG/AdminVessels/lupercal.yml",
    };

    [Test]
    public async Task CloneKeepsCharacterAfterBodyIsDeleted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cloning = entMan.System<GeneticCloningSystem>();
            var mindSys = entMan.System<SharedMindSystem>();
            var emoteVoices = entMan.System<EmoteVoiceSystem>();
            var protoMan = server.ProtoMan;

            var source = SpawnWithAnatomy(entMan, map.MapCoords);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(source);
            humanoid.MismatchedParts = true;
            humanoid.CustomSpeciesName = "Tester";
            humanoid.LegStyle = protoMan.EnumeratePrototypes<LegStylePrototype>().First().ID;

            var voice = entMan.EnsureComponent<SpeechVoiceComponent>(source);
            voice.Voice.Enabled = true;
            voice.Voice.Pitch = 1.25f;

            var emoteVoice = protoMan.EnumeratePrototypes<Content.Shared._WG.EmoteVoices.EmoteVoicePrototype>().First().ID;
            emoteVoices.SetVoices(source, new[] { new ProtoId<Content.Shared._WG.EmoteVoices.EmoteVoicePrototype>(emoteVoice) });

            var speechSounds = protoMan.EnumeratePrototypes<SpeechSoundsPrototype>().First().ID;
            entMan.GetComponent<SpeechComponent>(source).SpeechSounds = speechSounds;

            var profile = entMan.GetComponent<GenitalsComponent>(source).SourceProfile;

            var session = server.PlayerMan.Sessions.First();
            var mind = mindSys.CreateMind(session.UserId, "Tester");
            mindSys.TransferTo(mind, source, mind: mind.Comp);
            mindSys.SetUserId(mind, session.UserId, mind.Comp);

            var record = cloning.CreateRecord(source, out var failReason);
            Assert.That(record, Is.Not.Null, failReason);

            var pod = entMan.Spawn("CloningPod", map.MapCoords);
            var podComp = entMan.GetComponent<CloningPodComponent>(pod);
            entMan.System<MaterialStorageSystem>().TryChangeMaterialAmount(pod, podComp.RequiredMaterial, 100000);

            entMan.DeleteEntity(source);

            Assert.That(cloning.TryCloning(pod, record!, podComp), Is.True);
            var clone = podComp.BodyContainer.ContainedEntity;
            Assert.That(clone, Is.Not.Null);

            var cloneHumanoid = entMan.GetComponent<HumanoidAppearanceComponent>(clone!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(cloneHumanoid.MismatchedParts, Is.True);
                Assert.That(cloneHumanoid.CustomSpeciesName, Is.EqualTo("Tester"));
                Assert.That(cloneHumanoid.LegStyle, Is.EqualTo(record!.Appearance.LegStyle));
                Assert.That(entMan.GetComponent<SpeechVoiceComponent>(clone.Value).Voice.Pitch, Is.EqualTo(1.25f));
                Assert.That(emoteVoices.GetVoices(clone.Value), Does.Contain(new ProtoId<Content.Shared._WG.EmoteVoices.EmoteVoicePrototype>(emoteVoice)));
                Assert.That(entMan.GetComponent<SpeechComponent>(clone.Value).SpeechSounds, Is.EqualTo(new ProtoId<SpeechSoundsPrototype>(speechSounds)));
                Assert.That(entMan.GetComponent<GenitalsComponent>(clone.Value).SourceProfile, Is.EqualTo(profile));
            });

            entMan.DeleteEntity(clone.Value);
            entMan.DeleteEntity(pod);
            entMan.DeleteEntity(mind);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScanSetsFailChanceAndRefusesZombies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cloning = entMan.System<GeneticCloningSystem>();
            var mindSys = entMan.System<SharedMindSystem>();
            var session = server.PlayerMan.Sessions.First();

            var body = entMan.Spawn("MobHuman", map.MapCoords);
            var mind = mindSys.CreateMind(session.UserId, "Tester");
            mindSys.TransferTo(mind, body, mind: mind.Comp);
            mindSys.SetUserId(mind, session.UserId, mind.Comp);

            var cellular = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Cellular"), 50);
            entMan.System<DamageableSystem>().TryChangeDamage(body, cellular, ignoreResistances: true);

            var record = cloning.CreateRecord(body, out _, 0.5f);
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.FailChance, Is.EqualTo(0.25f).Within(0.001f));

            entMan.AddComponent<ZombieComponent>(body);
            Assert.That(cloning.CreateRecord(body, out var reason), Is.Null);
            Assert.That(reason, Is.Not.Null);

            entMan.DeleteEntity(body);
            entMan.DeleteEntity(mind);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A database that was unanchored stops being the selection, and one on another grid can't be selected.</summary>
    [Test]
    public async Task DatabaseSelectionStaysOnGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var database = entMan.System<GeneticDatabaseSystem>();
            var console = entMan.SpawnEntity("computerBodyScanner", map.GridCoords);
            var client = entMan.GetComponent<GeneticDatabaseClientComponent>(console);

            var first = entMan.SpawnEntity("GeneticDatabaseServer", map.GridCoords);
            database.TryGetDatabase(console, out _);
            Assert.That(client.Database, Is.EqualTo(first));

            var second = entMan.SpawnEntity("GeneticDatabaseServer", map.GridCoords);
            entMan.System<SharedTransformSystem>().Unanchor(first);
            database.TryGetDatabase(console, out _);
            Assert.That(client.Database, Is.EqualTo(second), "An unanchored selection should fall back to the installed database.");

            var otherGrid = entMan.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            entMan.System<SharedMapSystem>().SetTile(otherGrid, otherGrid.Comp, Robust.Shared.Maths.Vector2i.Zero, map.Tile.Tile);
            entMan.System<SharedTransformSystem>().SetWorldPosition(otherGrid, new System.Numerics.Vector2(100, 100));
            var offGrid = entMan.SpawnEntity("GeneticDatabaseServer", new EntityCoordinates(otherGrid, 0.5f, 0.5f));
            var offGridId = entMan.GetComponent<GeneticDatabaseComponent>(offGrid).Id;
            entMan.EventBus.RaiseLocalEvent(console, new GeneticDatabaseSelectMessage(offGridId));
            Assert.That(entMan.GetComponent<TransformComponent>(offGrid).GridUid, Is.EqualTo(otherGrid.Owner));
            Assert.That(client.Database, Is.EqualTo(second), "A database on another grid should be refused.");

            // Two databases never share a record id, so a console still showing the old list can't hit the wrong person.
            var firstEnt = new Entity<GeneticDatabaseComponent>(first, entMan.GetComponent<GeneticDatabaseComponent>(first));
            var secondEnt = new Entity<GeneticDatabaseComponent>(second, entMan.GetComponent<GeneticDatabaseComponent>(second));
            var firstRecord = new GeneticRecord { Mind = first };
            var secondRecord = new GeneticRecord { Mind = second };
            database.Upsert(firstEnt, firstRecord);
            database.Upsert(secondEnt, secondRecord);
            Assert.That(secondRecord.Id, Is.Not.EqualTo(firstRecord.Id));
            Assert.That(database.TryGetRecord(secondEnt, firstRecord.Id, out _), Is.False);

            entMan.DeleteEntity(console);
            entMan.DeleteEntity(first);
            entMan.DeleteEntity(second);
            entMan.DeleteEntity(offGrid);
            entMan.DeleteEntity(otherGrid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each cloner map loads with its console linked to a pod, a scanner console linked to a scanner, and power in reach.</summary>
    [Test]
    public async Task ClonerMapsAreWired([ValueSource(nameof(ClonerMaps))] string path)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var testMap = await pair.CreateTestMap();

        HashSet<Entity<MapComponent>> maps = new();
        HashSet<Entity<MapGridComponent>> grids = new();
        await server.WaitPost(() =>
        {
            var loader = entMan.System<MapLoaderSystem>();
            if (path == StationMap)
            {
                var options = MapLoadOptions.Default with
                {
                    DeserializationOptions = DeserializationOptions.Default with { InitializeMaps = true },
                };
                Assert.That(loader.TryLoadGeneric(new ResPath(path), out maps, out grids, options));
                return;
            }

            Assert.That(loader.TryLoadGrid(testMap.MapId, new ResPath(path), out var grid));
            grids.Add(grid!.Value);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var query = entMan.AllEntityQueryEnumerator<CloningConsoleComponent, TransformComponent>();
            var clonerGrid = default(EntityUid?);
            while (query.MoveNext(out var consoleUid, out var console, out var xform))
            {
                if (xform.GridUid == null || grids!.All(g => g.Owner != xform.GridUid))
                    continue;

                clonerGrid = xform.GridUid;
                Assert.That(console.CloningPod, Is.Not.Null, $"{path}: {entMan.ToPrettyString(consoleUid)} has no pod");
                Assert.That(console.CloningPodInRange, Is.True);
            }
            Assert.That(clonerGrid, Is.Not.Null, $"{path}: no cloning console");

            var scanners = entMan.AllEntityQueryEnumerator<BodyScannerConsoleComponent, TransformComponent>();
            var foundScanner = false;
            while (scanners.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.GridUid != clonerGrid)
                    continue;

                foundScanner = true;
                Assert.That(comp.Scanner, Is.Not.Null, $"{path}: scanner console has no scanner");
                Assert.That(entMan.HasComponent<MedicalScannerComponent>(comp.Scanner), Is.True);
                AssertInPowerReach(entMan, uid, path);
                AssertTileClear(entMan, uid, path);
            }
            Assert.That(foundScanner, Is.True, $"{path}: no scanner console");

            var databases = entMan.AllEntityQueryEnumerator<GeneticDatabaseComponent, TransformComponent>();
            var foundDatabase = false;
            while (databases.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != clonerGrid)
                    continue;

                foundDatabase = true;
                Assert.That(xform.Anchored, Is.True, $"{path}: database not anchored");
                AssertInPowerReach(entMan, uid, path);
                AssertTileClear(entMan, uid, path);
            }
            Assert.That(foundDatabase, Is.True, $"{path}: no genetic database");
        });

        await server.WaitPost(() =>
        {
            foreach (var grid in grids!)
            {
                entMan.DeleteEntity(grid);
            }
            foreach (var mapEnt in maps!)
            {
                entMan.DeleteEntity(mapEnt);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertTileClear(IEntityManager entMan, EntityUid uid, string path)
    {
        var xform = entMan.GetComponent<TransformComponent>(uid);
        var grid = xform.GridUid!.Value;
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var maps = entMan.System<SharedMapSystem>();
        var tile = maps.TileIndicesFor(grid, gridComp, xform.Coordinates);

        foreach (var other in maps.GetAnchoredEntities(grid, gridComp, tile))
        {
            if (other == uid || !entMan.TryGetComponent<FixturesComponent>(other, out var fixtures))
                continue;

            var blocks = fixtures.Fixtures.Values.Any(f => f.Hard && (f.CollisionLayer & (int) CollisionGroup.Impassable) != 0);
            Assert.That(blocks, Is.False,
                $"{path}: {entMan.ToPrettyString(uid)} shares tile {tile} with {entMan.ToPrettyString(other)}");
        }
    }

    private static void AssertInPowerReach(IEntityManager entMan, EntityUid uid, string path)
    {
        Assert.That(entMan.GetComponent<ExtensionCableReceiverComponent>(uid).Provider, Is.Not.Null,
            $"{path}: {entMan.ToPrettyString(uid)} is out of reach of APC power");
    }
}
