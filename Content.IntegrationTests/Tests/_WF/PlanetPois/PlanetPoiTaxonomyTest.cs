#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetMap;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.NPC.Components;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>The F7.4 site types: where each comes from, how its beacon shows, and what the new sites hold.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetPoiTaxonomyTest
{
    private const string AutomatedOutpost = "/Maps/_WF/PlanetPois/automated_outpost.yml";
    private const string ArtifactSite = "/Maps/_WF/PlanetPois/alien_artifact_site.yml";

    /// <summary>The recipe disks a workshop can leave: homestead, tier 2 and tier 3.</summary>
    private static readonly string[] DiskTiers =
    {
        "WFOutpostRecipeDiskWorkshop", "WFOutpostRecipeDiskIndustrial", "WFOutpostRecipeDiskAdvanced",
    };

    private static readonly string[] Tables =
    {
        "WFPoisAsclepiu", "WFPoisMerak", "WFPoisFervidus", "WFPoisThrascias", "WFPoisAerumna", "WFPoisCarcinoma",
    };

    private static readonly string[] Types =
    {
        "WFPoiTypeAutomatedOutpost", "WFPoiTypeTradepost", "WFPoiTypeDerelictCapital", "WFPoiTypeFarm", "WFPoiTypeWorkshop",
        "WFPoiTypeRuins", "WFPoiTypeHeavenlyShrine", "WFPoiTypeAlienArtifactSite",
    };

    [Test]
    public async Task EveryTypeIsRolledSomewhereWithItsBeacon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var tables = Tables.Select(id => server.ProtoMan.Index<WFPlanetPoiTablePrototype>(id)).ToList();
            var rolled = tables.SelectMany(table => table.Entries.Select(entry => entry.Type)
                    .Append(table.Wrecks?.Type).Append(table.Capitals?.Type))
                .Where(type => type != null).Select(type => type!.Value.Id).ToHashSet();
            foreach (var type in Types)
                Assert.That(rolled, Does.Contain(type), $"No world rolls a {type} site.");

            var beacons = Types.ToDictionary(id => id, id => server.ProtoMan.Index<WFPoiTypePrototype>(id).Beacon);
            Assert.That(beacons["WFPoiTypeTradepost"], Is.EqualTo(WFPoiBeacon.Known));
            Assert.That(beacons["WFPoiTypeHeavenlyShrine"], Is.EqualTo(WFPoiBeacon.Faint));
            Assert.That(beacons["WFPoiTypeAlienArtifactSite"], Is.EqualTo(WFPoiBeacon.Faint));
            Assert.That(beacons.Where(kv => kv.Value == WFPoiBeacon.Unknown).Select(kv => kv.Key),
                Is.EquivalentTo(new[] { "WFPoiTypeAutomatedOutpost", "WFPoiTypeDerelictCapital", "WFPoiTypeFarm", "WFPoiTypeWorkshop", "WFPoiTypeRuins" }));

            foreach (var table in tables.Where(table => table.Capitals != null))
            {
                Assert.That(table.Capitals!.Sizes, Is.EquivalentTo(new[] { Content.Shared._NF.Shipyard.Prototypes.VesselSize.Large }), table.ID);
                Assert.That(table.Capitals.Name?.Id, Is.EqualTo("wf-poi-derelict-capital"), table.ID);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAutomatedOutpostStandsDownWhenItsCoreIsHacked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(AutomatedOutpost), out var loaded), Is.True);
                var grid = loaded!.Value;
                var children = Children(entMan, grid.Owner);
                var cores = children.Where(entMan.HasComponent<WFPoiCoreComponent>).ToList();
                var turrets = children.Where(entMan.HasComponent<WFPoiCoreGuardComponent>).ToList();
                Assert.That(cores, Has.Count.EqualTo(1));
                Assert.That(turrets, Has.Count.EqualTo(4));
                Assert.That(turrets.All(entMan.HasComponent<NpcFactionMemberComponent>), Is.True);

                var core = cores[0];
                var coreTile = maps.TileIndicesFor(grid.Owner, grid.Comp, entMan.GetComponent<TransformComponent>(core).Coordinates);
                var roof = entMan.GetComponent<RoofComponent>(grid.Owner);
                Assert.That(server.System<SharedRoofSystem>().IsRooved((grid.Owner, grid.Comp, roof), coreTile), Is.True, "The core stands in the open.");

                var stood = server.System<WFPoiCoreSystem>().StandDown((core, entMan.GetComponent<WFPoiCoreComponent>(core)));
                Assert.That(stood, Is.EqualTo(4));
                Assert.That(entMan.GetComponent<WFPoiCoreComponent>(core).Hacked, Is.True);
                Assert.That(turrets.Any(entMan.HasComponent<NpcFactionMemberComponent>), Is.False, "A turret still answers to its faction.");
            }
            finally
            {
                entMan.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnArtifactSiteHoldsAnArtifactInItsPit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(ArtifactSite), out var loaded), Is.True);
                var children = Children(entMan, loaded!.Value.Owner);
                Assert.That(children.Count(entMan.HasComponent<XenoArtifactComponent>), Is.EqualTo(1));
                Assert.That(children.Count(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CMBarricadeSandbag"),
                    Is.GreaterThanOrEqualTo(20));
            }
            finally
            {
                entMan.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FaintSignalsHideUntilSurveyedAndCluesPointToSites()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pois = server.System<WFPlanetPoiSystem>();
            var survey = server.System<WFPoiSurveySystem>();
            var planetMap = server.System<WFPlanetMapSystem>();
            var table = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisMerak");
            var map = testMap.MapUid;
            var user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));

            EntityUid Signal(int x, string type) => pois.SpawnSignal(map, map, new Vector2i(x, 0),
                new WFPlanetPoiEntry { Name = "wf-poi-alien-artifact-site" }, table, type);

            var known = Signal(20, "WFPoiTypeTradepost");
            var near = Signal(30, "WFPoiTypeAlienArtifactSite");
            var far = Signal(500, "WFPoiTypeHeavenlyShrine");
            var plain = Signal(-40, "WFPoiTypeFarm");
            WFPoiSignalComponent Comp(EntityUid uid) => entMan.GetComponent<WFPoiSignalComponent>(uid);

            Assert.That(Comp(known).Revealed, Is.True, "A tradepost doesn't broadcast.");
            Assert.That(Comp(near).Faint && Comp(far).Faint, Is.True);
            Assert.That(Comp(near).RevealRange, Is.EqualTo(12f));
            Assert.That(Comp(plain).Faint || Comp(plain).Revealed, Is.False);

            var record = new WFPlanetPoiComponent();
            foreach (var signal in new[] { known, near, far, plain })
                record.Sites.Add(new WFPlanetPoiSite { Name = "wf-poi-alien-artifact-site", Signal = signal });
            Assert.That(planetMap.Sites(record), Has.Count.EqualTo(2), "A faint site shows on the map.");
            Assert.That(planetMap.Sites(record, all: true), Has.Count.EqualTo(4));

            var found = survey.Survey(user, 200f, out var nearest);
            Assert.That(found, Is.EqualTo(1));
            Assert.That(Comp(near).Faint, Is.False);
            Assert.That(Comp(far).Faint, Is.True, "The sweep reached past its range.");
            Assert.That(nearest, Is.Not.Null);
            Assert.That(nearest!.Value.Length(), Is.EqualTo(30.5f).Within(0.1f));
            Assert.That(planetMap.Sites(record), Has.Count.EqualTo(3));

            for (var i = 0; i < 3; i++)
                Assert.That(survey.TryFollowClue(user, out _, out _), Is.True);
            Assert.That(new[] { known, near, far, plain }.All(uid => Comp(uid).Revealed), Is.True);
            Assert.That(Comp(far).Faint, Is.False, "A clue's site stays off the radars.");
            Assert.That(survey.TryFollowClue(user, out _, out _), Is.False, "A clue pointed to a site already found.");

            entMan.DeleteEntity(user);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TypeSpawnsLandOnFreeSiteFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = testMap.Grid;
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);

            var wall = entMan.SpawnEntity("WallSolid", maps.GridTileToLocal(grid.Owner, grid.Comp, new Vector2i(2, 2)));
            var site = new WFPlanetPoiSite { Bounds = new Box2i(0, 0, 5, 5) };
            var pois = server.System<WFPlanetPoiSystem>();

            var ruins = server.ProtoMan.Index<WFPoiTypePrototype>("WFPoiTypeRuins");
            var clues = pois.SpawnTypeExtras(grid, site, ruins);
            Assert.That(clues, Has.Count.EqualTo(2));
            foreach (var clue in clues)
            {
                Assert.That(entMan.HasComponent<WFPoiClueComponent>(clue), Is.True);
                var tile = maps.TileIndicesFor(grid.Owner, grid.Comp, entMan.GetComponent<TransformComponent>(clue).Coordinates);
                Assert.That(tile, Is.Not.EqualTo(new Vector2i(2, 2)), "A clue landed on the wall.");
            }

            // The workshop's spawner leaves one recipe disk of the three tiers and goes.
            pois.SpawnTypeExtras(grid, site, server.ProtoMan.Index<WFPoiTypePrototype>("WFPoiTypeWorkshop"));
            var workshop = Children(entMan, grid.Owner)
                .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID is { } id && DiskTiers.Contains(id))
                .ToList();
            Assert.That(workshop, Has.Count.EqualTo(1));

            entMan.DeleteEntity(wall);
            foreach (var uid in clues.Concat(workshop))
                entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> Children(IEntityManager entMan, EntityUid grid)
    {
        var result = new List<EntityUid>();
        var enumerator = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
            result.Add(child);
        return result;
    }
}
