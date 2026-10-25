#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingMode.Issues;
using Content.Server.Singularity.Components;
using Content.Shared._WF.MappingMode.Issues;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The issues checker on a session: a bare grid fails every ship rule and each clears as its part is placed; the map
/// rules find doubled, loose, unsaved and denied entities; a second grid turns the ship check into a grid count.
/// </summary>
[TestFixture]
public sealed class WFMappingIssuesTest
{
    private const string Mob = "WFMappingIssuesTestMob";
    private const string Spawner = "WFMappingIssuesTestSpawner";

    // A prototype the map file drops, and the spawner that places it; live mobs all carry a denied ghost role.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingIssuesTestMob
  save: false

- type: entity
  id: WFMappingIssuesTestSpawner
  components:
  - type: ConditionalSpawner
    prototypes:
    - WFMappingIssuesTestMob
";

    private static readonly string[] ShipErrors =
    {
        WFMappingIssueCatalog.ShipNoConsole,
        WFMappingIssueCatalog.ShipNoDock,
        WFMappingIssueCatalog.ShipNoThruster,
        WFMappingIssueCatalog.ShipNoGyroscope,
        WFMappingIssueCatalog.ShipNoPowerSource,
        WFMappingIssueCatalog.ShipNoApc,
        WFMappingIssueCatalog.ShipNoBattery,
    };

    /// <summary>The part that clears each ship rule, placed in order along the grid's middle row.</summary>
    private static readonly (string Rule, string Prototype)[] Parts =
    {
        (WFMappingIssueCatalog.ShipNoConsole, "ComputerShuttle"),
        (WFMappingIssueCatalog.ShipNoDock, "AirlockShuttle"),
        (WFMappingIssueCatalog.ShipNoThruster, "Thruster"),
        (WFMappingIssueCatalog.ShipNoGyroscope, "Gyroscope"),
        (WFMappingIssueCatalog.ShipNoPowerSource, "GeneratorBasic"),
        (WFMappingIssueCatalog.ShipNoApc, "APCBasic"),
        (WFMappingIssueCatalog.ShipNoBattery, "SMESBasic"),
        (WFMappingIssueCatalog.ShipNoSpawnPoint, "SpawnPointLatejoin"),
    };

    private static List<string> Ids(IEnumerable<WFMappingIssue> issues)
    {
        return issues.Select(issue => issue.RuleId).ToList();
    }

