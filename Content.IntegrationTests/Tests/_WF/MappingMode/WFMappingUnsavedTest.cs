#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.MappingMode;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Server.Placement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// An entity a map file leaves out, such as a mob, still goes onto a mapper's map, but the mapper is told once that it
/// won't be saved and which spawner to place instead; the index picks the spawner.
/// </summary>
[TestFixture]
public sealed class WFMappingUnsavedTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingUnsavedMobA
  name: Unsaved Mob A
  save: false

- type: entity
  id: WFMappingUnsavedMobB
  name: Unsaved Mob B
  save: false

- type: entity
  id: WFMappingUnsavedMobC
  name: Unsaved Mob C
  save: false

- type: entity
  id: WFMappingUnsavedItem
  name: Unsaved Item

- type: entity
  id: WFMappingUnsavedSpawnWide
  name: Wide Spawner
  components:
  - type: ConditionalSpawner
    prototypes:
    - WFMappingUnsavedMobA
    - WFMappingUnsavedMobB
    - WFMappingUnsavedItem

- type: entity
  id: WFMappingUnsavedSpawnA
  name: A Spawner
  components:
  - type: ConditionalSpawner
    prototypes:
    - WFMappingUnsavedMobA

- type: entity
  id: WFMappingUnsavedSpawnRule
  name: Rule Spawner
  components:
  - type: ConditionalSpawner
    gameRules:
    - Nukeops
    prototypes:
    - WFMappingUnsavedMobC
";

    [Test]
    public async Task IndexPicksSpawners()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var mapping = server.EntMan.System<WFMappingModeSystem>();
        var prototypes = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var index = mapping.SpawnerIndex;
            Assert.Multiple(() =>
            {
                // MobCorgiMouse has a spawner of its own; MobCorgiIan shares SpawnMobCorgi with its family.
                Assert.That(index.TryGetSpawner("MobCorgiMouse", out var mouse), Is.True);
                Assert.That(mouse, Is.EqualTo("SpawnMobCorgiMouse"));
                Assert.That(index.TryGetSpawner("MobCorgiIan", out var ian), Is.True);
                Assert.That(ian, Is.EqualTo("SpawnMobCorgi"));

                // The plain corgi has no spawner; items never enter the index, as they save.
                Assert.That(index.TryGetSpawner("MobCorgi", out _), Is.False);
                Assert.That(index.TryGetSpawner("Crowbar", out _), Is.False);
                Assert.That(WFMappingSpawnerIndex.WontSave(prototypes.Index<EntityPrototype>("MobCorgi")), Is.True);
                Assert.That(WFMappingSpawnerIndex.WontSave(prototypes.Index<EntityPrototype>("MobCorgiMouse")), Is.True);
                Assert.That(WFMappingSpawnerIndex.WontSave(prototypes.Index<EntityPrototype>("Crowbar")), Is.False);

                // The exclusive spawner beats the wide one, which still serves the other mob; a spawner that waits
                // for a game rule serves nobody.
                Assert.That(index.TryGetSpawner("WFMappingUnsavedMobA", out var a), Is.True);
                Assert.That(a, Is.EqualTo("WFMappingUnsavedSpawnA"));
                Assert.That(index.TryGetSpawner("WFMappingUnsavedMobB", out var b), Is.True);
                Assert.That(b, Is.EqualTo("WFMappingUnsavedSpawnWide"));
                Assert.That(index.TryGetSpawner("WFMappingUnsavedMobC", out _), Is.False);
                Assert.That(index.TryGetSpawner("WFMappingUnsavedItem", out _), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EditsGoThroughAndWarnOnce()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var placement = server.ResolveDependency<IPlacementManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);

        // The test client sends engine placement messages, so it is the mapper.
        var a = pair.Player!;
        WFMappingSession session = default!;
        Entity<MapGridComponent> grid = default;
        var plating = new Tile(tileDefs["Plating"].TileId);

        int Count(string prototype) => entMan.EntityQuery<MetaDataComponent>(true)
            .Count(m => m.EntityPrototype?.ID == prototype && !entMan.IsQueuedForDeletion(m.Owner));

        void Draw(string prototype, params Vector2i[] cells) =>
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SpawnEntities,
                cells.ToList(), null, 0, prototype, Angle.Zero));

        void Place(string prototype, float x) => placement.HandleNetMessage(new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            EntityTemplateName = prototype,
            NetCoordinates = entMan.GetNetCoordinates(new EntityCoordinates(grid, new Vector2(x, 0.5f))),
            Align = "None",
            MsgChannel = a.Channel,
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Unsaved Test", out var created), Is.True);
            session = created!;
            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 8).Select(x => (new Vector2i(x, 0), plating)).ToList());
        });

        // A draw of a mob goes through and warns once, however often it is repeated.
        await server.WaitAssertion(() =>
        {
            Draw("WFMappingUnsavedItem", new Vector2i(0, 0));
            Assert.That(mapping.UnsavedWarnings(a.UserId), Is.Empty, "An item saves, so nothing is said.");

            Draw("WFMappingUnsavedMobA", new Vector2i(1, 0), new Vector2i(2, 0));
            Draw("WFMappingUnsavedMobA", new Vector2i(3, 0));
            Assert.Multiple(() =>
            {
                Assert.That(Count("WFMappingUnsavedMobA"), Is.EqualTo(3), "The draws still spawn the mob.");
                Assert.That(mapping.UnsavedWarnings(a.UserId), Is.EqualTo(new[] { "WFMappingUnsavedMobA" }));
            });
        });

        // Engine placement warns on its own prototypes, and a paste on what it carries.
        await server.WaitAssertion(() =>
        {
            Place("WFMappingUnsavedMobB", 4.5f);
            Place("WFMappingUnsavedMobB", 5.5f);
            Assert.That(Count("WFMappingUnsavedMobB"), Is.EqualTo(2));

            var paste = new WFMappingCanEditEvent(a, session.RootMap, WFMappingEditKind.Paste)
            {
                Grid = grid,
                CarriedPrototypes = new[] { "WFMappingUnsavedMobC", "WFMappingUnsavedItem" },
            };
            entMan.EventBus.RaiseEvent(EventSource.Local, paste);
            Assert.Multiple(() =>
            {
                Assert.That(paste.Cancelled, Is.False);
                Assert.That(mapping.UnsavedWarnings(a.UserId),
                    Is.EqualTo(new[] { "WFMappingUnsavedMobA", "WFMappingUnsavedMobB", "WFMappingUnsavedMobC" }));
            });
        });

        // The mapper's client got the last one as a result, in the form without a spawner.
        await pair.RunTicksSync(5);
        var client = pair.Client.EntMan.System<WFMappingClientSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(client.LastResult?.LocKey, Is.EqualTo("wf-mapping-mode-unsaved-none"));
            Assert.That(client.LastResult?.Success, Is.False);
            Assert.That(loc.GetString("wf-mapping-mode-unsaved-spawner",
                    ("name", "Unsaved Mob B"), ("spawner", "Wide Spawner")),
                Is.EqualTo("Unsaved Mob B won't be saved with the map. Place Wide Spawner instead."));
            Assert.That(loc.GetString("wf-mapping-mode-unsaved-none", ("name", "Unsaved Mob C")),
                Is.EqualTo("Unsaved Mob C won't be saved with the map. Nothing spawns it yet; it needs a spawner prototype."));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
