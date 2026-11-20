#nullable enable
using System.Linq;
using Content.Server._WG.Paintings;
using Content.Shared._WG.Paintings;
using Content.Shared.Construction.Prototypes;
using Content.Shared.VendingMachines;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Paintings;

/// <summary>Painting and finishing a canvas, scanning it into the catalogue, framing, gallery frames and blocking.</summary>
[TestFixture]
[TestOf(typeof(WGCanvasSystem))]
public sealed class PaintingsTest
{
    private const string Canvas = "WGCanvas11x11";
    private const string LargeCanvas = "WGCanvas36x24";

    [Test]
    public async Task PaintEraseAndFinish()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (painter, canvasUid) = SpawnPainter(entMan, map.GridCoords, Canvas);
            var canvas = entMan.GetComponent<WGCanvasComponent>(canvasUid);
            var ui = entMan.System<SharedUserInterfaceSystem>();
            ui.OpenUi(canvasUid, WGCanvasUiKey.Key, painter);

            Paint(ui, canvasUid, painter, false, 0, 1, 12);
            Paint(ui, canvasUid, painter, true, 1);
            Assert.Multiple(() =>
            {
                Assert.That(canvas.Used, Is.True);
                Assert.That(Pixel(canvas, 0), Is.EqualTo(Color.Red));
                Assert.That(Pixel(canvas, 1), Is.EqualTo(WGPaintingRules.Blank), "Right click must erase.");
                Assert.That(Pixel(canvas, 12), Is.EqualTo(Color.Red));
                Assert.That(canvas.Data, Is.Not.Empty, "Painted pixels must be kept for saving.");
            });

            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasFinishMessage("  Sunset  ", false) { Actor = painter });
            Assert.Multiple(() =>
            {
                Assert.That(canvas.Finished, Is.True);
                Assert.That(canvas.Title, Is.EqualTo("Sunset"));
                Assert.That(canvas.Artist, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(painter).EntityName));
                Assert.That(canvas.Hash, Is.EqualTo(WGPaintingStoreSystem.Hash(11, 11, canvas.Pixels)));
                Assert.That(canvas.Art, Is.EqualTo(canvas.Pixels));
            });

            Paint(ui, canvasUid, painter, false, 2);
            Assert.That(Pixel(canvas, 2), Is.EqualTo(WGPaintingRules.Blank), "A finished canvas must not take paint.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScanFrameAndBlock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var store = entMan.System<WGPaintingStoreSystem>();
        var map = await pair.CreateTestMap();
        await PoolManager.WaitUntil(server, () => store.Loaded);

        var scanner = Guid.NewGuid();
        EntityUid painter = default;
        EntityUid canvasUid = default;
        EntityUid unfinishedUid = default;
        Task<WGPaintingScanResult>? unfinished = null;
        Task<WGPaintingScanResult>? first = null;
        await server.WaitPost(() =>
        {
            (painter, canvasUid) = SpawnPainter(entMan, map.GridCoords, Canvas);
            var ui = entMan.System<SharedUserInterfaceSystem>();
            ui.OpenUi(canvasUid, WGCanvasUiKey.Key, painter);

            // Unique pixels, as the pooled server keeps the catalogue between tests.
            var noise = (ushort) Random.Shared.Next(0, 121);
            Paint(ui, canvasUid, painter, false, noise, (ushort) ((noise + 7) % 121), (ushort) ((noise + 31) % 121));
            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasFinishMessage(Guid.NewGuid().ToString("N")[..12], true) { Actor = painter });

            unfinishedUid = entMan.SpawnEntity(Canvas, map.GridCoords);
            unfinished = store.Scan((unfinishedUid, entMan.GetComponent<WGCanvasComponent>(unfinishedUid)), painter, scanner);
            first = store.Scan((canvasUid, entMan.GetComponent<WGCanvasComponent>(canvasUid)), painter, scanner);
        });

        await PoolManager.WaitUntil(server, () => first!.IsCompleted && unfinished!.IsCompleted);
        var hash = entMan.GetComponent<WGCanvasComponent>(canvasUid).Hash;
        var unfinishedResult = await unfinished!;
        var firstResult = await first!;
        Assert.Multiple(() =>
        {
            Assert.That(unfinishedResult, Is.EqualTo(WGPaintingScanResult.Unfinished));
            Assert.That(firstResult, Is.EqualTo(WGPaintingScanResult.Stored));
            Assert.That(store.Catalogue.Any(e => e.Hash == hash), Is.True);
            Assert.That(entMan.GetComponent<WGCanvasComponent>(canvasUid).Artist, Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString("wg-canvas-anonymous")));
        });

        Task<WGPaintingScanResult>? second = null;
        await server.WaitPost(() =>
            second = store.Scan((canvasUid, entMan.GetComponent<WGCanvasComponent>(canvasUid)), painter, scanner));
        await PoolManager.WaitUntil(server, () => second!.IsCompleted);
        Assert.That(await second!, Is.EqualTo(WGPaintingScanResult.Known));

        // A large frame turns the small canvas away; a small one takes it.
        EntityUid largeFrame = default;
        EntityUid frame = default;
        EntityUid gallery = default;
        await server.WaitAssertion(() =>
        {
            var hands = entMan.System<SharedHandsSystem>();
            var interaction = entMan.System<SharedInteractionSystem>();
            hands.TryDrop(painter, hands.GetActiveItem(painter)!.Value);
            Assert.That(hands.TryPickupAnyHand(painter, canvasUid), Is.True);

            largeFrame = entMan.SpawnEntity("WGPaintingFrameLarge", map.GridCoords);
            interaction.InteractUsing(painter, canvasUid, largeFrame, map.GridCoords);
            Assert.That(Framed(entMan, largeFrame), Is.Null, "A small canvas must not fit a large frame.");

            frame = entMan.SpawnEntity("WGPaintingFrame", map.GridCoords);
            interaction.InteractUsing(painter, canvasUid, frame, map.GridCoords);
            Assert.That(Framed(entMan, frame), Is.EqualTo(canvasUid));

            gallery = entMan.SpawnEntity("WGPaintingFrameGallery", map.GridCoords);
        });

        await PoolManager.WaitUntil(server, () => Framed(entMan, gallery) != null);
        await server.WaitAssertion(() =>
        {
            var print = entMan.GetComponent<WGCanvasComponent>(Framed(entMan, gallery)!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(print.Print, Is.True);
                Assert.That(print.Finished, Is.True);
                Assert.That(store.Catalogue.Any(e => e.Hash == print.Hash), Is.True,
                    "A gallery frame must hang a catalogue painting.");
                Assert.That(print.Hash, Is.EqualTo(WGPaintingStoreSystem.Hash(print.Width, print.Height, print.Pixels)));
            });
        });

        Task<bool>? block = null;
        await server.WaitPost(() => block = store.SetBlocked(hash, true, "test"));
        await PoolManager.WaitUntil(server, () => block!.IsCompleted);
        var blocked = await block!;
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(blocked, Is.True);
                Assert.That(store.Catalogue.Any(e => e.Hash == hash), Is.False);
                Assert.That(store.IsBlocked(hash), Is.True);
                Assert.That(entMan.Deleted(canvasUid), Is.True, "Blocking must delete the painting in the round.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LargeCanvasNeedsLargeFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (painter, canvasUid) = SpawnPainter(entMan, map.GridCoords, LargeCanvas);
            var ui = entMan.System<SharedUserInterfaceSystem>();
            ui.OpenUi(canvasUid, WGCanvasUiKey.Key, painter);
            Paint(ui, canvasUid, painter, false, 0, 863);
            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasFinishMessage("", false) { Actor = painter });
            var canvas = entMan.GetComponent<WGCanvasComponent>(canvasUid);
            Assert.That(canvas.Title, Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString("wg-canvas-untitled")));
            Assert.That(Pixel(canvas, 863), Is.EqualTo(Color.Red), "The last pixel of a 36x24 canvas must paint.");

            var hands = entMan.System<SharedHandsSystem>();
            hands.TryDrop(painter, hands.GetActiveItem(painter)!.Value);
            Assert.That(hands.TryPickupAnyHand(painter, canvasUid), Is.True);

            var interaction = entMan.System<SharedInteractionSystem>();
            var small = entMan.SpawnEntity("WGPaintingFrame", map.GridCoords);
            interaction.InteractUsing(painter, canvasUid, small, map.GridCoords);
            Assert.That(Framed(entMan, small), Is.Null);

            var large = entMan.SpawnEntity("WGPaintingFrameLarge", map.GridCoords);
            interaction.InteractUsing(painter, canvasUid, large, map.GridCoords);
            Assert.That(Framed(entMan, large), Is.EqualTo(canvasUid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminSearchAndDelete()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var store = entMan.System<WGPaintingStoreSystem>();
        var map = await pair.CreateTestMap();
        await PoolManager.WaitUntil(server, () => store.Loaded);

        var tag = Guid.NewGuid().ToString("N")[..10];
        var title = $"Harbour {tag} at dusk";
        EntityUid canvasUid = default;
        Task<WGPaintingScanResult>? scan = null;
        await server.WaitPost(() =>
        {
            EntityUid painter;
            (painter, canvasUid) = SpawnPainter(entMan, map.GridCoords, LargeCanvas);
            var ui = entMan.System<SharedUserInterfaceSystem>();
            ui.OpenUi(canvasUid, WGCanvasUiKey.Key, painter);
            var noise = (ushort) Random.Shared.Next(0, 864);
            Paint(ui, canvasUid, painter, false, noise, (ushort) ((noise + 101) % 864));
            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasFinishMessage(title, false) { Actor = painter });
            scan = store.Scan((canvasUid, entMan.GetComponent<WGCanvasComponent>(canvasUid)), painter, Guid.NewGuid());
        });

        await PoolManager.WaitUntil(server, () => scan!.IsCompleted);
        Assert.That(await scan!, Is.EqualTo(WGPaintingScanResult.Stored));
        var hash = entMan.GetComponent<WGCanvasComponent>(canvasUid).Hash;

        string[] Found(string query, WGPaintingAdminFilter filter = WGPaintingAdminFilter.All)
        {
            return WGPaintingAdminEui.Search(store, query, filter).Select(e => e.Hash).ToArray();
        }

        Assert.Multiple(() =>
        {
            Assert.That(Found(tag), Does.Contain(hash), "A plain word matches the title.");
            Assert.That(Found($"title:{tag} size:36x24"), Does.Contain(hash), "Field words combine.");
            Assert.That(Found($"\"{tag} at dusk\""), Does.Contain(hash), "A quoted phrase matches whole.");
            Assert.That(Found($"title:\"{tag} at dusk\""), Does.Contain(hash), "A field word qualifies a phrase.");
            Assert.That(Found($"artist:\"{tag} at dusk\""), Is.Empty, "A qualified phrase only looks in its field.");
            Assert.That(Found($"hash:{hash[..12]}"), Is.EqualTo(new[] { hash }));
            Assert.That(Found($"{tag} size:11x11"), Is.Empty, "Every word must match.");
            Assert.That(Found($"artist:{tag}"), Is.Empty, "A field word only looks in its field.");
            Assert.That(Found(tag, WGPaintingAdminFilter.Blocked), Is.Empty);
            Assert.That(Found(tag, WGPaintingAdminFilter.Visible), Does.Contain(hash));
        });

        Task<bool>? delete = null;
        await server.WaitPost(() => delete = store.Delete(hash, "test"));
        await PoolManager.WaitUntil(server, () => delete!.IsCompleted);
        Assert.That(await delete!, Is.True);
        await pair.RunTicksSync(5);

        Task<byte[]?>? pixels = null;
        await server.WaitPost(() => pixels = store.GetAnyPixelsAsync(hash));
        await PoolManager.WaitUntil(server, () => pixels!.IsCompleted);
        var stored = await pixels!;
        Assert.Multiple(() =>
        {
            Assert.That(Found(tag), Is.Empty);
            Assert.That(store.All.Any(e => e.Hash == hash), Is.False);
            Assert.That(entMan.Deleted(canvasUid), Is.True, "Deleting must remove the painting in the round.");
            Assert.That(stored, Is.Null, "The painting must be gone from the database.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaletteSavesColoursAndFrameShowsPainting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hands = entMan.System<SharedHandsSystem>();
            var ui = entMan.System<SharedUserInterfaceSystem>();
            var (painter, canvasUid) = SpawnPainter(entMan, map.GridCoords, Canvas);
            hands.TryDrop(painter, hands.GetActiveItem(painter)!.Value);
            var paletteUid = entMan.SpawnEntity("WGPaintPalette", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(painter, paletteUid), Is.True);
            var palette = entMan.GetComponent<WGPaintPaletteComponent>(paletteUid);
            var starting = palette.Swatches.Count;
            ui.OpenUi(canvasUid, WGCanvasUiKey.Key, painter);

            var teal = new Color(0.1f, 0.6f, 0.55f);
            var stored = WGPaintingRules.Quantize(teal);
            Swatch(ui, canvasUid, painter, teal, false);
            Swatch(ui, canvasUid, painter, stored, false);
            Assert.That(palette.Swatches, Has.Count.EqualTo(starting + 1), "A saved colour must be kept once.");
            Assert.That(palette.Swatches[^1], Is.EqualTo(stored));

            Swatch(ui, canvasUid, painter, teal, true);
            Assert.That(palette.Swatches, Does.Not.Contain(stored));

            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasPickColorMessage(teal) { Actor = painter });
            Assert.Multiple(() =>
            {
                Assert.That(palette.Selected, Is.EqualTo(stored));
                Assert.That(palette.Recent, Is.EqualTo(new[] { Color.Black }), "The colour picked before goes to recent.");
                Assert.That(palette.Swatches, Has.Count.EqualTo(starting), "Picking a colour must not save it.");
            });

            // A framed painting opens up close with an empty hand.
            Paint(ui, canvasUid, painter, false, 3);
            ui.RaiseUiMessage(canvasUid, WGCanvasUiKey.Key, new WGCanvasFinishMessage("Teal", false) { Actor = painter });
            var frame = entMan.SpawnEntity("WGPaintingFrame", map.GridCoords);
            var interaction = entMan.System<SharedInteractionSystem>();
            interaction.InteractHand(painter, frame);
            Assert.That(ui.IsUiOpen(frame, WGPaintingViewUiKey.Key, painter), Is.False, "An empty frame has nothing to show.");

            hands.TryDrop(painter, paletteUid);
            Assert.That(hands.TryPickupAnyHand(painter, canvasUid), Is.True);
            interaction.InteractUsing(painter, canvasUid, frame, map.GridCoords);
            Assert.That(Framed(entMan, frame), Is.EqualTo(canvasUid));

            interaction.InteractHand(painter, frame);
            Assert.That(ui.IsUiOpen(frame, WGPaintingViewUiKey.Key, painter), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A large frame needs two wall tiles facing north or south, hangs across them and blocks overlaps.</summary>
    [Test]
    public async Task LargeFrameSpansTwoWallTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            for (var x = 1; x <= 3; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 1.5f, 0.5f));

            var large = new WGPaintingFrameSpace { Large = true };
            var small = new WGPaintingFrameSpace();
            var first = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var second = new EntityCoordinates(map.Grid, 1.5f, 0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(large.Condition(EntityUid.Invalid, first, Direction.South), Is.True);
                Assert.That(large.Condition(EntityUid.Invalid, first, Direction.North), Is.True);
                Assert.That(large.Condition(EntityUid.Invalid, first, Direction.East), Is.False, "A large frame can't hang on a wall facing east.");
                Assert.That(large.Condition(EntityUid.Invalid, second, Direction.South), Is.False, "The tile east of it must be a wall.");
            });

            var frame = entMan.SpawnEntity("WGPaintingFrameLarge", first);
            new WGHangLargeFrame().PerformAction(frame, null, entMan);
            var position = entMan.GetComponent<TransformComponent>(frame).LocalPosition;
            Assert.Multiple(() =>
            {
                Assert.That(position.X, Is.EqualTo(1f).Within(0.001f), "The frame must hang on the joint of its two tiles.");
                Assert.That(position.Y, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(small.Condition(EntityUid.Invalid, second, Direction.South), Is.False, "The large frame covers the second tile.");
                Assert.That(small.Condition(EntityUid.Invalid, new EntityCoordinates(map.Grid, 2.5f, 0.5f), Direction.South), Is.True);
                Assert.That(large.Condition(EntityUid.Invalid, first, Direction.South), Is.False, "Frames can't overlap.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Painting supplies come only from John Wolfgate's stock; frames need his kits, not planks.</summary>
    [Test]
    public async Task SuppliesAreSoldByJohnWolfgate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var supplies = new[]
        {
            "WGCanvas11x11", "WGCanvas19x19", "WGCanvas23x19", "WGCanvas23x23", "WGCanvas24x24", "WGCanvas36x24",
            "WGCanvas45x27", "WGPaintPalette", "WGPaintingFrameKit", "WGPaintingFrameLargeKit", "WGEaselFlatpack",
        };

        var wolfgate = protoMan.Index<VendingMachineInventoryPrototype>("WGWolfgateVendInventory");
        Assert.Multiple(() =>
        {
            foreach (var supply in supplies)
            {
                Assert.That(wolfgate.StartingInventory.ContainsKey(supply), Is.True, $"{supply} must be in John Wolfgate's stock.");
                foreach (var inventory in protoMan.EnumeratePrototypes<VendingMachineInventoryPrototype>())
                {
                    if (inventory.ID != wolfgate.ID)
                        Assert.That(inventory.StartingInventory.ContainsKey(supply), Is.False, $"{supply} is also sold by {inventory.ID}.");
                }
            }

            var recipes = protoMan.EnumeratePrototypes<ConstructionPrototype>().Select(r => r.Graph).ToHashSet();
            Assert.That(recipes, Does.Not.Contain("WGEaselGraph"), "The easel comes as a flatpack, not from planks.");
            Assert.That(recipes, Does.Contain("WGPaintingFrameGraph"));
        });

        await pair.CleanReturnAsync();
    }

    private static void Swatch(SharedUserInterfaceSystem ui, EntityUid canvas, EntityUid painter, Color colour, bool remove)
    {
        ui.RaiseUiMessage(canvas, WGCanvasUiKey.Key, new WGCanvasSwatchMessage(colour, remove) { Actor = painter });
    }

    /// <summary>A human holding a red crayon, and a canvas beside them.</summary>
    private static (EntityUid Painter, EntityUid Canvas) SpawnPainter(IEntityManager entMan, EntityCoordinates coords,
        string canvas)
    {
        var painter = entMan.SpawnEntity("MobHuman", coords);
        var crayon = entMan.SpawnEntity("CrayonRed", coords);
        Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(painter, crayon), Is.True);
        return (painter, entMan.SpawnEntity(canvas, coords));
    }

    private static void Paint(SharedUserInterfaceSystem ui, EntityUid canvas, EntityUid painter, bool erase,
        params ushort[] pixels)
    {
        ui.RaiseUiMessage(canvas, WGCanvasUiKey.Key, new WGCanvasPaintMessage(pixels, erase) { Actor = painter });
    }

    private static Color Pixel(WGCanvasComponent canvas, int index)
    {
        return WGPaintingRules.Read(canvas.Pixels, index * 4);
    }

    private static EntityUid? Framed(IEntityManager entMan, EntityUid frame)
    {
        return entMan.System<SharedContainerSystem>().TryGetContainer(frame, WGPaintingFrameComponent.ContainerId,
            out var container) && container.ContainedEntities.Count > 0
            ? container.ContainedEntities[0]
            : null;
    }
}