    private static Entity<MapGridComponent> MakeGrid(IEntityManager entMan, MapSystem mapSys,
        ITileDefinitionManager tileDefs, EntityUid map, int width, int height)
    {
        var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(map).MapId);
        var plating = new Tile(tileDefs["Plating"].TileId);
        var cells = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                cells.Add((new Vector2i(x, y), plating));
            }
        }

        mapSys.SetTiles(grid, grid.Comp, cells);
        return grid;
    }

    [Test]
    public async Task ShipRulesClearAsPartsArePlaced()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var issues = entMan.System<WFMappingIssuesSystem>();
        var mapSys = entMan.System<MapSystem>();
        var meta = entMan.System<MetaDataSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Issues Ship", out var created), Is.True);
            var session = created!;

            // An empty level is a map issue and a ship grid count.
            Assert.That(Ids(issues.Check(session, WFMappingIssueScope.Map)),
                Is.EqualTo(new[] { WFMappingIssueCatalog.MapLevelEmpty }));
            Assert.That(Ids(issues.Check(session, WFMappingIssueScope.Ship)),
                Is.EqualTo(new[] { WFMappingIssueCatalog.ShipGridCount }));

            // A bare 9x3 plating grid: every ship error, the name and spawn warnings and the atmos note.
            var grid = MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 9, 3);
            var found = issues.Check(session, WFMappingIssueScope.Ship, out var summary);
            Assert.Multiple(() =>
            {
                Assert.That(Ids(found), Is.SupersetOf(ShipErrors));
                Assert.That(Ids(found), Does.Contain(WFMappingIssueCatalog.ShipNoName));
                Assert.That(Ids(found), Does.Contain(WFMappingIssueCatalog.ShipNoSpawnPoint));
                Assert.That(Ids(found), Does.Contain(WFMappingIssueCatalog.ShipNoAtmos));
                Assert.That(Ids(found), Does.Contain(WFMappingIssueCatalog.ShipAppraisalZero), "Bare plating appraises at nothing.");
                Assert.That(Ids(found), Does.Not.Contain(WFMappingIssueCatalog.ShipGridCount));
                Assert.That(Ids(found), Does.Not.Contain(WFMappingIssueCatalog.ShipNoFloor));
                Assert.That(found.Select(issue => issue.Severity), Is.Ordered, "Errors come first, then warnings, then notes.");
                Assert.That(issues.HasErrors(session, WFMappingIssueScope.Ship), Is.True);
                Assert.That(summary.Levels, Is.EqualTo(1));
                Assert.That(summary.Grids, Is.EqualTo(1));
                Assert.That(summary.Tiles, Is.EqualTo(27));
                Assert.That(summary.Entities, Is.Zero);
            });

            meta.SetEntityName(grid, "Issues Ship");
            Assert.That(Ids(issues.Check(session, WFMappingIssueScope.Ship)),
                Does.Not.Contain(WFMappingIssueCatalog.ShipNoName));

            // Each part clears its rule and no other rule appears.
            var x = 0;
            foreach (var (rule, prototype) in Parts)
            {
                var before = Ids(issues.Check(session, WFMappingIssueScope.Ship));
                Assert.That(before, Does.Contain(rule), $"{rule} should stand before {prototype} is placed.");
                entMan.SpawnEntity(prototype, new EntityCoordinates(grid, x++ + 0.5f, 1.5f));
                var after = Ids(issues.Check(session, WFMappingIssueScope.Ship));
                Assert.That(after, Does.Not.Contain(rule), $"{prototype} should clear {rule}.");
                Assert.That(after.Except(before), Is.Empty, $"{prototype} should raise nothing new.");
            }

            found = issues.Check(session, WFMappingIssueScope.Ship, out summary);
            Assert.Multiple(() =>
            {
                Assert.That(issues.HasErrors(session, WFMappingIssueScope.Ship), Is.False, string.Join(", ", Ids(found)));
                Assert.That(Ids(found), Is.EqualTo(new[] { WFMappingIssueCatalog.ShipNoAtmos }));
                Assert.That(summary.Appraisal, Is.Positive);
                Assert.That(summary.Entities, Is.AtLeast(Parts.Length), "Parts and whatever they hold.");
            });

            // A pipe is enough for the atmos note.
            entMan.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid, 8.5f, 0.5f));
            Assert.That(issues.Check(session, WFMappingIssueScope.Ship), Is.Empty);

            // A price under the appraisal fails the shipyard's floor; the generated price passes.
            var floorIssues = issues.Check(session, WFMappingIssueScope.Ship, price: 1);
            Assert.That(Ids(floorIssues), Is.EqualTo(new[] { WFMappingIssueCatalog.ShipPriceFloor }));
            Assert.That(issues.HasErrors(session, WFMappingIssueScope.Ship, price: 1), Is.True);

            // A second grid is one ship issue; the map check still passes.
            MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 2, 2);
            Assert.That(Ids(issues.Check(session, WFMappingIssueScope.Ship)),
                Is.EqualTo(new[] { WFMappingIssueCatalog.ShipGridCount }));
            Assert.That(issues.Check(session, WFMappingIssueScope.Map), Is.Empty);

            // The network request from a present member is accepted and throttled without throwing.
            RaiseAs(entMan, a, new WFMappingIssuesRequest(WFMappingIssueScope.Ship));
            RaiseAs(entMan, a, new WFMappingIssuesRequest(WFMappingIssueScope.Map));
        });

        await pair.RunTicksSync(2);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapRulesFindWrongPlacements()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var issues = entMan.System<WFMappingIssuesSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Issues Map", out var created), Is.True);
            var session = created!;
            var grid = MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 4, 4);
            Assert.That(issues.Check(session, WFMappingIssueScope.Map), Is.Empty);

            // Two walls on one tile, one wall beside them, a cable pair of one voltage.
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 0.5f, 0.5f));
            var doubled = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 0.5f, 0.5f));
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 1.5f, 0.5f));
            entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, 2.5f, 0.5f));
            var doubledCable = entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, 2.5f, 0.5f));
            entMan.SpawnEntity("CableHV", new EntityCoordinates(grid, 3.5f, 0.5f));
            entMan.SpawnEntity("CableMV", new EntityCoordinates(grid, 3.5f, 0.5f));

            var found = issues.Check(session, WFMappingIssueScope.Map);
            Assert.Multiple(() =>
            {
                Assert.That(Ids(found), Is.EqualTo(new[] { WFMappingIssueCatalog.MapDoubled, WFMappingIssueCatalog.MapDoubled }),
                    "Two tiles hold doubles; cables of different voltages don't count.");
                Assert.That(found[0].Where?.Position, Is.EqualTo(new Vector2(0.5f, 0.5f)), "Issues on one rule follow the grid position.");
                Assert.That(found[1].Where?.Position, Is.EqualTo(new Vector2(2.5f, 0.5f)));
                Assert.That(found[0].Depth, Is.EqualTo(0));
                Assert.That(found[0].Entity, Is.Not.Null);
                Assert.That(found[0].LocArgs.Select(arg => arg.Name), Is.EquivalentTo(new[] { "name", "count" }));
            });

            entMan.DeleteEntity(doubled);
            entMan.DeleteEntity(doubledCable);
            Assert.That(issues.Check(session, WFMappingIssueScope.Map), Is.Empty, "Erasing the extras clears the doubles.");

            // Loose in space, a mob the map file drops, and a denied component.
            var loose = entMan.SpawnEntity("WallSolid", new EntityCoordinates(session.RootMap, 40f, 40f));
            var mouse = entMan.SpawnEntity(Mob, new EntityCoordinates(grid, 1.5f, 1.5f));
            var well = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 2.5f, 2.5f));
            entMan.EnsureComponent<GravityWellComponent>(well);

            found = issues.Check(session, WFMappingIssueScope.Map, out var summary);
            Assert.Multiple(() =>
            {
                Assert.That(Ids(found), Is.EqualTo(new[]
                {
                    WFMappingIssueCatalog.MapDenied,
                    WFMappingIssueCatalog.MapUnsaved,
                    WFMappingIssueCatalog.MapLooseEntity,
                }));
                Assert.That(found[0].Entity, Is.EqualTo(entMan.GetNetEntity(well)));
                Assert.That(found[1].Entity, Is.EqualTo(entMan.GetNetEntity(mouse)));
                Assert.That(found[1].LocArgs.Single(arg => arg.Name == "spawner").Text, Is.EqualTo(Spawner),
                    "The spawner index names the spawner that places the mob.");
                Assert.That(found[2].Entity, Is.EqualTo(entMan.GetNetEntity(loose)));
                Assert.That(summary.Entities, Is.EqualTo(8),
                    "Two walls, three cables, the mob and the well on the grid plus the loose wall; the observer doesn't count.");
                Assert.That(issues.HasErrors(session, WFMappingIssueScope.Map), Is.True);
            });

            // The same rules fire for a ship check, with its own rules on top.
            Assert.That(Ids(issues.Check(session, WFMappingIssueScope.Ship)), Is.SupersetOf(Ids(found)));

            // A long list is cut at the cap and closed with a count.
            for (var i = 0; i < WFMappingIssueCatalog.MaxIssues + 5; i++)
            {
                entMan.SpawnEntity(Mob, new EntityCoordinates(grid, 0.5f, 3.5f));
            }

            found = issues.Check(session, WFMappingIssueScope.Map);
            Assert.Multiple(() =>
            {
                Assert.That(found, Has.Count.EqualTo(WFMappingIssueCatalog.MaxIssues + 1));
                Assert.That(found[^1].RuleId, Is.EqualTo(WFMappingIssueCatalog.More));
                Assert.That(found[^1].LocArgs.Single().Number, Is.EqualTo(8), "208 issues minus the 200 shown.");
            });
        });

        await pair.RunTicksSync(2);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
