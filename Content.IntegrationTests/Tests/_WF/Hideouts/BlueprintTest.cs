using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._NF.Bank;
using Content.Server._WF.Hideouts;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Hideouts;

/// <summary>The console's blueprint builds a painted plan for its price; a theme swaps a module's materials as it is laid.</summary>
[TestFixture]
[TestOf(typeof(WFHideoutSystem))]
public sealed class BlueprintTest
{
    [Test]
    public async Task APlanIsBuiltAndOnlyWhatIsBuiltIsCharged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var maps = server.System<SharedMapSystem>();
        var bank = server.System<BankSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        const int start = 100_000;
        await Fund(pair, user, start);

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground), new EntityCoordinates(ground, site.X, site.Y),
                    out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = new Entity<WFOutpostComponent>(grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
                var gridComp = entMan.GetComponent<MapGridComponent>(grid.Value);

                // The tile east of the floor's east edge, and the two past it.
                var floor = new List<Vector2i>();
                var all = maps.GetAllTilesEnumerator(grid.Value, gridComp);
                while (all.MoveNext(out var tile))
                    floor.Add(tile.Value.GridIndices);
                var edge = floor.MaxBy(index => index.X);
                var first = edge + Vector2i.Right;
                var second = first + Vector2i.Right;
                var far = edge + new Vector2i(40, 0);

                var wood = protos.Index<WFBlueprintBrushPrototype>("WFBlueprintWood");
                var wall = protos.Index<WFBlueprintBrushPrototype>("WFBlueprintWallBrick");
                var cable = protos.Index<WFBlueprintBrushPrototype>("WFBlueprintCableLv");
                var roof = protos.Index<WFBlueprintBrushPrototype>("WFBlueprintRoof");
                var plan = new List<WFBlueprintStroke>
                {
                    new(first, wood.ID, Direction.South),
                    new(second, wood.ID, Direction.South),
                    new(second, wall.ID, Direction.South),
                    new(second, cable.ID, Direction.South),
                    new(first, roof.ID, Direction.South),
                    // No floor out here, and too far to lay any.
                    new(far, wall.ID, Direction.South),
                };

                var built = hideouts.TryApplyBlueprint(user, outpost, plan, out var spent, out reason);
                Assert.That(built, Is.EqualTo(5), $"Not everything that could be built was: {reason}.");
                Assert.That(reason, Is.Not.Null, "The wall with no floor under it wasn't refused.");
                var expected = hideouts.BrushPrice(wood.ID) * 2 + hideouts.BrushPrice(wall.ID) + hideouts.BrushPrice(cable.ID) + hideouts.BrushPrice(roof.ID);
                Assert.That(spent, Is.EqualTo(expected));
                bank.TryGetBalance(pair.Player, out var balance);
                Assert.That(balance, Is.EqualTo(start - expected), "The refused stroke wasn't paid back.");

                Assert.That(tiles[maps.GetTileRef(grid.Value, gridComp, first).Tile.TypeId].ID, Is.EqualTo("FloorWood"));
                var standing = maps.GetAnchoredEntities(grid.Value, gridComp, second)
                    .Select(ent => entMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID).ToList();
                Assert.That(standing, Does.Contain("WallBrick"), "The wall wasn't set.");
                Assert.That(standing, Does.Contain("CableApcExtension"), "The cable wasn't run under the wall.");

                // A second wall on the same tile is refused and costs nothing; removing takes the first away.
                Assert.That(hideouts.TryApplyBlueprint(user, outpost, new List<WFBlueprintStroke> { new(second, wall.ID, Direction.South) },
                    out spent, out reason), Is.EqualTo(0));
                Assert.That(reason, Is.EqualTo("wf-blueprint-taken"));
                Assert.That(spent, Is.EqualTo(0));
                Assert.That(hideouts.TryApplyBlueprint(user, outpost, new List<WFBlueprintStroke> { new(second, "WFBlueprintErase", Direction.South) },
                    out _, out _), Is.EqualTo(1));
                standing = maps.GetAnchoredEntities(grid.Value, gridComp, second)
                    .Select(ent => entMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID).ToList();
                Assert.That(standing, Does.Not.Contain("WallBrick"), "The wall wasn't removed.");

                // A catalogue's shelf: a tree, which stands unanchored, and a floor decal; both come off again.
                const string tree = "WFBlueprintShelfPlants:FloraTree";
                const string marking = "WFBlueprintShelfMarkings:Bot";
                Assert.That(hideouts.BrushPrice(tree), Is.GreaterThan(0), "The tree's shelf isn't in the palette.");
                Assert.That(hideouts.TryApplyBlueprint(user, outpost, new List<WFBlueprintStroke>
                {
                    new(first, tree, Direction.South),
                    new(first, marking, Direction.East),
                }, out spent, out reason), Is.EqualTo(2), $"The decorations weren't laid: {reason}.");
                Assert.That(spent, Is.EqualTo(hideouts.BrushPrice(tree) + hideouts.BrushPrice(marking)));
                var decals = entMan.GetComponent<DecalGridComponent>(grid.Value);
                Assert.That(decals.ChunkCollection.ChunkCollection.Values.Sum(chunk => chunk.Decals.Count), Is.EqualTo(1), "The decal wasn't laid.");
                Assert.That(Children(entMan, grid.Value).Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID),
                    Does.Contain("FloraTree"), "The tree wasn't planted.");

                Assert.That(hideouts.TryApplyBlueprint(user, outpost, new List<WFBlueprintStroke>
                {
                    new(first, "WFBlueprintErase", Direction.South),
                    new(first, "WFBlueprintCleanDecals", Direction.South),
                }, out _, out _), Is.EqualTo(2), "The tree and the decal didn't both come off.");
                Assert.That(decals.ChunkCollection.ChunkCollection.Values.Sum(chunk => chunk.Decals.Count), Is.EqualTo(0));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AThemeSwapsAModulesMaterialsAndItsDecalsAreLaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var maps = server.System<SharedMapSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        await Fund(pair, user, 0);

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground), new EntityCoordinates(ground, site.X, site.Y),
                    out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = new Entity<WFOutpostComponent>(grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
                var theme = protos.Index<WFHideoutThemePrototype>("WFHideoutThemeBrick");
                var skin = new WFPrefabSkin(theme.Entities.ToDictionary(pair => pair.Key.Id, pair => pair.Value.Id),
                    theme.Tiles.ToDictionary(pair => pair.Key.Id, pair => pair.Value.Id));
                Assert.That(outposts.TryStampPrefab(user, outpost, new ResPath("/SharedMaps/_WF/Hideouts/workshop.yml"),
                    new EntityCoordinates(ground, 8.5f, 0.5f), Direction.East, out reason, skin: skin), Is.True, $"The module was refused: {reason}.");

                var standing = new List<string>();
                var children = entMan.GetComponent<TransformComponent>(grid.Value).ChildEnumerator;
                while (children.MoveNext(out var child))
                    standing.Add(entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID);
                Assert.That(standing, Does.Contain("WallBrick"), "The theme's walls weren't laid.");
                Assert.That(standing, Does.Not.Contain("WFPrefabWallFixed"), "A prefab wall was left in.");
                Assert.That(standing, Does.Contain("WFOutpostFabricator"), "The module lost its furnishing.");

                var gridComp = entMan.GetComponent<MapGridComponent>(grid.Value);
                var laid = new HashSet<string>();
                var all = maps.GetAllTilesEnumerator(grid.Value, gridComp);
                while (all.MoveNext(out var tile))
                    laid.Add(tiles[tile.Value.Tile.TypeId].ID);
                Assert.That(laid, Does.Contain("FloorWood"), "The theme's floor wasn't laid.");
                Assert.That(laid, Does.Not.Contain("WFFloorColonyGrey"), "The prefab's floor was left in.");

                Assert.That(entMan.TryGetComponent<DecalGridComponent>(grid.Value, out var decals), Is.True);
                Assert.That(decals.ChunkCollection.ChunkCollection.Values.Sum(chunk => chunk.Decals.Count), Is.GreaterThan(5),
                    "The module's floor decals weren't laid.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hideout is given a save slot without its owner filing a manifest, and its autosave is spread over ticks: no
    /// tick spends long on it, and the save that comes out holds the whole outpost.
    /// </summary>
    [Test]
    public async Task AHideoutKeepsItselfAFewMillisecondsATick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var owner = pair.Player.UserId;
        await Fund(pair, user, 0);
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Outposts.OutpostCVars.AutosaveInterval, 3f));

        try
        {
            EntityUid outpostUid = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground), new EntityCoordinates(ground, site.X, site.Y),
                    out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpostUid = grid.Value;
                var outpost = new Entity<WFOutpostComponent>(grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
                Assert.That(outposts.TryStampPrefab(user, outpost, new ResPath("/SharedMaps/_WF/Hideouts/workshop.yml"),
                    new EntityCoordinates(ground, 8.5f, 0.5f), Direction.East, out reason), Is.True, $"The module was refused: {reason}.");
            });

            // The slot is given once the owner's saves are read, then the save runs over the ticks after.
            var comp = entMan.GetComponent<WFOutpostComponent>(outpostUid);
            await PoolManager.WaitUntil(server, () => outposts.GetSlots(owner) is { } slots && slots.Any(slot => slot != null && slot.Entities > 50),
                maxTicks: 1800);

            await server.WaitAssertion(() =>
            {
                Assert.That(comp.Slot, Is.Not.Null, "The outpost was never given a save slot.");
                Assert.That(outposts.LastSlicedSave, Is.Not.Null, "The save wasn't spread over ticks.");
                var (entities, ms, ticks, worst, torn) = outposts.LastSlicedSave!.Value;
                Assert.That(torn, Is.False, "A save of an outpost nobody touched came out torn.");
                Assert.That(ticks, Is.GreaterThan(1), "The whole save was taken in one tick.");
                Assert.That(entities, Is.GreaterThan(50), "The save left most of the outpost out.");
                TestContext.Out.WriteLine($"Sliced save, cold: {entities} objects, {ms:0.0} ms over {ticks} ticks, longest {worst:0.0} ms.");
            });

            // A second save, now that the serialiser has seen every component once: this is what a running server pays.
            var before = outposts.LastSlicedSave;
            await server.WaitAssertion(() =>
            {
                var outpost = new Entity<WFOutpostComponent>(outpostUid, comp);
                Assert.That(outposts.TryStampPrefab(user, outpost, new ResPath("/SharedMaps/_WF/Hideouts/vault.yml"),
                    new EntityCoordinates(ground, 8.5f, 11.5f), Direction.East, out var reason), Is.True, $"The second module was refused: {reason}.");
            });
            await PoolManager.WaitUntil(server, () => outposts.LastSlicedSave != before && !outposts.IsSlicing, maxTicks: 1800);
            await server.WaitAssertion(() =>
            {
                var (entities, ms, ticks, worst, torn) = outposts.LastSlicedSave!.Value;
                Assert.That(torn, Is.False, "The second save came out torn.");
                Assert.That(entities, Is.GreaterThan(100), "The second save left the new module out.");
                TestContext.Out.WriteLine($"Sliced save, new module: {entities} objects, {ms:0.0} ms over {ticks} ticks, longest {worst:0.0} ms.");
            });

            // And a third, with nothing new in it but one rack: every component has been through the serialiser by now.
            before = outposts.LastSlicedSave;
            await server.WaitPost(() => entMan.SpawnEntity("Rack", entMan.GetComponent<TransformComponent>(comp.Console).Coordinates));
            await PoolManager.WaitUntil(server, () => outposts.LastSlicedSave != before && !outposts.IsSlicing, maxTicks: 1800);
            await server.WaitAssertion(() =>
            {
                var (entities, ms, ticks, worst, torn) = outposts.LastSlicedSave!.Value;
                Assert.That(torn, Is.False, "The third save came out torn.");
                TestContext.Out.WriteLine($"Sliced save, warm: {entities} objects, {ms:0.0} ms over {ticks} ticks, longest {worst:0.0} ms.");
            });

            // The same outpost saved at once, as it used to be, for comparison: one tick pays for all of it.
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
            await server.WaitAssertion(() =>
            {
                var outpost = new Entity<WFOutpostComponent>(outpostUid, comp);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Assert.That(outposts.TrySave(user, outpost, comp.Slot!.Value, out var reason), Is.True, $"The save at once was refused: {reason}.");
                TestContext.Out.WriteLine($"Save at once: {watch.Elapsed.TotalMilliseconds:0.0} ms in one tick.");
            });
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Outposts.OutpostCVars.AutosaveInterval, 600f));
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static async Task Fund(Pair.TestPair pair, EntityUid user, int balance)
    {
        var server = pair.Server;
        var owner = pair.Player.UserId;
        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(balance));
            server.EntMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
    }
}
